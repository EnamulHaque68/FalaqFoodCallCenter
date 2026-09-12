using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CallCenter.Application.Authentication.DTOs;
using CallCenter.Application.Reports.DTOs;
using CallCenter.Domain.Entities;
using CallCenter.Domain.Enums;
using CallCenter.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CallCenter.Tests;

public sealed class ReportingAndAnalyticsTests : IAsyncLifetime
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
        var user = CreateUser(agentRole.Id, employeeCode.ToLowerInvariant());
        db.Users.Add(user);

        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            DisplayName = name,
            EmployeeCode = employeeCode,
            Team = "Inbound",
            Status = status,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Agents.Add(agent);
        await db.SaveChangesAsync();

        return (agent, user);
    }

    private async Task<Customer> SeedCustomerAsync(string name, string phone)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();

        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            DisplayName = name,
            PhoneNumber = phone,
            Email = $"{name.Replace(" ", "").ToLowerInvariant()}@example.com",
            CreatedAt = DateTime.UtcNow
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        return customer;
    }

    private async Task<CallDisposition> SeedDispositionAsync(string code, string name, bool requiresFollowUp = false, bool requiresNotes = false)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();

        var existing = await db.CallDispositions.SingleOrDefaultAsync(d => d.Code == code);
        if (existing is not null) return existing;

        var disposition = new CallDisposition
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = name,
            Description = $"{name} description",
            RequiresFollowUp = requiresFollowUp,
            RequiresNotes = requiresNotes,
            SortOrder = 1,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.CallDispositions.Add(disposition);
        await db.SaveChangesAsync();

        return disposition;
    }

    [Fact]
    public async Task GetAnalytics_Presets_CalculatesCorrectDateWindows()
    {
        await AuthenticateAsync("phase10-admin");

        // 1. Today
        var respToday = await client.GetAsync("/api/v1/reports/analytics?preset=today");
        respToday.EnsureSuccessStatusCode();
        var dataToday = await respToday.Content.ReadFromJsonAsync<ComprehensiveAnalyticsReportDto>();
        Assert.NotNull(dataToday);
        Assert.Equal("Today", dataToday.PeriodPreset);
        Assert.True(dataToday.ToUtc >= dataToday.FromUtc);

        // 2. Yesterday
        var respYesterday = await client.GetAsync("/api/v1/reports/analytics?preset=yesterday");
        respYesterday.EnsureSuccessStatusCode();
        var dataYesterday = await respYesterday.Content.ReadFromJsonAsync<ComprehensiveAnalyticsReportDto>();
        Assert.NotNull(dataYesterday);
        Assert.Equal("Yesterday", dataYesterday.PeriodPreset);
        Assert.True(dataYesterday.ToUtc >= dataYesterday.FromUtc);

        // 3. ThisWeek
        var respThisWeek = await client.GetAsync("/api/v1/reports/analytics?preset=thisweek");
        respThisWeek.EnsureSuccessStatusCode();
        var dataThisWeek = await respThisWeek.Content.ReadFromJsonAsync<ComprehensiveAnalyticsReportDto>();
        Assert.NotNull(dataThisWeek);
        Assert.Equal("This Week", dataThisWeek.PeriodPreset);

        // 4. ThisMonth
        var respThisMonth = await client.GetAsync("/api/v1/reports/analytics?preset=thismonth");
        respThisMonth.EnsureSuccessStatusCode();
        var dataThisMonth = await respThisMonth.Content.ReadFromJsonAsync<ComprehensiveAnalyticsReportDto>();
        Assert.NotNull(dataThisMonth);
        Assert.Equal("This Month", dataThisMonth.PeriodPreset);

        // 5. Custom
        var customFrom = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var customTo = new DateTime(2026, 7, 10, 23, 59, 59, DateTimeKind.Utc);
        var respCustom = await client.GetAsync($"/api/v1/reports/analytics?fromUtc={customFrom:O}&toUtc={customTo:O}");
        respCustom.EnsureSuccessStatusCode();
        var dataCustom = await respCustom.Content.ReadFromJsonAsync<ComprehensiveAnalyticsReportDto>();
        Assert.NotNull(dataCustom);
        Assert.Equal("Custom", dataCustom.PeriodPreset);
        Assert.Equal(customFrom, dataCustom.FromUtc);
        Assert.Equal(customTo, dataCustom.ToUtc);
    }

    [Fact]
    public async Task GetAnalytics_VolumeAndTalkTime_ComputesExactAggregates()
    {
        await AuthenticateAsync("phase10-admin");

        var cust = await SeedCustomerAsync("Volume Customer", "01710101010");
        var (agent, _) = await SeedAgentAsync("AG-VOL", "Agent Volume");
        var now = DateTime.UtcNow;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();

            // Call 1: Completed, Inbound, duration 120s
            db.Calls.Add(new Call
            {
                Id = Guid.NewGuid(),
                CustomerId = cust.Id,
                AssignedAgentId = agent.Id,
                PhoneNumber = cust.PhoneNumber,
                CorrelationId = "VOL-CALL-1",
                Direction = CallDirection.Inbound,
                Status = CallStatus.Completed,
                StartedAt = now.AddMinutes(-30),
                AnsweredAt = now.AddMinutes(-29),
                EndedAt = now.AddMinutes(-27), // 120s
                CreatedAt = now.AddMinutes(-30)
            });

            // Call 2: Completed, Outbound, duration 60s
            db.Calls.Add(new Call
            {
                Id = Guid.NewGuid(),
                CustomerId = cust.Id,
                AssignedAgentId = agent.Id,
                PhoneNumber = cust.PhoneNumber,
                CorrelationId = "VOL-CALL-2",
                Direction = CallDirection.Outbound,
                Status = CallStatus.Completed,
                StartedAt = now.AddMinutes(-20),
                AnsweredAt = now.AddMinutes(-19),
                EndedAt = now.AddMinutes(-18), // 60s
                CreatedAt = now.AddMinutes(-20)
            });

            // Call 3: Abandoned (Missed), Inbound
            db.Calls.Add(new Call
            {
                Id = Guid.NewGuid(),
                CustomerId = cust.Id,
                PhoneNumber = cust.PhoneNumber,
                CorrelationId = "VOL-CALL-3",
                Direction = CallDirection.Inbound,
                Status = CallStatus.Abandoned,
                StartedAt = now.AddMinutes(-10),
                CreatedAt = now.AddMinutes(-10)
            });

            // Call 4: Rejected, Inbound
            db.Calls.Add(new Call
            {
                Id = Guid.NewGuid(),
                CustomerId = cust.Id,
                PhoneNumber = cust.PhoneNumber,
                CorrelationId = "VOL-CALL-4",
                Direction = CallDirection.Inbound,
                Status = CallStatus.Rejected,
                StartedAt = now.AddMinutes(-5),
                CreatedAt = now.AddMinutes(-5)
            });

            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync("/api/v1/reports/analytics?preset=today");
        response.EnsureSuccessStatusCode();

        var report = await response.Content.ReadFromJsonAsync<ComprehensiveAnalyticsReportDto>();
        Assert.NotNull(report);
        Assert.NotNull(report.Volume);

        Assert.Equal(4, report.Volume.TotalCalls);
        Assert.Equal(3, report.Volume.Incoming);
        Assert.Equal(1, report.Volume.Outgoing);
        Assert.Equal(2, report.Volume.Completed);
        Assert.Equal(1, report.Volume.Missed);
        Assert.Equal(1, report.Volume.Rejected);

        // Total talk time: 120s + 60s = 180s
        Assert.Equal(180, report.Volume.TotalTalkTimeSeconds);
        // Average duration: 180 / 2 = 90s
        Assert.Equal(90.0, report.Volume.AverageDurationSeconds);
    }

    [Fact]
    public async Task GetAnalytics_AgentPerformance_GroupsCorrectlyByAgent()
    {
        await AuthenticateAsync("phase10-admin");

        var cust = await SeedCustomerAsync("AgentPerf Customer", "01720202020");
        var (agent1, _) = await SeedAgentAsync("AG-P1", "Agent Alpha");
        var (agent2, _) = await SeedAgentAsync("AG-P2", "Agent Beta");
        var now = DateTime.UtcNow;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();

            // Agent 1: 2 calls, 1 completed (100s), 1 rejected
            db.Calls.Add(new Call
            {
                Id = Guid.NewGuid(),
                CustomerId = cust.Id,
                AssignedAgentId = agent1.Id,
                PhoneNumber = cust.PhoneNumber,
                CorrelationId = "AP-A1-1",
                Direction = CallDirection.Inbound,
                Status = CallStatus.Completed,
                StartedAt = now.AddMinutes(-25),
                AnsweredAt = now.AddMinutes(-24),
                EndedAt = now.AddMinutes(-24).AddSeconds(100),
                CreatedAt = now.AddMinutes(-25)
            });

            db.Calls.Add(new Call
            {
                Id = Guid.NewGuid(),
                CustomerId = cust.Id,
                AssignedAgentId = agent1.Id,
                PhoneNumber = cust.PhoneNumber,
                CorrelationId = "AP-A1-2",
                Direction = CallDirection.Inbound,
                Status = CallStatus.Rejected,
                StartedAt = now.AddMinutes(-20),
                CreatedAt = now.AddMinutes(-20)
            });

            // Agent 2: 1 call, 1 completed (200s)
            db.Calls.Add(new Call
            {
                Id = Guid.NewGuid(),
                CustomerId = cust.Id,
                AssignedAgentId = agent2.Id,
                PhoneNumber = cust.PhoneNumber,
                CorrelationId = "AP-A2-1",
                Direction = CallDirection.Outbound,
                Status = CallStatus.Completed,
                StartedAt = now.AddMinutes(-15),
                AnsweredAt = now.AddMinutes(-14),
                EndedAt = now.AddMinutes(-14).AddSeconds(200),
                CreatedAt = now.AddMinutes(-15)
            });

            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync("/api/v1/reports/analytics?preset=today");
        response.EnsureSuccessStatusCode();

        var report = await response.Content.ReadFromJsonAsync<ComprehensiveAnalyticsReportDto>();
        Assert.NotNull(report);
        Assert.NotEmpty(report.Agents);

        var a1 = report.Agents.Single(a => a.AgentId == agent1.Id);
        Assert.Equal("Agent Alpha", a1.AgentName);
        Assert.Equal(2, a1.TotalCalls);
        Assert.Equal(1, a1.CompletedCalls);
        Assert.Equal(1, a1.RejectedCalls);
        Assert.Equal(100, a1.TotalTalkTimeSeconds);
        Assert.Equal(100, a1.AverageTalkTimeSeconds);
        Assert.Equal(50.0, a1.CompletionRatePercent);

        var a2 = report.Agents.Single(a => a.AgentId == agent2.Id);
        Assert.Equal("Agent Beta", a2.AgentName);
        Assert.Equal(1, a2.TotalCalls);
        Assert.Equal(1, a2.CompletedCalls);
        Assert.Equal(200, a2.TotalTalkTimeSeconds);
        Assert.Equal(100.0, a2.CompletionRatePercent);
    }

    [Fact]
    public async Task GetAnalytics_QueuePerformance_CalculatesWaitTimesAndCounts()
    {
        await AuthenticateAsync("phase10-admin");

        var now = DateTime.UtcNow;
        Guid queueId = Guid.NewGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();

            var queue = new CallQueue
            {
                Id = queueId,
                Name = "VIP Order Queue",
                Priority = 1,
                IsActive = true,
                CreatedAt = now.AddDays(-1)
            };
            db.CallQueues.Add(queue);

            var cust = new Customer
            {
                Id = Guid.NewGuid(),
                DisplayName = "Queue Customer",
                PhoneNumber = "01730303030",
                CreatedAt = now
            };
            db.Customers.Add(cust);

            var call1 = new Call
            {
                Id = Guid.NewGuid(),
                CustomerId = cust.Id,
                PhoneNumber = cust.PhoneNumber,
                CorrelationId = "Q-CALL-1",
                Direction = CallDirection.Inbound,
                Status = CallStatus.Completed,
                StartedAt = now.AddMinutes(-30),
                CreatedAt = now.AddMinutes(-30)
            };

            var call2 = new Call
            {
                Id = Guid.NewGuid(),
                CustomerId = cust.Id,
                PhoneNumber = cust.PhoneNumber,
                CorrelationId = "Q-CALL-2",
                Direction = CallDirection.Inbound,
                Status = CallStatus.Abandoned,
                StartedAt = now.AddMinutes(-20),
                CreatedAt = now.AddMinutes(-20)
            };

            var call3 = new Call
            {
                Id = Guid.NewGuid(),
                CustomerId = cust.Id,
                PhoneNumber = cust.PhoneNumber,
                CorrelationId = "Q-CALL-3",
                Direction = CallDirection.Inbound,
                Status = CallStatus.Queued,
                StartedAt = now.AddMinutes(-5),
                CreatedAt = now.AddMinutes(-5)
            };

            db.Calls.AddRange(call1, call2, call3);

            // Entry 1: Waited 30s, Answered (Completed)
            db.CallQueueEntries.Add(new CallQueueEntry
            {
                Id = Guid.NewGuid(),
                CallQueueId = queue.Id,
                CallId = call1.Id,
                Position = 1,
                EnqueuedAt = now.AddMinutes(-30),
                DequeuedAt = now.AddMinutes(-30).AddSeconds(30)
            });

            // Entry 2: Waited 60s, Abandoned
            db.CallQueueEntries.Add(new CallQueueEntry
            {
                Id = Guid.NewGuid(),
                CallQueueId = queue.Id,
                CallId = call2.Id,
                Position = 1,
                EnqueuedAt = now.AddMinutes(-20),
                DequeuedAt = now.AddMinutes(-20).AddSeconds(60)
            });

            // Entry 3: Currently waiting in queue
            db.CallQueueEntries.Add(new CallQueueEntry
            {
                Id = Guid.NewGuid(),
                CallQueueId = queue.Id,
                CallId = call3.Id,
                Position = 1,
                EnqueuedAt = now.AddMinutes(-5),
                DequeuedAt = null
            });

            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync("/api/v1/reports/analytics?preset=today");
        response.EnsureSuccessStatusCode();

        var report = await response.Content.ReadFromJsonAsync<ComprehensiveAnalyticsReportDto>();
        Assert.NotNull(report);
        Assert.NotEmpty(report.Queues);

        var q = report.Queues.Single(x => x.QueueId == queueId);
        Assert.Equal("VIP Order Queue", q.QueueName);
        Assert.Equal(3, q.TotalEnqueued);
        Assert.Equal(1, q.AnsweredCalls);
        Assert.Equal(1, q.AbandonedCalls);
        Assert.Equal(1, q.CurrentWaiting);
        // Wait times: 30s and 60s -> avg 45s, max 60s
        Assert.Equal(45.0, q.AverageWaitSeconds);
        Assert.Equal(60.0, q.MaxWaitSeconds);
    }

    [Fact]
    public async Task GetAnalytics_DispositionStatistics_CalculatesPercentagesAndCounts()
    {
        await AuthenticateAsync("phase10-admin");

        var cust = await SeedCustomerAsync("Disp Customer", "01740404040");
        var dInterested = await SeedDispositionAsync("INTERESTED", "Customer Interested");
        var dComplaint = await SeedDispositionAsync("COMPLAINT_R", "Customer Complaint");
        var now = DateTime.UtcNow;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();

            // 3 calls Interested (duration 50s each)
            for (int i = 0; i < 3; i++)
            {
                db.Calls.Add(new Call
                {
                    Id = Guid.NewGuid(),
                    CustomerId = cust.Id,
                    CallDispositionId = dInterested.Id,
                    PhoneNumber = cust.PhoneNumber,
                    CorrelationId = $"DISP-C-INT-{i}",
                    Direction = CallDirection.Inbound,
                    Status = CallStatus.Completed,
                    StartedAt = now.AddMinutes(-20),
                    AnsweredAt = now.AddMinutes(-19),
                    EndedAt = now.AddMinutes(-19).AddSeconds(50),
                    CreatedAt = now.AddMinutes(-20)
                });
            }

            // 1 call Complaint (duration 100s)
            db.Calls.Add(new Call
            {
                Id = Guid.NewGuid(),
                CustomerId = cust.Id,
                CallDispositionId = dComplaint.Id,
                PhoneNumber = cust.PhoneNumber,
                CorrelationId = "DISP-C-COMP-1",
                Direction = CallDirection.Inbound,
                Status = CallStatus.Completed,
                StartedAt = now.AddMinutes(-10),
                AnsweredAt = now.AddMinutes(-9),
                EndedAt = now.AddMinutes(-9).AddSeconds(100),
                CreatedAt = now.AddMinutes(-10)
            });

            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync("/api/v1/reports/analytics?preset=today");
        response.EnsureSuccessStatusCode();

        var report = await response.Content.ReadFromJsonAsync<ComprehensiveAnalyticsReportDto>();
        Assert.NotNull(report);
        Assert.NotEmpty(report.Dispositions);

        var intStat = report.Dispositions.Single(d => d.DispositionId == dInterested.Id);
        Assert.Equal(3, intStat.CallCount);
        Assert.Equal(75.0, intStat.Percentage); // 3 of 4 = 75%
        Assert.Equal(150, intStat.TotalTalkTimeSeconds);
        Assert.Equal(50.0, intStat.AverageDurationSeconds);

        var compStat = report.Dispositions.Single(d => d.DispositionId == dComplaint.Id);
        Assert.Equal(1, compStat.CallCount);
        Assert.Equal(25.0, compStat.Percentage); // 1 of 4 = 25%
        Assert.Equal(100, compStat.TotalTalkTimeSeconds);
    }

    [Fact]
    public async Task GetAnalytics_Charts_GeneratesHourlyAndDailyBuckets()
    {
        await AuthenticateAsync("phase10-admin");

        // Hourly for Today
        var respToday = await client.GetAsync("/api/v1/reports/analytics?preset=today");
        respToday.EnsureSuccessStatusCode();
        var reportToday = await respToday.Content.ReadFromJsonAsync<ComprehensiveAnalyticsReportDto>();
        Assert.NotNull(reportToday);
        Assert.NotNull(reportToday.Charts);
        Assert.NotEmpty(reportToday.Charts.CallsOverTime);
        Assert.NotNull(reportToday.Charts.CallsByDirection);
        Assert.NotNull(reportToday.Charts.CallsByStatus);
        Assert.NotNull(reportToday.Charts.CallsByDisposition);
        Assert.NotNull(reportToday.Charts.CallsByAgent);

        // Daily for ThisMonth
        var respMonth = await client.GetAsync("/api/v1/reports/analytics?preset=thismonth");
        respMonth.EnsureSuccessStatusCode();
        var reportMonth = await respMonth.Content.ReadFromJsonAsync<ComprehensiveAnalyticsReportDto>();
        Assert.NotNull(reportMonth);
        Assert.NotEmpty(reportMonth.Charts.CallsOverTime);
    }

    [Fact]
    public async Task ExportCallReportCsv_GeneratesValidCsvContentAndHeaders()
    {
        await AuthenticateAsync("phase10-admin");

        var cust = await SeedCustomerAsync("CSV Customer", "01750505050");
        var (agent, _) = await SeedAgentAsync("AG-CSV", "Agent CSV");
        var disp = await SeedDispositionAsync("ORDER_CSV", "Order CSV");
        var now = DateTime.UtcNow;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();

            db.Calls.Add(new Call
            {
                Id = Guid.NewGuid(),
                CustomerId = cust.Id,
                AssignedAgentId = agent.Id,
                CallDispositionId = disp.Id,
                PhoneNumber = cust.PhoneNumber,
                CorrelationId = "CSV-CALL-1",
                Direction = CallDirection.Inbound,
                Status = CallStatus.Completed,
                Notes = "Special instructions: handle with care, spicy food.",
                StartedAt = now.AddMinutes(-10),
                AnsweredAt = now.AddMinutes(-9),
                EndedAt = now.AddMinutes(-8),
                CreatedAt = now.AddMinutes(-10)
            });

            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync("/api/v1/reports/export/calls?preset=today");
        response.EnsureSuccessStatusCode();

        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        var csv = await response.Content.ReadAsStringAsync();

        Assert.Contains("CallId,CustomerName,PhoneNumber,AgentName,Direction,Status,StartedAt,AnsweredAt,EndedAt,DurationSeconds,Disposition,Notes", csv);
        Assert.Contains("CSV Customer", csv);
        Assert.Contains("01750505050", csv);
        Assert.Contains("Agent CSV", csv);
        Assert.Contains("Order CSV", csv);
        Assert.Contains("handle with care", csv);
    }

    [Fact]
    public async Task ReportsAuthorization_RequiresReportsViewPolicy()
    {
        // 1. Admin allowed
        await AuthenticateAsync("phase10-admin");
        var respAdmin = await client.GetAsync("/api/v1/reports/analytics?preset=today");
        Assert.Equal(HttpStatusCode.OK, respAdmin.StatusCode);

        // 2. Supervisor allowed
        await AuthenticateAsync("phase10-supervisor");
        var respSupervisor = await client.GetAsync("/api/v1/reports/analytics?preset=today");
        Assert.Equal(HttpStatusCode.OK, respSupervisor.StatusCode);

        // 3. Agent forbidden (403)
        await AuthenticateAsync("phase10-agent");
        var respAgent = await client.GetAsync("/api/v1/reports/analytics?preset=today");
        Assert.Equal(HttpStatusCode.Forbidden, respAgent.StatusCode);
    }
}
