using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CallCenter.Application.Authentication.DTOs;
using CallCenter.Application.Reports.DTOs;
using CallCenter.Domain.Entities;
using CallCenter.Domain.Enums;
using CallCenter.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace CallCenter.Tests;

public sealed class OperationsDashboardIntegrationTests : IAsyncLifetime
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

    [Fact]
    public async Task Admin_can_retrieve_full_operations_dashboard()
    {
        await AuthenticateAsync("phase10-admin");

        var response = await client.GetAsync("/api/v1/reports/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dashboard = await response.Content.ReadFromJsonAsync<OperationsDashboardDto>();

        Assert.NotNull(dashboard);
        Assert.NotNull(dashboard.Metrics);
        Assert.NotNull(dashboard.DailyStats);
        Assert.NotNull(dashboard.WeeklyStats);
        Assert.NotNull(dashboard.MonthlyStats);
        Assert.NotNull(dashboard.CallTrends);
        Assert.Equal(7, dashboard.CallTrends.Count);
        Assert.NotNull(dashboard.AgentStatus);
        Assert.NotNull(dashboard.QueueStatus);

        // Verify default seed state
        Assert.Equal(0, dashboard.Metrics.TotalCalls);
        Assert.Equal(0, dashboard.Metrics.Incoming);
        Assert.Equal(0, dashboard.Metrics.Outgoing);
        Assert.Equal(0, dashboard.Metrics.Completed);
        Assert.Equal(0, dashboard.Metrics.Missed);
        Assert.Equal(0, dashboard.Metrics.Rejected);
        Assert.Equal(0.0, dashboard.Metrics.AverageDurationSeconds);
        Assert.Equal(0, dashboard.Metrics.AvailableAgents);
        Assert.Equal(0, dashboard.Metrics.BusyAgents);
        Assert.Equal(0, dashboard.Metrics.QueueSize);
    }

    [Fact]
    public async Task Supervisor_can_retrieve_full_operations_dashboard()
    {
        await AuthenticateAsync("phase10-supervisor");

        var response = await client.GetAsync("/api/v1/reports/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dashboard = await response.Content.ReadFromJsonAsync<OperationsDashboardDto>();
        Assert.NotNull(dashboard);
        Assert.NotNull(dashboard.Metrics);
    }

    [Fact]
    public async Task Agent_cannot_access_operations_dashboard_returns_403_forbidden()
    {
        await AuthenticateAsync("phase10-agent");

        var response = await client.GetAsync("/api/v1/reports/dashboard");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_request_returns_401_unauthorized()
    {
        client.DefaultRequestHeaders.Authorization = null;

        var response = await client.GetAsync("/api/v1/reports/dashboard");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Operations_dashboard_calculates_daily_weekly_monthly_and_trends_accurately()
    {
        var now = DateTime.UtcNow;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();

            // Extra agent 1: Available
            var availAgent = new Agent
            {
                Id = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                EmployeeCode = "AG-AVAIL",
                DisplayName = "Available Agent",
                Status = AgentStatus.Available,
                IsActive = true,
                CreatedAt = now
            };

            // Extra agent 2: Busy
            var busyAgent = new Agent
            {
                Id = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                EmployeeCode = "AG-BUSY",
                DisplayName = "Busy Agent",
                Status = AgentStatus.Busy,
                IsActive = true,
                CreatedAt = now
            };

            db.Agents.AddRange(availAgent, busyAgent);

            // Seed calls across timeframes
            // 1. Today Inbound Completed (duration 120s)
            var call1 = new Call
            {
                Id = Guid.NewGuid(),
                CustomerId = factory.CustomerId,
                AssignedAgentId = availAgent.Id,
                Direction = CallDirection.Inbound,
                Status = CallStatus.Completed,
                PhoneNumber = "8801711111111",
                CorrelationId = Guid.NewGuid().ToString(),
                StartedAt = now.Date.AddHours(2),
                AnsweredAt = now.Date.AddHours(2),
                EndedAt = now.Date.AddHours(2).AddSeconds(120),
                CreatedAt = now
            };

            // 2. Today Outbound Missed/Abandoned
            var call2 = new Call
            {
                Id = Guid.NewGuid(),
                CustomerId = factory.CustomerId,
                AssignedAgentId = busyAgent.Id,
                Direction = CallDirection.Outbound,
                Status = CallStatus.Abandoned,
                PhoneNumber = "8801722222222",
                CorrelationId = Guid.NewGuid().ToString(),
                StartedAt = now.Date.AddHours(3),
                CreatedAt = now
            };

            // 3. Today Inbound Rejected
            var call3 = new Call
            {
                Id = Guid.NewGuid(),
                CustomerId = factory.CustomerId,
                Direction = CallDirection.Inbound,
                Status = CallStatus.Rejected,
                PhoneNumber = "8801733333333",
                CorrelationId = Guid.NewGuid().ToString(),
                StartedAt = now.Date.AddHours(4),
                CreatedAt = now
            };

            // 4. 3 Days Ago: Outbound Completed (duration 60s)
            var call4 = new Call
            {
                Id = Guid.NewGuid(),
                CustomerId = factory.CustomerId,
                AssignedAgentId = availAgent.Id,
                Direction = CallDirection.Outbound,
                Status = CallStatus.Completed,
                PhoneNumber = "8801744444444",
                CorrelationId = Guid.NewGuid().ToString(),
                StartedAt = now.Date.AddDays(-3).AddHours(1),
                AnsweredAt = now.Date.AddDays(-3).AddHours(1),
                EndedAt = now.Date.AddDays(-3).AddHours(1).AddSeconds(60),
                CreatedAt = now.AddDays(-3)
            };

            // 5. 15 Days Ago: Inbound Completed (duration 180s)
            var call5 = new Call
            {
                Id = Guid.NewGuid(),
                CustomerId = factory.CustomerId,
                Direction = CallDirection.Inbound,
                Status = CallStatus.Completed,
                PhoneNumber = "8801755555555",
                CorrelationId = Guid.NewGuid().ToString(),
                StartedAt = now.Date.AddDays(-15).AddHours(1),
                AnsweredAt = now.Date.AddDays(-15).AddHours(1),
                EndedAt = now.Date.AddDays(-15).AddHours(1).AddSeconds(180),
                CreatedAt = now.AddDays(-15)
            };

            // 6. 45 Days Ago: Beyond monthly window
            var call6 = new Call
            {
                Id = Guid.NewGuid(),
                CustomerId = factory.CustomerId,
                Direction = CallDirection.Inbound,
                Status = CallStatus.Completed,
                PhoneNumber = "8801766666666",
                CorrelationId = Guid.NewGuid().ToString(),
                StartedAt = now.Date.AddDays(-45),
                AnsweredAt = now.Date.AddDays(-45),
                EndedAt = now.Date.AddDays(-45).AddSeconds(30),
                CreatedAt = now.AddDays(-45)
            };

            db.Calls.AddRange(call1, call2, call3, call4, call5, call6);

            // Seed Queue
            var queue = new CallQueue
            {
                Id = Guid.NewGuid(),
                Name = "VIP Order Queue",
                Priority = 1,
                IsActive = true,
                CreatedAt = now
            };
            db.CallQueues.Add(queue);

            var queueEntry = new CallQueueEntry
            {
                Id = Guid.NewGuid(),
                CallQueueId = queue.Id,
                CallId = call3.Id,
                Position = 1,
                EnqueuedAt = now.AddMinutes(-5)
            };
            db.CallQueueEntries.Add(queueEntry);

            await db.SaveChangesAsync();
        }

        await AuthenticateAsync("phase10-admin");

        var response = await client.GetAsync("/api/v1/reports/dashboard");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dashboard = await response.Content.ReadFromJsonAsync<OperationsDashboardDto>();
        Assert.NotNull(dashboard);

        // Verify Core Metrics (all time)
        Assert.Equal(6, dashboard.Metrics.TotalCalls);
        Assert.Equal(4, dashboard.Metrics.Incoming);
        Assert.Equal(2, dashboard.Metrics.Outgoing);
        Assert.Equal(4, dashboard.Metrics.Completed);
        Assert.Equal(1, dashboard.Metrics.Missed);
        Assert.Equal(1, dashboard.Metrics.Rejected);
        Assert.Equal(1, dashboard.Metrics.AvailableAgents);
        Assert.Equal(1, dashboard.Metrics.BusyAgents);
        Assert.Equal(1, dashboard.Metrics.QueueSize);

        // Verify Daily Stats (3 calls today: call1, call2, call3)
        Assert.Equal("Today", dashboard.DailyStats.PeriodName);
        Assert.Equal(3, dashboard.DailyStats.TotalCalls);
        Assert.Equal(2, dashboard.DailyStats.Incoming);
        Assert.Equal(1, dashboard.DailyStats.Outgoing);
        Assert.Equal(1, dashboard.DailyStats.Completed);
        Assert.Equal(1, dashboard.DailyStats.Missed);
        Assert.Equal(1, dashboard.DailyStats.Rejected);
        Assert.Equal(120.0, dashboard.DailyStats.AverageDurationSeconds);
        Assert.Equal(33.3, dashboard.DailyStats.CompletionRatePercent);

        // Verify Weekly Stats (4 calls in last 7 days: call1, call2, call3, call4)
        Assert.Equal("Last 7 Days", dashboard.WeeklyStats.PeriodName);
        Assert.Equal(4, dashboard.WeeklyStats.TotalCalls);
        Assert.Equal(2, dashboard.WeeklyStats.Incoming);
        Assert.Equal(2, dashboard.WeeklyStats.Outgoing);
        Assert.Equal(2, dashboard.WeeklyStats.Completed);
        Assert.Equal(1, dashboard.WeeklyStats.Missed);
        Assert.Equal(1, dashboard.WeeklyStats.Rejected);
        Assert.Equal(90.0, dashboard.WeeklyStats.AverageDurationSeconds); // (120 + 60) / 2 = 90
        Assert.Equal(50.0, dashboard.WeeklyStats.CompletionRatePercent);

        // Verify Monthly Stats (5 calls in last 30 days: call1, call2, call3, call4, call5)
        Assert.Equal("Last 30 Days", dashboard.MonthlyStats.PeriodName);
        Assert.Equal(5, dashboard.MonthlyStats.TotalCalls);
        Assert.Equal(3, dashboard.MonthlyStats.Incoming);
        Assert.Equal(2, dashboard.MonthlyStats.Outgoing);
        Assert.Equal(3, dashboard.MonthlyStats.Completed);
        Assert.Equal(1, dashboard.MonthlyStats.Missed);
        Assert.Equal(1, dashboard.MonthlyStats.Rejected);
        Assert.Equal(120.0, dashboard.MonthlyStats.AverageDurationSeconds); // (120 + 60 + 180) / 3 = 120
        Assert.Equal(60.0, dashboard.MonthlyStats.CompletionRatePercent);

        // Verify Trend Points
        Assert.Equal(7, dashboard.CallTrends.Count);
        var todayTrend = dashboard.CallTrends.Last();
        Assert.Equal(3, todayTrend.TotalCalls);
        Assert.Equal(1, todayTrend.CompletedCalls);
        Assert.Equal(1, todayTrend.MissedCalls);
        Assert.Equal(1, todayTrend.RejectedCalls);

        // Verify Queue Status
        Assert.Equal(1, dashboard.QueueStatus.TotalWaiting);
        Assert.True(dashboard.QueueStatus.AverageWaitSeconds > 0);
        Assert.Single(dashboard.QueueStatus.Entries);
        Assert.Equal(1, dashboard.QueueStatus.Entries[0].Position);
    }
}
