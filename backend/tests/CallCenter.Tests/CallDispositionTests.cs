using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CallCenter.Application.Authentication.DTOs;
using CallCenter.Application.Calls.DTOs;
using CallCenter.Application.Dispositions.DTOs;
using CallCenter.Application.Reports.DTOs;
using CallCenter.Application.Telephony.DTOs;
using CallCenter.Domain.Entities;
using CallCenter.Domain.Enums;
using CallCenter.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CallCenter.Tests;

public sealed class CallDispositionTests : IAsyncLifetime
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

    private async Task<(Agent Agent, User User)> SeedAgentAsync(string employeeCode, string name, AgentStatus status = AgentStatus.Available)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();

        var agentRole = await db.Roles.SingleAsync(r => r.Name == "Agent");
        var user = CreateUser(agentRole.Id, $"agent-{employeeCode.ToLowerInvariant()}");
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            EmployeeCode = employeeCode,
            DisplayName = name,
            Status = status,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        db.Users.Add(user);
        db.Agents.Add(agent);
        await db.SaveChangesAsync();

        return (agent, user);
    }

    private async Task<(CallDisposition Disp1, CallDisposition DispFollowUp, CallDisposition DispNotes)> SeedStandardDispositionsAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();

        var d1 = new CallDisposition
        {
            Id = Guid.NewGuid(),
            Code = "ORDER_COMPLETED",
            Name = "Order Completed",
            RequiresFollowUp = false,
            RequiresNotes = false,
            SortOrder = 1,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var d2 = new CallDisposition
        {
            Id = Guid.NewGuid(),
            Code = "FOLLOW_UP_REQUIRED",
            Name = "Follow Up Required",
            RequiresFollowUp = true,
            RequiresNotes = false,
            SortOrder = 2,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var d3 = new CallDisposition
        {
            Id = Guid.NewGuid(),
            Code = "COMPLAINT",
            Name = "Customer Complaint",
            RequiresFollowUp = false,
            RequiresNotes = true,
            SortOrder = 3,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        db.CallDispositions.AddRange(d1, d2, d3);
        await db.SaveChangesAsync();

        return (d1, d2, d3);
    }

    private async Task<Call> SeedCallAsync(Agent agent, CallStatus status = CallStatus.Connected)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();

        var call = new Call
        {
            Id = Guid.NewGuid(),
            CustomerId = factory.CustomerId,
            AssignedAgentId = agent.Id,
            CorrelationId = $"CORR-{Guid.NewGuid():N}",
            Direction = CallDirection.Inbound,
            Status = status,
            PhoneNumber = "8801712345678",
            StartedAt = DateTime.UtcNow.AddMinutes(-5),
            AnsweredAt = DateTime.UtcNow.AddMinutes(-4),
            CreatedAt = DateTime.UtcNow
        };

        db.Calls.Add(call);
        await db.SaveChangesAsync();

        return call;
    }

    [Fact]
    public async Task GetAllDispositions_ReturnsActiveDispositions_ForAuthenticatedAgent()
    {
        var (d1, d2, _) = await SeedStandardDispositionsAsync();
        var (_, agentUser) = await SeedAgentAsync("DISP01", "Agent Disp 1");
        await AuthenticateAsync(agentUser.UserName);

        var response = await client.GetAsync("/api/v1/dispositions");
        response.EnsureSuccessStatusCode();

        var list = await response.Content.ReadFromJsonAsync<List<CallDispositionDto>>();
        Assert.NotNull(list);
        Assert.Contains(list, x => x.Id == d1.Id && x.Name == "Order Completed");
        Assert.Contains(list, x => x.Id == d2.Id && x.RequiresFollowUp);
    }

    [Fact]
    public async Task Admin_CanCreateAndModifyCustomDisposition()
    {
        await AuthenticateAsync("phase10-admin");

        // 1. Create custom disposition
        var createRequest = new CreateDispositionRequestDto
        {
            Code = "VIP_SUPPORT",
            Name = "VIP Escalation Support",
            Description = "Calls handled with priority concierge care",
            RequiresFollowUp = true,
            RequiresNotes = true,
            SortOrder = 10
        };

        var postResponse = await client.PostAsJsonAsync("/api/v1/dispositions", createRequest);
        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);

        var created = await postResponse.Content.ReadFromJsonAsync<CallDispositionDto>();
        Assert.NotNull(created);
        Assert.Equal("VIP_SUPPORT", created.Code);
        Assert.True(created.RequiresFollowUp);
        Assert.True(created.RequiresNotes);

        // 2. Update disposition
        var updateRequest = new UpdateDispositionRequestDto
        {
            Name = "VIP Escalation Care Updated",
            Description = "Updated description",
            RequiresFollowUp = false,
            RequiresNotes = true,
            SortOrder = 5,
            IsActive = true
        };

        var putResponse = await client.PutAsJsonAsync($"/api/v1/dispositions/{created.Id}", updateRequest);
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        var updated = await putResponse.Content.ReadFromJsonAsync<CallDispositionDto>();
        Assert.NotNull(updated);
        Assert.Equal("VIP Escalation Care Updated", updated.Name);
        Assert.False(updated.RequiresFollowUp);

        // 3. Toggle status
        var patchResponse = await client.PatchAsync($"/api/v1/dispositions/{created.Id}/toggle-status", null);
        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);
        var toggled = await patchResponse.Content.ReadFromJsonAsync<CallDispositionDto>();
        Assert.NotNull(toggled);
        Assert.False(toggled.IsActive);
    }

    [Fact]
    public async Task Agent_CannotCreateOrUpdateDispositions_ReturnsForbidden()
    {
        var (_, agentUser) = await SeedAgentAsync("DISP02", "Agent Disp 2");
        await AuthenticateAsync(agentUser.UserName);

        var postResponse = await client.PostAsJsonAsync("/api/v1/dispositions", new CreateDispositionRequestDto
        {
            Code = "AGENT_ILLEGAL",
            Name = "Should Fail"
        });

        Assert.Equal(HttpStatusCode.Forbidden, postResponse.StatusCode);
    }

    [Fact]
    public async Task CompleteCall_RequiresFollowUpDate_WhenDispositionRequiresFollowUp()
    {
        var (agent, agentUser) = await SeedAgentAsync("DISP03", "Agent Disp 3", AgentStatus.Busy);
        var (_, dFollowUp, _) = await SeedStandardDispositionsAsync();
        var call = await SeedCallAsync(agent, CallStatus.Connected);

        await AuthenticateAsync(agentUser.UserName);

        // Call completion without followUpAt when RequiresFollowUp = true should fail
        var request = new CompleteCallRequestDto
        {
            DispositionId = dFollowUp.Id,
            Notes = "Follow up is required but no date provided."
        };

        var response = await client.PostAsJsonAsync($"/api/v1/telephony/calls/{call.Id}/complete", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var errorBody = await response.Content.ReadAsStringAsync();
        Assert.Contains("follow-up date and time is required", errorBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompleteCall_RejectsPastFollowUpDate()
    {
        var (agent, agentUser) = await SeedAgentAsync("DISP04", "Agent Disp 4", AgentStatus.Busy);
        var (_, dFollowUp, _) = await SeedStandardDispositionsAsync();
        var call = await SeedCallAsync(agent, CallStatus.Connected);

        await AuthenticateAsync(agentUser.UserName);

        var request = new CompleteCallRequestDto
        {
            DispositionId = dFollowUp.Id,
            FollowUpAt = DateTime.UtcNow.AddMinutes(-30), // in the past!
            FollowUpNotes = "Call back yesterday"
        };

        var response = await client.PostAsJsonAsync($"/api/v1/telephony/calls/{call.Id}/complete", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var errorBody = await response.Content.ReadAsStringAsync();
        Assert.Contains("must be in the future", errorBody);
    }

    [Fact]
    public async Task CompleteCall_RequiresNotes_WhenDispositionRequiresNotes()
    {
        var (agent, agentUser) = await SeedAgentAsync("DISP05", "Agent Disp 5", AgentStatus.Busy);
        var (_, _, dComplaint) = await SeedStandardDispositionsAsync();
        var call = await SeedCallAsync(agent, CallStatus.Connected);

        await AuthenticateAsync(agentUser.UserName);

        var request = new CompleteCallRequestDto
        {
            DispositionId = dComplaint.Id,
            Notes = "   " // empty/whitespace
        };

        var response = await client.PostAsJsonAsync($"/api/v1/telephony/calls/{call.Id}/complete", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var errorBody = await response.Content.ReadAsStringAsync();
        Assert.Contains("notes are required", errorBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompleteCall_SuccessfulWrapUp_PersistsDetailsAndFreesAgent()
    {
        var (agent, agentUser) = await SeedAgentAsync("DISP06", "Agent Disp 6", AgentStatus.Busy);
        var (_, dFollowUp, _) = await SeedStandardDispositionsAsync();
        var call = await SeedCallAsync(agent, CallStatus.Connected);

        await AuthenticateAsync(agentUser.UserName);

        var followUpDate = DateTime.UtcNow.AddDays(2);
        var request = new CompleteCallRequestDto
        {
            DispositionId = dFollowUp.Id,
            Notes = "Customer ordered combo but wants follow-up for next week event catering.",
            FollowUpAt = followUpDate,
            FollowUpNotes = "Check menu package 3 pricing and phone customer."
        };

        var response = await client.PostAsJsonAsync($"/api/v1/telephony/calls/{call.Id}/complete", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var telephonyResult = await response.Content.ReadFromJsonAsync<TelephonyCallResponseDto>();
        Assert.NotNull(telephonyResult);
        Assert.Equal(call.Id, telephonyResult.CallId);
        Assert.Equal(CallStatus.Completed, telephonyResult.Status);

        // Verify database state
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();

        var updatedCall = await db.Calls
            .Include(c => c.Events)
            .SingleAsync(c => c.Id == call.Id);

        Assert.Equal(CallStatus.Completed, updatedCall.Status);
        Assert.Equal(dFollowUp.Id, updatedCall.CallDispositionId);
        Assert.Equal(request.Notes, updatedCall.Notes);
        Assert.NotNull(updatedCall.FollowUpAt);
        Assert.Equal(request.FollowUpNotes, updatedCall.FollowUpNotes);
        Assert.NotNull(updatedCall.EndedAt);

        // Verify Call Event
        var completedEvent = updatedCall.Events.FirstOrDefault(e => e.EventType == "Completed");
        Assert.NotNull(completedEvent);
        Assert.Contains(dFollowUp.Name, completedEvent.MetadataJson ?? string.Empty);

        // Verify Agent was freed back to Available
        var updatedAgent = await db.Agents.SingleAsync(a => a.Id == agent.Id);
        Assert.Equal(AgentStatus.Available, updatedAgent.Status);
    }

    [Fact]
    public async Task CompleteCall_CanCompleteHeldCall()
    {
        var (agent, agentUser) = await SeedAgentAsync("DISP07", "Agent Disp 7", AgentStatus.Busy);
        var (dOrder, _, _) = await SeedStandardDispositionsAsync();
        var call = await SeedCallAsync(agent, CallStatus.OnHold);

        await AuthenticateAsync(agentUser.UserName);

        var request = new CompleteCallRequestDto
        {
            DispositionId = dOrder.Id,
            Notes = "Completed while on hold after verifying kitchen stock."
        };

        var response = await client.PostAsJsonAsync($"/api/v1/telephony/calls/{call.Id}/complete", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();

        var updatedCall = await db.Calls.SingleAsync(c => c.Id == call.Id);
        Assert.Equal(CallStatus.Completed, updatedCall.Status);
    }

    [Fact]
    public async Task CompleteCall_UnauthorizedAgent_CannotCompleteAnotherAgentsCall()
    {
        var (agent1, _) = await SeedAgentAsync("DISP08A", "Agent A", AgentStatus.Busy);
        var (_, agentUserB) = await SeedAgentAsync("DISP08B", "Agent B", AgentStatus.Available);
        var (dOrder, _, _) = await SeedStandardDispositionsAsync();
        var call = await SeedCallAsync(agent1, CallStatus.Connected);

        // Agent B attempts to complete Agent A's call
        await AuthenticateAsync(agentUserB.UserName);

        var request = new CompleteCallRequestDto
        {
            DispositionId = dOrder.Id
        };

        var response = await client.PostAsJsonAsync($"/api/v1/telephony/calls/{call.Id}/complete", request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DispositionsReport_AggregatesOutcomesAndUpcomingFollowUps()
    {
        var (agent, _) = await SeedAgentAsync("DISP09", "Agent Disp 9");
        var (dOrder, dFollowUp, _) = await SeedStandardDispositionsAsync();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();

            // Seed 2 completed calls with dOrder
            for (int i = 0; i < 2; i++)
            {
                db.Calls.Add(new Call
                {
                    Id = Guid.NewGuid(),
                    CustomerId = factory.CustomerId,
                    AssignedAgentId = agent.Id,
                    CorrelationId = $"CORR-{Guid.NewGuid():N}",
                    Direction = CallDirection.Inbound,
                    Status = CallStatus.Completed,
                    PhoneNumber = "8801712345678",
                    CallDispositionId = dOrder.Id,
                    StartedAt = DateTime.UtcNow.AddHours(-2),
                    AnsweredAt = DateTime.UtcNow.AddHours(-2),
                    EndedAt = DateTime.UtcNow.AddHours(-1),
                    CreatedAt = DateTime.UtcNow.AddHours(-2)
                });
            }

            // Seed 1 completed call with dFollowUp and future FollowUpAt
            db.Calls.Add(new Call
            {
                Id = Guid.NewGuid(),
                CustomerId = factory.CustomerId,
                AssignedAgentId = agent.Id,
                CorrelationId = $"CORR-{Guid.NewGuid():N}",
                Direction = CallDirection.Inbound,
                Status = CallStatus.Completed,
                PhoneNumber = "8801712345678",
                CallDispositionId = dFollowUp.Id,
                StartedAt = DateTime.UtcNow.AddHours(-1),
                AnsweredAt = DateTime.UtcNow.AddHours(-1),
                EndedAt = DateTime.UtcNow.AddMinutes(-30),
                FollowUpAt = DateTime.UtcNow.AddDays(1),
                FollowUpNotes = "Call back for catering confirmation",
                CreatedAt = DateTime.UtcNow.AddHours(-1)
            });

            await db.SaveChangesAsync();
        }

        // Supervisor views disposition report
        await AuthenticateAsync("phase10-supervisor");

        var response = await client.GetAsync("/api/v1/reports/dispositions");
        response.EnsureSuccessStatusCode();

        var report = await response.Content.ReadFromJsonAsync<DispositionReportDto>();
        Assert.NotNull(report);
        Assert.True(report.TotalCompletedCalls >= 3);
        Assert.True(report.TotalDisposedCalls >= 3);
        Assert.True(report.FollowUpsScheduledCount >= 1);

        var orderBreakdown = report.Breakdowns.FirstOrDefault(b => b.DispositionId == dOrder.Id);
        Assert.NotNull(orderBreakdown);
        Assert.Equal(2, orderBreakdown.CallCount);

        var followUpItem = report.UpcomingFollowUps.FirstOrDefault(f => f.DispositionName == dFollowUp.Name);
        Assert.NotNull(followUpItem);
        Assert.Equal("Call back for catering confirmation", followUpItem.FollowUpNotes);
    }
}
