using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CallCenter.Application.Authentication.DTOs;
using CallCenter.Application.Calls.DTOs;
using CallCenter.Application.Telephony.DTOs;
using CallCenter.Domain.Entities;
using CallCenter.Domain.Enums;
using CallCenter.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CallCenter.Tests;

public sealed class ActiveCallWorkspaceTests : IAsyncLifetime
{
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

    private static User CreateUser(Guid roleId, string userName) => new()
    {
        Id = Guid.NewGuid(),
        RoleId = roleId,
        UserName = userName,
        PasswordHash = "TestHash123!",
        IsActive = true,
        CreatedAt = DateTime.UtcNow
    };

    private static Call CreateCall(Guid customerId, CallStatus status, Guid? assignedAgentId = null, Guid? callId = null) => new()
    {
        Id = callId ?? Guid.NewGuid(),
        CustomerId = customerId,
        AssignedAgentId = assignedAgentId,
        Direction = CallDirection.Inbound,
        Status = status,
        PhoneNumber = "8801712345678",
        CorrelationId = $"ACTIVE-{Guid.NewGuid():N}",
        StartedAt = DateTime.UtcNow,
        AnsweredAt = status is CallStatus.Connected or CallStatus.OnHold ? DateTime.UtcNow.AddMinutes(-2) : null,
        CreatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task Hold_and_resume_call_transitions_backend_state_and_emits_events()
    {
        await AuthenticateAsync("phase10-admin");

        Guid callId = Guid.NewGuid();
        Guid agentId;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var agentRole = await db.Roles.SingleAsync(r => r.Name == "Agent");
            var user = CreateUser(agentRole.Id, "agent-hold-resume");
            agentId = Guid.NewGuid();
            var agent = new Agent
            {
                Id = agentId,
                UserId = user.Id,
                EmployeeCode = "HR01",
                DisplayName = "Hold Resume Agent",
                Status = AgentStatus.Busy,
                CreatedAt = DateTime.UtcNow
            };

            var call = CreateCall(factory.CustomerId, CallStatus.Connected, agentId, callId);

            db.Users.Add(user);
            db.Agents.Add(agent);
            db.Calls.Add(call);
            await db.SaveChangesAsync();
        }

        // 1. Hold Call
        var holdResponse = await client.PostAsync($"/api/v1/telephony/calls/{callId}/hold", null);
        Assert.Equal(HttpStatusCode.OK, holdResponse.StatusCode);
        var holdBody = await holdResponse.Content.ReadFromJsonAsync<TelephonyCallResponseDto>();
        Assert.NotNull(holdBody);
        Assert.Equal(CallStatus.OnHold, holdBody.Status);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var dbCall = await db.Calls.Include(c => c.Events).SingleAsync(c => c.Id == callId);
            Assert.Equal(CallStatus.OnHold, dbCall.Status);
            Assert.Contains(dbCall.Events, e => e.EventType == "Hold");
        }

