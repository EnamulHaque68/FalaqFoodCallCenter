using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CallCenter.Application.Authentication.DTOs;
using CallCenter.Application.Calls.DTOs;
using CallCenter.Application.Telephony.DTOs;
using CallCenter.Domain.Entities;
using CallCenter.Domain.Enums;
using CallCenter.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CallCenter.Tests;

public sealed class HoldAndResumeTests : IAsyncLifetime
{
    private static readonly PasswordHasher<User> Hasher = new();
    private readonly IntegrationTestFactory factory = new();
    private HttpClient client = null!;

    public async Task InitializeAsync()
    {
        client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

        await factory.SeedAsync();
    }

    public Task DisposeAsync()
    {
        client.Dispose();
        factory.Dispose();
        return Task.CompletedTask;
    }

    private async Task AuthenticateAsync(string userName)
    {
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto
        {
            UserName = userName,
            Password = IntegrationTestFactory.TestPassword
        });
        login.EnsureSuccessStatusCode();
        var body = await login.Content.ReadFromJsonAsync<LoginResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.AccessToken);
    }

    private static User CreateUser(Guid roleId, string userName)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            RoleId = roleId,
            UserName = userName,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        user.PasswordHash = Hasher.HashPassword(user, IntegrationTestFactory.TestPassword);
        return user;
    }

    private static Call CreateCall(Guid customerId, CallStatus status, Guid? assignedAgentId = null, Guid? callId = null) => new()
    {
        Id = callId ?? Guid.NewGuid(),
        CustomerId = customerId,
        AssignedAgentId = assignedAgentId,
        Direction = CallDirection.Inbound,
        Status = status,
        PhoneNumber = "8801712345678",
        CorrelationId = $"HOLDRESUME-{Guid.NewGuid():N}",
        StartedAt = DateTime.UtcNow.AddMinutes(-3),
        AnsweredAt = status is CallStatus.Connected or CallStatus.OnHold ? DateTime.UtcNow.AddMinutes(-2) : null,
        CreatedAt = DateTime.UtcNow.AddMinutes(-3)
    };

    [Fact]
    public async Task Connected_to_OnHold_to_Connected_lifecycle_succeeds_with_events()
    {
        Guid callId = Guid.NewGuid();
        Guid agentId;
        string agentUserName = "agent-lifecycle-hold";

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var agentRole = await db.Roles.SingleAsync(r => r.Name == "Agent");

            var user = CreateUser(agentRole.Id, agentUserName);
            agentId = Guid.NewGuid();
            var agent = new Agent
            {
                Id = agentId,
                UserId = user.Id,
                EmployeeCode = "HL01",
                DisplayName = "Hold Lifecycle Agent",
                Status = AgentStatus.Busy,
                CreatedAt = DateTime.UtcNow
            };

            var call = CreateCall(factory.CustomerId, CallStatus.Connected, agentId, callId);

            db.Users.Add(user);
            db.Agents.Add(agent);
            db.Calls.Add(call);
            await db.SaveChangesAsync();
        }

        // Authenticate as the assigned agent
        await AuthenticateAsync(agentUserName);

        // 1. Place Call on Hold
        var holdResponse = await client.PostAsync($"/api/v1/telephony/calls/{callId}/hold", null);
        Assert.Equal(HttpStatusCode.OK, holdResponse.StatusCode);

        var holdBody = await holdResponse.Content.ReadFromJsonAsync<TelephonyCallResponseDto>();
        Assert.NotNull(holdBody);
        Assert.Equal(CallStatus.OnHold, holdBody.Status);

        // Verify DB status and Hold event
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var dbCall = await db.Calls.Include(c => c.Events).SingleAsync(c => c.Id == callId);
            Assert.Equal(CallStatus.OnHold, dbCall.Status);

            var holdEvent = dbCall.Events.FirstOrDefault(e => e.EventType == "Hold");
            Assert.NotNull(holdEvent);
            Assert.Equal(agentId, holdEvent.AgentId);
            Assert.Contains("OnHold", holdEvent.MetadataJson);
        }

        // 2. Resume Call
        var resumeResponse = await client.PostAsync($"/api/v1/telephony/calls/{callId}/resume", null);
        Assert.Equal(HttpStatusCode.OK, resumeResponse.StatusCode);

        var resumeBody = await resumeResponse.Content.ReadFromJsonAsync<TelephonyCallResponseDto>();
        Assert.NotNull(resumeBody);
        Assert.Equal(CallStatus.Connected, resumeBody.Status);

        // Verify DB status and Resumed event with duration
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var dbCall = await db.Calls.Include(c => c.Events).SingleAsync(c => c.Id == callId);
            Assert.Equal(CallStatus.Connected, dbCall.Status);

            var resumeEvent = dbCall.Events.FirstOrDefault(e => e.EventType == "Resumed");
            Assert.NotNull(resumeEvent);
            Assert.Equal(agentId, resumeEvent.AgentId);
            Assert.NotNull(resumeEvent.MetadataJson);
            Assert.Contains("holdDurationSeconds", resumeEvent.MetadataJson);
        }

        // 3. Verify Timeline API returns friendly descriptions
        var timelineResponse = await client.GetAsync($"/api/v1/calls/{callId}/timeline");
        Assert.Equal(HttpStatusCode.OK, timelineResponse.StatusCode);
        var timeline = await timelineResponse.Content.ReadFromJsonAsync<List<CallTimelineEventDto>>();
        Assert.NotNull(timeline);

        Assert.Contains(timeline, t => t.EventType == "Hold" && t.Description == "Call placed on hold");
        Assert.Contains(timeline, t => t.EventType == "Resumed" && t.Description != null && t.Description.StartsWith("Call resumed from hold"));
    }

    [Fact]
    public async Task Unauthorized_agent_cannot_hold_another_agents_call()
    {
        Guid callId = Guid.NewGuid();
        Guid agent1Id;
        Guid agent2Id;
        string agent1UserName = "agent-owner-hold";
        string agent2UserName = "agent-attacker-hold";

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var agentRole = await db.Roles.SingleAsync(r => r.Name == "Agent");

            var user1 = CreateUser(agentRole.Id, agent1UserName);
            agent1Id = Guid.NewGuid();
            var agent1 = new Agent
            {
                Id = agent1Id,
                UserId = user1.Id,
                EmployeeCode = "OW01",
                DisplayName = "Owner Agent",
                Status = AgentStatus.Busy,
                CreatedAt = DateTime.UtcNow
            };

            var user2 = CreateUser(agentRole.Id, agent2UserName);
            agent2Id = Guid.NewGuid();
            var agent2 = new Agent
            {
                Id = agent2Id,
                UserId = user2.Id,
                EmployeeCode = "AT01",
                DisplayName = "Attacker Agent",
                Status = AgentStatus.Available,
                CreatedAt = DateTime.UtcNow
            };

            var call = CreateCall(factory.CustomerId, CallStatus.Connected, agent1Id, callId);

            db.Users.AddRange(user1, user2);
            db.Agents.AddRange(agent1, agent2);
            db.Calls.Add(call);
            await db.SaveChangesAsync();
        }

        // Authenticate as Agent 2 (not assigned to the call)
        await AuthenticateAsync(agent2UserName);

        var response = await client.PostAsync($"/api/v1/telephony/calls/{callId}/hold", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // Verify call remains Connected in DB
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var dbCall = await db.Calls.SingleAsync(c => c.Id == callId);
            Assert.Equal(CallStatus.Connected, dbCall.Status);
        }
    }

    [Fact]
    public async Task Unauthorized_agent_cannot_resume_another_agents_call()
    {
        Guid callId = Guid.NewGuid();
        Guid agent1Id;
        Guid agent2Id;
        string agent1UserName = "agent-owner-resume";
        string agent2UserName = "agent-attacker-resume";

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var agentRole = await db.Roles.SingleAsync(r => r.Name == "Agent");

            var user1 = CreateUser(agentRole.Id, agent1UserName);
            agent1Id = Guid.NewGuid();
            var agent1 = new Agent
            {
                Id = agent1Id,
                UserId = user1.Id,
                EmployeeCode = "OR01",
                DisplayName = "Owner Resume Agent",
                Status = AgentStatus.Busy,
                CreatedAt = DateTime.UtcNow
            };

            var user2 = CreateUser(agentRole.Id, agent2UserName);
            agent2Id = Guid.NewGuid();
            var agent2 = new Agent
            {
                Id = agent2Id,
                UserId = user2.Id,
                EmployeeCode = "AR01",
                DisplayName = "Attacker Resume Agent",
                Status = AgentStatus.Available,
                CreatedAt = DateTime.UtcNow
            };

            var call = CreateCall(factory.CustomerId, CallStatus.OnHold, agent1Id, callId);

            db.Users.AddRange(user1, user2);
            db.Agents.AddRange(agent1, agent2);
            db.Calls.Add(call);
            await db.SaveChangesAsync();
        }

        // Authenticate as Agent 2
        await AuthenticateAsync(agent2UserName);

        var response = await client.PostAsync($"/api/v1/telephony/calls/{callId}/resume", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // Verify call remains OnHold in DB
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var dbCall = await db.Calls.SingleAsync(c => c.Id == callId);
            Assert.Equal(CallStatus.OnHold, dbCall.Status);
        }
    }

    [Fact]
    public async Task Privileged_supervisor_can_hold_and_resume_any_agents_call()
    {
        Guid callId = Guid.NewGuid();
        Guid agentId;
        string supervisorUserName = "sup-override-hold";

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var agentRole = await db.Roles.SingleAsync(r => r.Name == "Agent");
            var supervisorRole = await db.Roles.SingleAsync(r => r.Name == "Supervisor");

            var agentUser = CreateUser(agentRole.Id, "agent-subordinate-hold");
            agentId = Guid.NewGuid();
            var agent = new Agent
            {
                Id = agentId,
                UserId = agentUser.Id,
                EmployeeCode = "SO01",
                DisplayName = "Subordinate Agent",
                Status = AgentStatus.Busy,
                CreatedAt = DateTime.UtcNow
            };

            var supUser = CreateUser(supervisorRole.Id, supervisorUserName);

            var call = CreateCall(factory.CustomerId, CallStatus.Connected, agentId, callId);

            db.Users.AddRange(agentUser, supUser);
            db.Agents.Add(agent);
            db.Calls.Add(call);
            await db.SaveChangesAsync();
        }

        // Authenticate as Supervisor
        await AuthenticateAsync(supervisorUserName);

        // Supervisor holds the subordinate's call
        var holdResponse = await client.PostAsync($"/api/v1/telephony/calls/{callId}/hold", null);
        Assert.Equal(HttpStatusCode.OK, holdResponse.StatusCode);

        // Supervisor resumes the subordinate's call
        var resumeResponse = await client.PostAsync($"/api/v1/telephony/calls/{callId}/resume", null);
        Assert.Equal(HttpStatusCode.OK, resumeResponse.StatusCode);
    }

    [Theory]
    [InlineData(CallStatus.Completed)]
    [InlineData(CallStatus.Abandoned)]
    [InlineData(CallStatus.Rejected)]
    [InlineData(CallStatus.Failed)]
    public async Task Cannot_hold_an_already_ended_call(CallStatus terminalStatus)
    {
        await AuthenticateAsync("phase10-admin");

        Guid callId = Guid.NewGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var call = CreateCall(factory.CustomerId, terminalStatus, null, callId);
            call.EndedAt = DateTime.UtcNow;
            db.Calls.Add(call);
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsync($"/api/v1/telephony/calls/{callId}/hold", null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Cannot_hold_a_call_already_on_hold()
    {
        await AuthenticateAsync("phase10-admin");

        Guid callId = Guid.NewGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var call = CreateCall(factory.CustomerId, CallStatus.OnHold, null, callId);
            db.Calls.Add(call);
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsync($"/api/v1/telephony/calls/{callId}/hold", null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData(CallStatus.Connected)]
    [InlineData(CallStatus.Queued)]
    [InlineData(CallStatus.Ringing)]
    [InlineData(CallStatus.Completed)]
    public async Task Cannot_resume_a_non_held_call(CallStatus nonHeldStatus)
    {
        await AuthenticateAsync("phase10-admin");

        Guid callId = Guid.NewGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var call = CreateCall(factory.CustomerId, nonHeldStatus, null, callId);
            db.Calls.Add(call);
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsync($"/api/v1/telephony/calls/{callId}/resume", null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Ending_call_while_on_hold_completes_and_records_timeline()
    {
        await AuthenticateAsync("phase10-admin");

        Guid callId = Guid.NewGuid();
        Guid agentId;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var agentRole = await db.Roles.SingleAsync(r => r.Name == "Agent");

            var user = CreateUser(agentRole.Id, "agent-end-onhold");
            agentId = Guid.NewGuid();
            var agent = new Agent
            {
                Id = agentId,
                UserId = user.Id,
                EmployeeCode = "EO01",
                DisplayName = "End OnHold Agent",
                Status = AgentStatus.Busy,
                CreatedAt = DateTime.UtcNow
            };

            var call = CreateCall(factory.CustomerId, CallStatus.OnHold, agentId, callId);

            db.Users.Add(user);
            db.Agents.Add(agent);
            db.Calls.Add(call);
            await db.SaveChangesAsync();
        }

        var endResponse = await client.PostAsync($"/api/v1/telephony/calls/{callId}/end", null);
        Assert.Equal(HttpStatusCode.OK, endResponse.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var dbCall = await db.Calls.Include(c => c.Events).SingleAsync(c => c.Id == callId);
            Assert.Equal(CallStatus.Completed, dbCall.Status);
            Assert.NotNull(dbCall.EndedAt);
            Assert.Contains(dbCall.Events, e => e.EventType == "Ended");

            var dbAgent = await db.Agents.SingleAsync(a => a.Id == agentId);
            Assert.Equal(AgentStatus.Available, dbAgent.Status);
        }
    }
}
