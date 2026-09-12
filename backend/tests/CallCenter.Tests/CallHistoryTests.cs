using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CallCenter.Application.Authentication.DTOs;
using CallCenter.Application.Calls.DTOs;
using CallCenter.Application.Dispositions.DTOs;
using CallCenter.Application.Telephony.DTOs;
using CallCenter.Domain.Entities;
using CallCenter.Domain.Enums;
using CallCenter.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CallCenter.Tests;

public sealed class CallHistoryTests : IAsyncLifetime
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
            Team = "Support",
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
    public async Task GetHistory_Search_FilterByCustomerNamePhoneAndAgent_ReturnsMatchingCalls()
    {
        await AuthenticateAsync("phase10-admin");

        var cust1 = await SeedCustomerAsync("Rahim Khan", "01711000111");
        var cust2 = await SeedCustomerAsync("Karim Ahmed", "01822000222");
        var (agent1, _) = await SeedAgentAsync("AG-H1", "Farhan Agent");
        var (agent2, _) = await SeedAgentAsync("AG-H2", "Sadia Agent");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var now = DateTime.UtcNow;

            db.Calls.Add(new Call
            {
                Id = Guid.NewGuid(),
                CustomerId = cust1.Id,
                AssignedAgentId = agent1.Id,
                PhoneNumber = cust1.PhoneNumber,
                CorrelationId = "H-SEARCH-1",
                Direction = CallDirection.Inbound,
                Status = CallStatus.Completed,
                StartedAt = now.AddMinutes(-20),
                EndedAt = now.AddMinutes(-10),
                CreatedAt = now.AddMinutes(-20)
            });

            db.Calls.Add(new Call
            {
                Id = Guid.NewGuid(),
                CustomerId = cust2.Id,
                AssignedAgentId = agent2.Id,
                PhoneNumber = cust2.PhoneNumber,
                CorrelationId = "H-SEARCH-2",
                Direction = CallDirection.Outbound,
                Status = CallStatus.Connected,
                StartedAt = now.AddMinutes(-5),
                CreatedAt = now.AddMinutes(-5)
            });

            await db.SaveChangesAsync();
        }

        // Search by customer name
        var respCustomer = await client.GetAsync("/api/v1/calls/history?search=Rahim");
        respCustomer.EnsureSuccessStatusCode();
        var dataCustomer = await respCustomer.Content.ReadFromJsonAsync<CallPagedResultDto>();
        Assert.NotNull(dataCustomer);
        Assert.Contains(dataCustomer.Items, c => c.CustomerName.Contains("Rahim"));
        Assert.DoesNotContain(dataCustomer.Items, c => c.CustomerName.Contains("Karim"));

        // Search by phone
        var respPhone = await client.GetAsync("/api/v1/calls/history?search=01822000222");
        respPhone.EnsureSuccessStatusCode();
        var dataPhone = await respPhone.Content.ReadFromJsonAsync<CallPagedResultDto>();
        Assert.NotNull(dataPhone);
        Assert.Contains(dataPhone.Items, c => c.PhoneNumber == "01822000222");

        // Search by agent name
        var respAgent = await client.GetAsync("/api/v1/calls/history?search=Farhan");
        respAgent.EnsureSuccessStatusCode();
        var dataAgent = await respAgent.Content.ReadFromJsonAsync<CallPagedResultDto>();
        Assert.NotNull(dataAgent);
        Assert.Contains(dataAgent.Items, c => c.AgentName != null && c.AgentName.Contains("Farhan"));
    }

    [Fact]
    public async Task GetHistory_DateRange_FiltersCorrectly()
    {
        await AuthenticateAsync("phase10-admin");
        var cust = await SeedCustomerAsync("DateRange Customer", "01755000555");

        var baseTime = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();

            // Call 1: 3 days prior
            db.Calls.Add(new Call
            {
                Id = Guid.NewGuid(),
                CustomerId = cust.Id,
                PhoneNumber = cust.PhoneNumber,
                CorrelationId = "H-DATE-1",
                Direction = CallDirection.Inbound,
                Status = CallStatus.Completed,
                StartedAt = baseTime.AddDays(-3),
                CreatedAt = baseTime.AddDays(-3)
            });

            // Call 2: Within range
            db.Calls.Add(new Call
            {
                Id = Guid.NewGuid(),
                CustomerId = cust.Id,
                PhoneNumber = cust.PhoneNumber,
                CorrelationId = "H-DATE-2",
                Direction = CallDirection.Inbound,
                Status = CallStatus.Completed,
                StartedAt = baseTime,
                CreatedAt = baseTime
            });

            // Call 3: 3 days later
            db.Calls.Add(new Call
            {
                Id = Guid.NewGuid(),
                CustomerId = cust.Id,
                PhoneNumber = cust.PhoneNumber,
                CorrelationId = "H-DATE-3",
                Direction = CallDirection.Inbound,
                Status = CallStatus.Completed,
                StartedAt = baseTime.AddDays(3),
                CreatedAt = baseTime.AddDays(3)
            });

            await db.SaveChangesAsync();
        }

        var fromUtc = baseTime.AddDays(-1).ToString("o");
        var toUtc = baseTime.AddDays(1).ToString("o");

        var response = await client.GetAsync($"/api/v1/calls/history?customerId={cust.Id}&fromUtc={fromUtc}&toUtc={toUtc}");
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CallPagedResultDto>();
        Assert.NotNull(result);
        Assert.Single(result.Items);
        Assert.Equal("H-DATE-2", result.Items[0].CorrelationId);
    }

    [Fact]
    public async Task GetHistory_Filters_ByCustomerAgentDirectionStatusAndDisposition()
    {
        await AuthenticateAsync("phase10-admin");

        var cust = await SeedCustomerAsync("MultiFilter Customer", "01788000888");
        var (agent, _) = await SeedAgentAsync("AG-MF1", "MultiFilter Agent");
        var dispResolved = await SeedDispositionAsync("RESOLVED_MF", "MF Resolved");
        var dispWrong = await SeedDispositionAsync("WRONG_MF", "MF Wrong Number");

        Guid targetCallId = Guid.NewGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var now = DateTime.UtcNow;

            // Target call: customer, agent, Inbound, Completed, dispResolved
            db.Calls.Add(new Call
            {
                Id = targetCallId,
                CustomerId = cust.Id,
                AssignedAgentId = agent.Id,
                CallDispositionId = dispResolved.Id,
                PhoneNumber = cust.PhoneNumber,
                CorrelationId = "H-MULTI-TARGET",
                Direction = CallDirection.Inbound,
                Status = CallStatus.Completed,
                StartedAt = now.AddMinutes(-30),
                EndedAt = now.AddMinutes(-20),
                CreatedAt = now.AddMinutes(-30)
            });

            // Other call with different disposition and direction
            db.Calls.Add(new Call
            {
                Id = Guid.NewGuid(),
                CustomerId = cust.Id,
                AssignedAgentId = agent.Id,
                CallDispositionId = dispWrong.Id,
                PhoneNumber = cust.PhoneNumber,
                CorrelationId = "H-MULTI-OTHER",
                Direction = CallDirection.Outbound,
                Status = CallStatus.Completed,
                StartedAt = now.AddMinutes(-10),
                CreatedAt = now.AddMinutes(-10)
            });

            await db.SaveChangesAsync();
        }

        var url = $"/api/v1/calls/history?customerId={cust.Id}&agentId={agent.Id}&direction={CallDirection.Inbound}&status={CallStatus.Completed}&dispositionId={dispResolved.Id}";
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CallPagedResultDto>();
        Assert.NotNull(result);
        Assert.Single(result.Items);
        Assert.Equal(targetCallId, result.Items[0].Id);
        Assert.Equal("MF Resolved", result.Items[0].DispositionName);
    }

    [Fact]
    public async Task GetHistory_Pagination_ReturnsCorrectPagesAndTotals()
    {
        await AuthenticateAsync("phase10-admin");
        var cust = await SeedCustomerAsync("Pagination Customer", "01799000999");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var now = DateTime.UtcNow;

            for (int i = 1; i <= 7; i++)
            {
                db.Calls.Add(new Call
                {
                    Id = Guid.NewGuid(),
                    CustomerId = cust.Id,
                    PhoneNumber = cust.PhoneNumber,
                    CorrelationId = $"H-PAGE-{i}",
                    Direction = CallDirection.Inbound,
                    Status = CallStatus.Completed,
                    StartedAt = now.AddMinutes(-i),
                    CreatedAt = now.AddMinutes(-i)
                });
            }

            await db.SaveChangesAsync();
        }

        // Page 1 with pageSize 3
        var p1Resp = await client.GetAsync($"/api/v1/calls/history?customerId={cust.Id}&page=1&pageSize=3");
        p1Resp.EnsureSuccessStatusCode();
        var p1 = await p1Resp.Content.ReadFromJsonAsync<CallPagedResultDto>();
        Assert.NotNull(p1);
        Assert.Equal(1, p1.Page);
        Assert.Equal(3, p1.PageSize);
        Assert.Equal(7, p1.TotalCount);
        Assert.Equal(3, p1.TotalPages);
        Assert.False(p1.HasPreviousPage);
        Assert.True(p1.HasNextPage);
        Assert.Equal(3, p1.Items.Count);

        // Page 3 with pageSize 3
        var p3Resp = await client.GetAsync($"/api/v1/calls/history?customerId={cust.Id}&page=3&pageSize=3");
        p3Resp.EnsureSuccessStatusCode();
        var p3 = await p3Resp.Content.ReadFromJsonAsync<CallPagedResultDto>();
        Assert.NotNull(p3);
        Assert.Equal(3, p3.Page);
        Assert.True(p3.HasPreviousPage);
        Assert.False(p3.HasNextPage);
        Assert.Single(p3.Items);
    }

    [Fact]
    public async Task CallTimeline_SourceOfTruth_TracksCompleteCallEventsLifecycle()
    {
        await AuthenticateAsync("phase10-admin");

        var cust = await SeedCustomerAsync("Timeline Customer", "01733000333");
        var (agentA, userA) = await SeedAgentAsync("AG-TIMEA", "Agent Alice", AgentStatus.Available);
        var (agentB, userB) = await SeedAgentAsync("AG-TIMEB", "Agent Bob", AgentStatus.Available);
        var disp = await SeedDispositionAsync("ORDER_TIMELINE", "Order Completed", requiresFollowUp: false, requiresNotes: true);

        // 1. Inbound call simulated -> triggers Incoming, Queued, Assigned, Ringing (since Agent Alice is Available)
        var callCorrelation = $"TIME-CALL-{Guid.NewGuid():N}";
        var callResp = await client.PostAsJsonAsync("/api/v1/telephony/incoming/simulate", new SimulateIncomingCallRequestDto
        {
            PhoneNumber = cust.PhoneNumber,
            CorrelationId = callCorrelation
        });
        callResp.EnsureSuccessStatusCode();
        var callData = await callResp.Content.ReadFromJsonAsync<TelephonyCallResponseDto>();
        Assert.NotNull(callData);
        var callId = callData.CallId;

        // 2. Agent Alice accepts call -> Connected
        await AuthenticateAsync(userA.UserName);
        var acceptResp = await client.PostAsync($"/api/v1/telephony/calls/{callId}/accept", null);
        acceptResp.EnsureSuccessStatusCode();

        // 3. Agent Alice places call on Hold -> Hold
        var holdResp = await client.PostAsync($"/api/v1/telephony/calls/{callId}/hold", null);
        holdResp.EnsureSuccessStatusCode();

        // 4. Agent Alice resumes call -> Resumed
        var resumeResp = await client.PostAsync($"/api/v1/telephony/calls/{callId}/resume", null);
        resumeResp.EnsureSuccessStatusCode();

        // 5. Agent Alice transfers call to Agent Bob -> Transferred
        var transferResp = await client.PostAsJsonAsync($"/api/v1/telephony/calls/{callId}/transfer", new TransferCallRequestDto
        {
            TargetAgentId = agentB.Id,
            TransferType = TransferType.Blind,
            Reason = "Escalating customer request to Bob"
        });
        transferResp.EnsureSuccessStatusCode();

        // 6. Agent Bob accepts call -> Connected
        await AuthenticateAsync(userB.UserName);
        var acceptBResp = await client.PostAsync($"/api/v1/telephony/calls/{callId}/accept", null);
        acceptBResp.EnsureSuccessStatusCode();

        // 7. Agent Bob completes call with disposition -> Completed
        var completeResp = await client.PostAsJsonAsync($"/api/v1/telephony/calls/{callId}/complete", new CompleteCallRequestDto
        {
            DispositionId = disp.Id,
            Notes = "Customer ordered biryani package successfully."
        });
        completeResp.EnsureSuccessStatusCode();

        // 8. Inspect the Call Timeline API
        await AuthenticateAsync("phase10-admin");
        var timelineResp = await client.GetAsync($"/api/v1/calls/{callId}/timeline");
        timelineResp.EnsureSuccessStatusCode();

        var timelineEvents = await timelineResp.Content.ReadFromJsonAsync<List<CallTimelineEventDto>>();
        Assert.NotNull(timelineEvents);
        Assert.NotEmpty(timelineEvents);

        // Verify key events are captured in chronological order from CallEvents table
        var eventTypes = timelineEvents.Select(e => e.EventType).ToList();

        Assert.Contains("Incoming", eventTypes);
        Assert.Contains("Assigned", eventTypes);
        Assert.Contains("Ringing", eventTypes);
        Assert.Contains("Connected", eventTypes);
        Assert.Contains("Hold", eventTypes);
        Assert.Contains("Resumed", eventTypes);
        Assert.Contains("Transferred", eventTypes);
        Assert.Contains("Completed", eventTypes);

        // Verify that descriptions are populated properly
        var completedEvent = timelineEvents.Single(e => e.EventType == "Completed");
        Assert.Contains("Order Completed", completedEvent.Description);

        var holdEvent = timelineEvents.First(e => e.EventType == "Hold");
        Assert.Equal("Call placed on hold", holdEvent.Description);

        var resumeEvent = timelineEvents.First(e => e.EventType == "Resumed");
        Assert.StartsWith("Call resumed from hold", resumeEvent.Description);

        // Check Call details via /api/v1/calls/{id}
        var callDetailsResp = await client.GetAsync($"/api/v1/calls/{callId}");
        callDetailsResp.EnsureSuccessStatusCode();
        var callDetails = await callDetailsResp.Content.ReadFromJsonAsync<CallResponseDto>();
        Assert.NotNull(callDetails);
        Assert.Equal(CallStatus.Completed, callDetails.Status);
        Assert.Equal(disp.Name, callDetails.DispositionName);
        Assert.Equal("Customer ordered biryani package successfully.", callDetails.Notes);
    }
}
