using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CallCenter.Application.Agents.DTOs;
using CallCenter.Application.Authentication.DTOs;
using CallCenter.Domain.Entities;
using CallCenter.Domain.Enums;
using CallCenter.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace CallCenter.Tests;

public sealed class AgentDashboardIntegrationTests : IAsyncLifetime
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

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto
        {
            UserName = "phase10-agent",
            Password = IntegrationTestFactory.TestPassword
        });
        login.EnsureSuccessStatusCode();
        var body = await login.Content.ReadFromJsonAsync<LoginResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.AccessToken);
    }

    public Task DisposeAsync()
    {
        client.Dispose();
        factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Agent_can_load_own_dashboard()
    {
        var response = await client.GetAsync("/api/v1/agents/me/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dashboard = await response.Content.ReadFromJsonAsync<AgentDashboardResponseDto>();

        Assert.Equal(factory.AgentId, dashboard!.AgentId);
        Assert.Equal(factory.AgentUserId, dashboard.UserId);
        Assert.Equal("Phase 10 Agent", dashboard.DisplayName);
        Assert.Equal(0, dashboard.QueueCount);
        Assert.Null(dashboard.CurrentCall);
        Assert.Equal(0, dashboard.TodaysCallsCount);
        Assert.Equal(0, dashboard.CompletedCallsCount);
        Assert.Equal(0, dashboard.MissedCallsCount);
        Assert.Equal(0.0, dashboard.AverageDurationSeconds);
    }

    [Fact]
    public async Task Agent_can_load_own_dashboard_with_all_metrics()
    {
        // Seed completed call, missed call, active ringing call, and queue entry
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var queue = db.CallQueues.FirstOrDefault() ?? new CallQueue
            {
                Id = Guid.NewGuid(),
                Name = "General Queue",
                Priority = 1,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            if (!db.CallQueues.Any(x => x.Id == queue.Id))
            {
                db.CallQueues.Add(queue);
            }

            // 1. Completed call today (duration: 120s)
            db.Calls.Add(new Call
            {
                Id = Guid.NewGuid(),
                CustomerId = factory.CustomerId,
                AssignedAgentId = factory.AgentId,
                PhoneNumber = "8801712345678",
                CorrelationId = $"COMPLETED-{Guid.NewGuid():N}",
                Direction = CallDirection.Inbound,
                Status = CallStatus.Completed,
                StartedAt = DateTime.UtcNow.AddMinutes(-10),
                AnsweredAt = DateTime.UtcNow.AddMinutes(-10),
                EndedAt = DateTime.UtcNow.AddMinutes(-8),
                CreatedAt = DateTime.UtcNow
            });

            // 2. Missed call today
            db.Calls.Add(new Call
            {
                Id = Guid.NewGuid(),
                CustomerId = factory.CustomerId,
                AssignedAgentId = factory.AgentId,
                PhoneNumber = "8801712345679",
                CorrelationId = $"MISSED-{Guid.NewGuid():N}",
                Direction = CallDirection.Inbound,
                Status = CallStatus.Abandoned,
                StartedAt = DateTime.UtcNow.AddMinutes(-20),
                EndedAt = DateTime.UtcNow.AddMinutes(-19),
                CreatedAt = DateTime.UtcNow
            });

            // 3. Active Ringing call
            db.Calls.Add(new Call
            {
                Id = Guid.NewGuid(),
                CustomerId = factory.CustomerId,
                AssignedAgentId = factory.AgentId,
                PhoneNumber = "8801712345680",
                CorrelationId = $"ACTIVE-{Guid.NewGuid():N}",
                Direction = CallDirection.Inbound,
                Status = CallStatus.Ringing,
                StartedAt = DateTime.UtcNow.AddSeconds(-15),
                CreatedAt = DateTime.UtcNow
            });

            // 4. Queued call
            var queuedCall = new Call
            {
                Id = Guid.NewGuid(),
                CustomerId = factory.CustomerId,
                PhoneNumber = "8801712345681",
                CorrelationId = $"QUEUED-{Guid.NewGuid():N}",
                Direction = CallDirection.Inbound,
                Status = CallStatus.Queued,
                StartedAt = DateTime.UtcNow.AddSeconds(-30),
                CreatedAt = DateTime.UtcNow
            };
            db.Calls.Add(queuedCall);
            db.CallQueueEntries.Add(new CallQueueEntry
            {
                Id = Guid.NewGuid(),
                CallId = queuedCall.Id,
                CallQueueId = queue.Id,
                Position = 1,
                EnqueuedAt = DateTime.UtcNow.AddSeconds(-30)
            });

            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync("/api/v1/agents/me/dashboard");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dashboard = await response.Content.ReadFromJsonAsync<AgentDashboardResponseDto>();
        Assert.NotNull(dashboard);

        // Verify metrics
        Assert.Equal(3, dashboard.TodaysCallsCount);
        Assert.Equal(1, dashboard.CompletedCallsCount);
        Assert.Equal(1, dashboard.MissedCallsCount);
        Assert.True(dashboard.AverageDurationSeconds >= 119 && dashboard.AverageDurationSeconds <= 121);

        // Verify active call
        Assert.NotNull(dashboard.CurrentCall);
        Assert.Equal(CallStatus.Ringing, dashboard.CurrentCall.Status);
        Assert.Equal("8801712345680", dashboard.CurrentCall.PhoneNumber);

        // Verify queue
        Assert.True(dashboard.QueueCount >= 1);
        Assert.NotEmpty(dashboard.IncomingQueue);
        Assert.Equal("8801712345681", dashboard.IncomingQueue[0].PhoneNumber);

        // Verify recent calls list
        Assert.NotEmpty(dashboard.RecentCalls);
    }

    [Fact]
    public async Task Agent_dashboard_isolates_personal_data_only()
    {
        var otherAgentId = Guid.NewGuid();

        // Seed calls for another agent
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var agentRole = db.Roles.Single(x => x.Name == "Agent");

            var otherUser = new User
            {
                Id = Guid.NewGuid(),
                RoleId = agentRole.Id,
                UserName = "other-agent-isolated",
                PasswordHash = "hash",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            db.Users.Add(otherUser);
            db.Agents.Add(new Agent
            {
                Id = otherAgentId,
                UserId = otherUser.Id,
                EmployeeCode = "OTHER-ISO",
                DisplayName = "Other Agent Isolated",
                Status = AgentStatus.Available,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });

            // 5 Completed calls for the other agent
            for (int i = 0; i < 5; i++)
            {
                db.Calls.Add(new Call
                {
                    Id = Guid.NewGuid(),
                    CustomerId = factory.CustomerId,
                    AssignedAgentId = otherAgentId,
                    PhoneNumber = $"880190000000{i}",
                    CorrelationId = $"OTHER-CALL-{i}",
                    Direction = CallDirection.Inbound,
                    Status = CallStatus.Completed,
                    StartedAt = DateTime.UtcNow.AddMinutes(-30),
                    EndedAt = DateTime.UtcNow.AddMinutes(-20),
                    CreatedAt = DateTime.UtcNow
                });
            }

            await db.SaveChangesAsync();
        }

        // Load phase10-agent's dashboard
        var response = await client.GetAsync("/api/v1/agents/me/dashboard");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dashboard = await response.Content.ReadFromJsonAsync<AgentDashboardResponseDto>();
        Assert.NotNull(dashboard);

        // Must NOT include the other agent's 5 completed calls
        Assert.Equal(0, dashboard.TodaysCallsCount);
        Assert.Equal(0, dashboard.CompletedCallsCount);
        Assert.Empty(dashboard.RecentCalls);
    }

    [Fact]
    public async Task Agent_cannot_access_other_agent_details_returns_403_forbidden()
    {
        var otherAgentId = Guid.NewGuid();

        // Attempt to inspect another agent's details
        var response = await client.GetAsync($"/api/v1/agents/{otherAgentId}/details");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Agent_cannot_access_full_agent_roster_returns_403_forbidden()
    {
        // Attempt to list all agents
        var response = await client.GetAsync("/api/v1/agents");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_request_to_dashboard_returns_401_unauthorized()
    {
        client.DefaultRequestHeaders.Authorization = null;

        var response = await client.GetAsync("/api/v1/agents/me/dashboard");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