        // 2. Resume Call
        var resumeResponse = await client.PostAsync($"/api/v1/telephony/calls/{callId}/resume", null);
        Assert.Equal(HttpStatusCode.OK, resumeResponse.StatusCode);
        var resumeBody = await resumeResponse.Content.ReadFromJsonAsync<TelephonyCallResponseDto>();
        Assert.NotNull(resumeBody);
        Assert.Equal(CallStatus.Connected, resumeBody.Status);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var dbCall = await db.Calls.Include(c => c.Events).SingleAsync(c => c.Id == callId);
            Assert.Equal(CallStatus.Connected, dbCall.Status);
            Assert.Contains(dbCall.Events, e => e.EventType == "Resumed");
        }
    }

    [Fact]
    public async Task Transfer_call_to_another_agent_updates_state_and_frees_previous_agent()
    {
        await AuthenticateAsync("phase10-admin");

        Guid callId = Guid.NewGuid();
        Guid prevAgentId;
        Guid targetAgentId;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var agentRole = await db.Roles.SingleAsync(r => r.Name == "Agent");

            var user1 = CreateUser(agentRole.Id, "agent-transfer-from");
            prevAgentId = Guid.NewGuid();
            var agent1 = new Agent
            {
                Id = prevAgentId,
                UserId = user1.Id,
                EmployeeCode = "TF01",
                DisplayName = "Transfer From Agent",
                Status = AgentStatus.Busy,
                CreatedAt = DateTime.UtcNow
            };

            var user2 = CreateUser(agentRole.Id, "agent-transfer-to");
            targetAgentId = Guid.NewGuid();
            var agent2 = new Agent
            {
                Id = targetAgentId,
                UserId = user2.Id,
                EmployeeCode = "TT01",
                DisplayName = "Transfer To Agent",
                Status = AgentStatus.Available,
                CreatedAt = DateTime.UtcNow
            };

            var call = CreateCall(factory.CustomerId, CallStatus.Connected, prevAgentId, callId);

            db.Users.AddRange(user1, user2);
            db.Agents.AddRange(agent1, agent2);
            db.Calls.Add(call);
            await db.SaveChangesAsync();
        }

        var transferRequest = new TransferCallRequestDto
        {
            TargetAgentId = targetAgentId,
            Reason = "Customer requested technical specialist"
        };

        var response = await client.PostAsJsonAsync($"/api/v1/telephony/calls/{callId}/transfer", transferRequest);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<TelephonyCallResponseDto>();
        Assert.NotNull(body);
        Assert.Equal(CallStatus.Ringing, body.Status);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var dbCall = await db.Calls.Include(c => c.Events).SingleAsync(c => c.Id == callId);
            Assert.Equal(CallStatus.Ringing, dbCall.Status);
            Assert.Equal(targetAgentId, dbCall.AssignedAgentId);

            // Previous agent should be returned to Available
            var prevAgent = await db.Agents.SingleAsync(a => a.Id == prevAgentId);
            Assert.Equal(AgentStatus.Available, prevAgent.Status);

            // Transferred event recorded
            var transferEvent = dbCall.Events.FirstOrDefault(e => e.EventType == "Transferred");
            Assert.NotNull(transferEvent);
            Assert.Contains(transferRequest.Reason, transferEvent.MetadataJson);
        }
    }

    [Fact]
    public async Task Transfer_call_to_queue_enqueues_call_and_frees_agent()
    {
        await AuthenticateAsync("phase10-admin");

        Guid callId = Guid.NewGuid();
        Guid prevAgentId;
        Guid queueId = Guid.NewGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var agentRole = await db.Roles.SingleAsync(r => r.Name == "Agent");

            var user = CreateUser(agentRole.Id, "agent-transfer-queue");
            prevAgentId = Guid.NewGuid();
            var agent = new Agent
            {
                Id = prevAgentId,
                UserId = user.Id,
                EmployeeCode = "TQ01",
                DisplayName = "Transfer Queue Agent",
                Status = AgentStatus.Busy,
                CreatedAt = DateTime.UtcNow
            };

            var queue = new CallQueue
            {
                Id = queueId,
                Name = "VIP Escalation Queue",
                Priority = 10,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            var call = CreateCall(factory.CustomerId, CallStatus.Connected, prevAgentId, callId);

            db.Users.Add(user);
            db.Agents.Add(agent);
            db.CallQueues.Add(queue);
            db.Calls.Add(call);
            await db.SaveChangesAsync();
        }

        var transferRequest = new TransferCallRequestDto
        {
            TargetQueueId = queueId,
            Reason = "Agent shift ending"
        };

        var response = await client.PostAsJsonAsync($"/api/v1/telephony/calls/{callId}/transfer", transferRequest);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<TelephonyCallResponseDto>();
        Assert.NotNull(body);
        Assert.Equal(CallStatus.Queued, body.Status);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var dbCall = await db.Calls.Include(c => c.Events).Include(c => c.QueueEntries).SingleAsync(c => c.Id == callId);
            Assert.Equal(CallStatus.Queued, dbCall.Status);
            Assert.Equal(queueId, dbCall.CallQueueId);
            Assert.Null(dbCall.AssignedAgentId);

            // Queue entry should exist
            Assert.Single(dbCall.QueueEntries);
            Assert.Equal(queueId, dbCall.QueueEntries.First().CallQueueId);

            // Previous agent returned to Available
            var prevAgent = await db.Agents.SingleAsync(a => a.Id == prevAgentId);
            Assert.Equal(AgentStatus.Available, prevAgent.Status);

            // TransferredToQueue event recorded
            var queueEvent = dbCall.Events.FirstOrDefault(e => e.EventType == "TransferredToQueue");
            Assert.NotNull(queueEvent);
            Assert.Contains(transferRequest.Reason, queueEvent.MetadataJson);
        }
    }

    [Fact]
    public async Task End_call_while_on_hold_completes_successfully()
    {
        await AuthenticateAsync("phase10-admin");

        Guid callId = Guid.NewGuid();
        Guid agentId;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var agentRole = await db.Roles.SingleAsync(r => r.Name == "Agent");
            var user = CreateUser(agentRole.Id, "agent-end-hold");
            agentId = Guid.NewGuid();
            var agent = new Agent
            {
                Id = agentId,
                UserId = user.Id,
                EmployeeCode = "EH01",
                DisplayName = "End Hold Agent",
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

        var body = await endResponse.Content.ReadFromJsonAsync<TelephonyCallResponseDto>();
        Assert.NotNull(body);
        Assert.Equal(CallStatus.Completed, body.Status);

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

    [Fact]
    public async Task Update_call_notes_persists_notes_and_records_timeline_event()
    {
        await AuthenticateAsync("phase10-admin");

        Guid callId = Guid.NewGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var call = CreateCall(factory.CustomerId, CallStatus.Connected, null, callId);
            db.Calls.Add(call);
            await db.SaveChangesAsync();
        }

        var updateRequest = new UpdateCallNotesRequestDto
        {
            Notes = "Customer ordered 2x Falaq Special Biryani with extra raita."
        };

        var response = await client.PutAsJsonAsync($"/api/v1/calls/{callId}/notes", updateRequest);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CallResponseDto>();
        Assert.NotNull(body);
        Assert.Equal(updateRequest.Notes, body.Notes);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var dbCall = await db.Calls.Include(c => c.Events).SingleAsync(c => c.Id == callId);
            Assert.Equal(updateRequest.Notes, dbCall.Notes);
            Assert.Contains(dbCall.Events, e => e.EventType == "NotesUpdated");
        }
    }

    [Fact]
    public async Task Get_call_timeline_returns_ordered_event_history()
    {
        await AuthenticateAsync("phase10-admin");

        Guid callId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var call = CreateCall(factory.CustomerId, CallStatus.Connected, null, callId);

            db.Calls.Add(call);
            db.CallEvents.AddRange(
                new CallEvent { Id = Guid.NewGuid(), CallId = callId, EventType = "Initiated", OccurredAt = now.AddMinutes(-5) },
                new CallEvent { Id = Guid.NewGuid(), CallId = callId, EventType = "Ringing", OccurredAt = now.AddMinutes(-4) },
                new CallEvent { Id = Guid.NewGuid(), CallId = callId, EventType = "Connected", OccurredAt = now.AddMinutes(-3) },
                new CallEvent { Id = Guid.NewGuid(), CallId = callId, EventType = "Hold", OccurredAt = now.AddMinutes(-2) },
                new CallEvent { Id = Guid.NewGuid(), CallId = callId, EventType = "Resumed", OccurredAt = now.AddMinutes(-1) }
            );

            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/v1/calls/{callId}/timeline");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var timeline = await response.Content.ReadFromJsonAsync<List<CallTimelineEventDto>>();
        Assert.NotNull(timeline);
        Assert.Equal(5, timeline.Count);

        // Events must be ordered by OccurredAt ascending
        Assert.Equal("Initiated", timeline[0].EventType);
        Assert.Equal("Ringing", timeline[1].EventType);
        Assert.Equal("Connected", timeline[2].EventType);
        Assert.Equal("Hold", timeline[3].EventType);
        Assert.Equal("Resumed", timeline[4].EventType);
    }

    [Fact]
    public async Task Invalid_hold_or_resume_returns_conflict()
    {
        await AuthenticateAsync("phase10-admin");

        Guid callId = Guid.NewGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var call = CreateCall(factory.CustomerId, CallStatus.Queued, null, callId);
            db.Calls.Add(call);
            await db.SaveChangesAsync();
        }

        // Cannot hold a queued call
        var holdResponse = await client.PostAsync($"/api/v1/telephony/calls/{callId}/hold", null);
        Assert.Equal(HttpStatusCode.Conflict, holdResponse.StatusCode);

        // Cannot resume a queued call
        var resumeResponse = await client.PostAsync($"/api/v1/telephony/calls/{callId}/resume", null);
        Assert.Equal(HttpStatusCode.Conflict, resumeResponse.StatusCode);
    }
}
