using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CallCenter.Application.Authentication.DTOs;
using CallCenter.Application.Routing;
using CallCenter.Application.Routing.DTOs;
using CallCenter.Domain.Entities;
using CallCenter.Domain.Enums;
using CallCenter.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CallCenter.Tests;

public sealed class RoutingEngineIntegrationTests : IAsyncLifetime
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

    private static Call CreateCall(Guid customerId, CallStatus status = CallStatus.Queued, Guid? assignedAgentId = null, Guid? callId = null) => new()
    {
        Id = callId ?? Guid.NewGuid(),
        CustomerId = customerId,
        AssignedAgentId = assignedAgentId,
        Direction = CallDirection.Inbound,
        Status = status,
        PhoneNumber = "8801712345678",
        CorrelationId = $"ROUTING-{Guid.NewGuid():N}",
        StartedAt = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task Route_call_assigns_to_least_busy_agent_via_api()
    {
        await AuthenticateAsync("phase10-admin");

        Guid agent1Id;
        Guid agent2Id;
        Guid callToRouteId = Guid.NewGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var agentRole = await db.Roles.SingleAsync(r => r.Name == "Agent");

            // Agent 1: Busy with an ongoing call
            var user1 = CreateUser(agentRole.Id, "agent-leastbusy-1");
            agent1Id = Guid.NewGuid();
            var agent1 = new Agent { Id = agent1Id, UserId = user1.Id, EmployeeCode = "LB01", DisplayName = "LB Agent 1", Status = AgentStatus.Available, CreatedAt = DateTime.UtcNow.AddMinutes(-20) };

            // Agent 2: Completely free
            var user2 = CreateUser(agentRole.Id, "agent-leastbusy-2");
            agent2Id = Guid.NewGuid();
            var agent2 = new Agent { Id = agent2Id, UserId = user2.Id, EmployeeCode = "LB02", DisplayName = "LB Agent 2", Status = AgentStatus.Available, CreatedAt = DateTime.UtcNow.AddMinutes(-10) };

            db.Users.AddRange(user1, user2);
            db.Agents.AddRange(agent1, agent2);

            // Active call for Agent 1
            var ongoingCall = CreateCall(factory.CustomerId, CallStatus.Connected, agent1Id);
            ongoingCall.StartedAt = DateTime.UtcNow.AddMinutes(-5);

            // Call to route
            var targetCall = CreateCall(factory.CustomerId, CallStatus.Queued, null, callToRouteId);

            db.Calls.AddRange(ongoingCall, targetCall);
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsync($"/api/v1/routing/calls/{callToRouteId}/route", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<RoutingResultDto>();
        Assert.NotNull(result);
        Assert.Equal(callToRouteId, result.CallId);
        Assert.Equal(agent2Id, result.AgentId);
        Assert.Equal(CallStatus.Ringing, result.CallStatus);
        Assert.Equal(nameof(RoutingStrategyType.LeastBusy), result.StrategyUsed);

        // Verify in DB
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var dbCall = await db.Calls.SingleAsync(c => c.Id == callToRouteId);
            Assert.Equal(agent2Id, dbCall.AssignedAgentId);
            Assert.Equal(CallStatus.Ringing, dbCall.Status);
        }
    }

    [Fact]
    public async Task Route_call_with_explicit_strategy_query_param()
    {
        await AuthenticateAsync("phase10-admin");

        Guid agentAId;
        Guid agentBId;
        Guid call1Id = Guid.NewGuid();
        Guid call2Id = Guid.NewGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var agentRole = await db.Roles.SingleAsync(r => r.Name == "Agent");

            var userA = CreateUser(agentRole.Id, "agent-rr-a");
            agentAId = Guid.NewGuid();
            var agentA = new Agent { Id = agentAId, UserId = userA.Id, EmployeeCode = "RRA", DisplayName = "RR Agent A", Status = AgentStatus.Available, CreatedAt = DateTime.UtcNow.AddMinutes(-20) };

            var userB = CreateUser(agentRole.Id, "agent-rr-b");
            agentBId = Guid.NewGuid();
            var agentB = new Agent { Id = agentBId, UserId = userB.Id, EmployeeCode = "RRB", DisplayName = "RR Agent B", Status = AgentStatus.Available, CreatedAt = DateTime.UtcNow.AddMinutes(-10) };

            db.Users.AddRange(userA, userB);
            db.Agents.AddRange(agentA, agentB);

            var call1 = CreateCall(factory.CustomerId, CallStatus.Queued, null, call1Id);
            var call2 = CreateCall(factory.CustomerId, CallStatus.Queued, null, call2Id);

            db.Calls.AddRange(call1, call2);
            await db.SaveChangesAsync();
        }

        // Route call 1 with RoundRobin
        var res1 = await client.PostAsync($"/api/v1/routing/calls/{call1Id}/route?strategy=RoundRobin", null);
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);
        var dto1 = await res1.Content.ReadFromJsonAsync<RoutingResultDto>();
        Assert.NotNull(dto1);
        Assert.Equal(nameof(RoutingStrategyType.RoundRobin), dto1.StrategyUsed);
        Assert.NotNull(dto1.AgentId);

        // Route call 2 with RoundRobin
        var res2 = await client.PostAsync($"/api/v1/routing/calls/{call2Id}/route?strategy=RoundRobin", null);
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);
        var dto2 = await res2.Content.ReadFromJsonAsync<RoutingResultDto>();
        Assert.NotNull(dto2);
        Assert.Equal(nameof(RoutingStrategyType.RoundRobin), dto2.StrategyUsed);
        Assert.NotNull(dto2.AgentId);

        // Round robin distributes across the candidates
        Assert.NotEqual(dto1.AgentId, dto2.AgentId);
    }

    [Fact]
    public async Task Route_call_queues_when_no_agent_available()
    {
        await AuthenticateAsync("phase10-admin");

        Guid callId = Guid.NewGuid();

        // Ensure all seeded agents are Offline
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var agents = await db.Agents.ToListAsync();
            foreach (var a in agents)
            {
                a.Status = AgentStatus.Offline;
            }

            var call = CreateCall(factory.CustomerId, CallStatus.Queued, null, callId);

            db.Calls.Add(call);
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsync($"/api/v1/routing/calls/{callId}/route", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<RoutingResultDto>();
        Assert.NotNull(result);
        Assert.Null(result.AgentId);
        Assert.NotNull(result.QueueId);
        Assert.Equal(CallStatus.Queued, result.CallStatus);

        // Verify CallQueueEntry created in DB
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var queueEntry = await db.CallQueueEntries.FirstOrDefaultAsync(q => q.CallId == callId && q.DequeuedAt == null);
            Assert.NotNull(queueEntry);
        }
    }

    [Fact]
    public async Task Assign_call_directly_and_dequeue_entries()
    {
        await AuthenticateAsync("phase10-admin");

        Guid callId = Guid.NewGuid();
        Guid targetAgentId;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var agentRole = await db.Roles.SingleAsync(r => r.Name == "Agent");

            var user = CreateUser(agentRole.Id, "agent-manual-assign");
            targetAgentId = Guid.NewGuid();
            var agent = new Agent { Id = targetAgentId, UserId = user.Id, EmployeeCode = "MA01", DisplayName = "Manual Agent", Status = AgentStatus.Available, CreatedAt = DateTime.UtcNow };

            var call = CreateCall(factory.CustomerId, CallStatus.Queued, null, callId);

            var queue = new CallQueue { Id = Guid.NewGuid(), Name = "General Queue", IsActive = true, CreatedAt = DateTime.UtcNow };
            var queueEntry = new CallQueueEntry
            {
                Id = Guid.NewGuid(),
                CallQueueId = queue.Id,
                CallId = callId,
                EnqueuedAt = DateTime.UtcNow,
                Position = 1
            };

            db.Users.Add(user);
            db.Agents.Add(agent);
            db.Calls.Add(call);
            db.CallQueues.Add(queue);
            db.CallQueueEntries.Add(queueEntry);
            await db.SaveChangesAsync();
        }

        var request = new AssignCallRequestDto
        {
            AgentId = targetAgentId
        };

        var response = await client.PostAsJsonAsync($"/api/v1/routing/calls/{callId}/assign", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<RoutingResultDto>();
        Assert.NotNull(result);
        Assert.Equal(targetAgentId, result.AgentId);
        Assert.Equal(CallStatus.Ringing, result.CallStatus);

        // Verify DB state: call is Ringing and queue entry dequeued
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var dbCall = await db.Calls.SingleAsync(c => c.Id == callId);
            Assert.Equal(targetAgentId, dbCall.AssignedAgentId);
            Assert.Equal(CallStatus.Ringing, dbCall.Status);

            var dbQueueEntry = await db.CallQueueEntries.SingleAsync(q => q.CallId == callId);
            Assert.NotNull(dbQueueEntry.DequeuedAt);
        }
    }

    [Fact]
    public async Task Reassign_call_to_available_agent()
    {
        await AuthenticateAsync("phase10-admin");

        Guid callId = Guid.NewGuid();
        Guid agent1Id;
        Guid agent2Id;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var agentRole = await db.Roles.SingleAsync(r => r.Name == "Agent");

            var user1 = CreateUser(agentRole.Id, "agent-reassign-1");
            agent1Id = Guid.NewGuid();
            var agent1 = new Agent { Id = agent1Id, UserId = user1.Id, EmployeeCode = "RE01", DisplayName = "Reassign 1", Status = AgentStatus.Available, CreatedAt = DateTime.UtcNow };

            var user2 = CreateUser(agentRole.Id, "agent-reassign-2");
            agent2Id = Guid.NewGuid();
            var agent2 = new Agent { Id = agent2Id, UserId = user2.Id, EmployeeCode = "RE02", DisplayName = "Reassign 2", Status = AgentStatus.Available, CreatedAt = DateTime.UtcNow };

            var call = CreateCall(factory.CustomerId, CallStatus.Ringing, agent1Id, callId);

            db.Users.AddRange(user1, user2);
            db.Agents.AddRange(agent1, agent2);
            db.Calls.Add(call);
            await db.SaveChangesAsync();
        }

        var request = new ReassignCallRequestDto
        {
            AgentId = agent2Id
        };

        var response = await client.PostAsJsonAsync($"/api/v1/routing/calls/{callId}/reassign", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<RoutingResultDto>();
        Assert.NotNull(result);
        Assert.Equal(agent2Id, result.AgentId);
        Assert.Equal(CallStatus.Ringing, result.CallStatus);

        // Verify in DB
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var dbCall = await db.Calls.SingleAsync(c => c.Id == callId);
            Assert.Equal(agent2Id, dbCall.AssignedAgentId);
        }
    }

    [Fact]
    public async Task Cannot_route_call_in_invalid_state()
    {
        await AuthenticateAsync("phase10-admin");

        Guid completedCallId = Guid.NewGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var call = CreateCall(factory.CustomerId, CallStatus.Completed, null, completedCallId);
            call.CreatedAt = DateTime.UtcNow.AddMinutes(-30);
            call.EndedAt = DateTime.UtcNow.AddMinutes(-5);

            db.Calls.Add(call);
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsync($"/api/v1/routing/calls/{completedCallId}/route", null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Agent_cannot_manually_assign_or_reassign_calls()
    {
        await AuthenticateAsync("phase10-agent");

        var assignResponse = await client.PostAsJsonAsync($"/api/v1/routing/calls/{Guid.NewGuid()}/assign", new AssignCallRequestDto
        {
            AgentId = Guid.NewGuid()
        });
        Assert.Equal(HttpStatusCode.Forbidden, assignResponse.StatusCode);

        var reassignResponse = await client.PostAsJsonAsync($"/api/v1/routing/calls/{Guid.NewGuid()}/reassign", new ReassignCallRequestDto
        {
            AgentId = Guid.NewGuid()
        });
        Assert.Equal(HttpStatusCode.Forbidden, reassignResponse.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_request_is_unauthorized()
    {
        client.DefaultRequestHeaders.Authorization = null;

        var response = await client.PostAsync($"/api/v1/routing/calls/{Guid.NewGuid()}/route", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
