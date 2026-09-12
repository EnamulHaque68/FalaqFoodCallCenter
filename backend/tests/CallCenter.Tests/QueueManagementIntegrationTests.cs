using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CallCenter.Application.Authentication.DTOs;
using CallCenter.Application.Queues.DTOs;
using CallCenter.Application.Routing.DTOs;
using CallCenter.Domain.Entities;
using CallCenter.Domain.Enums;
using CallCenter.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CallCenter.Tests;

public sealed class QueueManagementIntegrationTests : IAsyncLifetime
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

    private static Call CreateCall(Guid customerId, CallStatus status = CallStatus.Queued, Guid? assignedAgentId = null, Guid? callId = null) => new()
    {
        Id = callId ?? Guid.NewGuid(),
        CustomerId = customerId,
        AssignedAgentId = assignedAgentId,
        Direction = CallDirection.Inbound,
        Status = status,
        PhoneNumber = "8801712345678",
        CorrelationId = $"Q-INT-{Guid.NewGuid():N}",
        StartedAt = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task Get_queues_and_summary_returns_live_metrics_via_api()
    {
        await AuthenticateAsync("phase10-admin");

        var queueRes = await client.GetAsync("/api/v1/queues");
        Assert.Equal(HttpStatusCode.OK, queueRes.StatusCode);
        var queues = await queueRes.Content.ReadFromJsonAsync<List<CallQueueDto>>();
        Assert.NotNull(queues);

        var summaryRes = await client.GetAsync("/api/v1/queues/summary");
        Assert.Equal(HttpStatusCode.OK, summaryRes.StatusCode);
        var summary = await summaryRes.Content.ReadFromJsonAsync<QueueSummaryDto>();
        Assert.NotNull(summary);
        Assert.True(summary.TotalQueues >= 0);
    }

    [Fact]
    public async Task Create_and_update_queue_via_api()
    {
        await AuthenticateAsync("phase10-admin");

        var createReq = new CreateQueueRequestDto
        {
            Name = $"Test-Queue-{Guid.NewGuid():N}",
            Priority = 3,
            IsActive = true
        };

        var postRes = await client.PostAsJsonAsync("/api/v1/queues", createReq);
        Assert.Equal(HttpStatusCode.Created, postRes.StatusCode);
        var created = await postRes.Content.ReadFromJsonAsync<CallQueueDto>();
        Assert.NotNull(created);
        Assert.Equal(createReq.Name, created.Name);
        Assert.Equal(3, created.Priority);

        var updateReq = new UpdateQueueRequestDto
        {
            Name = $"{createReq.Name}-Updated",
            Priority = 5,
            IsActive = true
        };

        var putRes = await client.PutAsJsonAsync($"/api/v1/queues/{created.Id}", updateReq);
        Assert.Equal(HttpStatusCode.OK, putRes.StatusCode);
        var updated = await putRes.Content.ReadFromJsonAsync<CallQueueDto>();
        Assert.NotNull(updated);
        Assert.Equal(updateReq.Name, updated.Name);
        Assert.Equal(5, updated.Priority);
    }

    [Fact]
    public async Task Get_queue_entries_returns_sorted_positions_and_wait_times()
    {
        await AuthenticateAsync("phase10-admin");

        Guid queueId;
        Guid call1Id;
        Guid call2Id;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();

            var queue = new CallQueue
            {
                Id = Guid.NewGuid(),
                Name = $"Entries-Queue-{Guid.NewGuid():N}",
                Priority = 2,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            queueId = queue.Id;

            var call1 = CreateCall(factory.CustomerId);
            var call2 = CreateCall(factory.CustomerId);
            call1Id = call1.Id;
            call2Id = call2.Id;

            var entry1 = new CallQueueEntry
            {
                Id = Guid.NewGuid(),
                CallQueueId = queueId,
                CallId = call1Id,
                Position = 1,
                Priority = 0,
                EnqueuedAt = DateTime.UtcNow.AddMinutes(-5)
            };

            var entry2 = new CallQueueEntry
            {
                Id = Guid.NewGuid(),
                CallQueueId = queueId,
                CallId = call2Id,
                Position = 2,
                Priority = 2,
                EnqueuedAt = DateTime.UtcNow.AddMinutes(-2)
            };

            db.CallQueues.Add(queue);
            db.Calls.AddRange(call1, call2);
            db.CallQueueEntries.AddRange(entry1, entry2);
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/v1/queues/{queueId}/entries");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var entries = await response.Content.ReadFromJsonAsync<List<CallQueueEntryDto>>();
        Assert.NotNull(entries);
        Assert.Equal(2, entries.Count);

        // Entry with Priority = 2 should appear first
        Assert.Equal(call2Id, entries[0].CallId);
        Assert.Equal(call1Id, entries[1].CallId);
    }

    [Fact]
    public async Task Cancel_queue_call_marks_abandoned_and_dequeues()
    {
        await AuthenticateAsync("phase10-admin");

        Guid queueId;
        Guid callId;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();

            var queue = new CallQueue
            {
                Id = Guid.NewGuid(),
                Name = $"Cancel-Queue-{Guid.NewGuid():N}",
                Priority = 1,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            queueId = queue.Id;

            var call = CreateCall(factory.CustomerId);
            callId = call.Id;

            var entry = new CallQueueEntry
            {
                Id = Guid.NewGuid(),
                CallQueueId = queueId,
                CallId = callId,
                Position = 1,
                Priority = 0,
                EnqueuedAt = DateTime.UtcNow.AddMinutes(-2)
            };

            db.CallQueues.Add(queue);
            db.Calls.Add(call);
            db.CallQueueEntries.Add(entry);
            await db.SaveChangesAsync();
        }

        var cancelRes = await client.PostAsync($"/api/v1/queues/calls/{callId}/cancel?reason=IntegrationCancel", null);
        Assert.Equal(HttpStatusCode.OK, cancelRes.StatusCode);

        // Verify in DB
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var dbCall = await db.Calls.SingleAsync(c => c.Id == callId);
            Assert.Equal(CallStatus.Abandoned, dbCall.Status);
            Assert.Contains("IntegrationCancel", dbCall.Notes);

            var dbEntry = await db.CallQueueEntries.SingleAsync(e => e.CallId == callId);
            Assert.NotNull(dbEntry.DequeuedAt);
        }
    }

    [Fact]
    public async Task Prioritize_queue_entry_via_api()
    {
        await AuthenticateAsync("phase10-admin");

        Guid queueId;
        Guid entryId;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();

            var queue = new CallQueue
            {
                Id = Guid.NewGuid(),
                Name = $"Priority-Queue-{Guid.NewGuid():N}",
                Priority = 1,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            queueId = queue.Id;

            var call = CreateCall(factory.CustomerId);
            var entry = new CallQueueEntry
            {
                Id = Guid.NewGuid(),
                CallQueueId = queueId,
                CallId = call.Id,
                Position = 1,
                Priority = 0,
                EnqueuedAt = DateTime.UtcNow
            };
            entryId = entry.Id;

            db.CallQueues.Add(queue);
            db.Calls.Add(call);
            db.CallQueueEntries.Add(entry);
            await db.SaveChangesAsync();
        }

        var req = new SetEntryPriorityRequestDto { Priority = 10 };
        var res = await client.PostAsJsonAsync($"/api/v1/queues/entries/{entryId}/priority", req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var updated = await res.Content.ReadFromJsonAsync<CallQueueEntryDto>();
        Assert.NotNull(updated);
        Assert.Equal(10, updated.Priority);
    }

    [Fact]
    public async Task Auto_assignment_assigns_call_when_agent_is_available()
    {
        await AuthenticateAsync("phase10-admin");

        Guid queueId;
        Guid callId;
        Guid agentId = factory.AgentId;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();

            // Set seeded agent to Available
            var agent = await db.Agents.SingleAsync(a => a.Id == agentId);
            agent.Status = AgentStatus.Available;

            var queue = new CallQueue
            {
                Id = Guid.NewGuid(),
                Name = $"AutoAssign-Queue-{Guid.NewGuid():N}",
                Priority = 5,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            queueId = queue.Id;

            var call = CreateCall(factory.CustomerId);
            callId = call.Id;

            var entry = new CallQueueEntry
            {
                Id = Guid.NewGuid(),
                CallQueueId = queueId,
                CallId = callId,
                Position = 1,
                Priority = 0,
                EnqueuedAt = DateTime.UtcNow
            };

            db.CallQueues.Add(queue);
            db.Calls.Add(call);
            db.CallQueueEntries.Add(entry);
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsync("/api/v1/queues/auto-assign", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<RoutingResultDto>();
        Assert.NotNull(result);
        Assert.Equal(callId, result.CallId);
        Assert.Equal(agentId, result.AgentId);
        Assert.Equal(CallStatus.Ringing, result.CallStatus);

        // Verify in DB
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var dbCall = await db.Calls.SingleAsync(c => c.Id == callId);
            Assert.Equal(CallStatus.Ringing, dbCall.Status);
            Assert.Equal(agentId, dbCall.AssignedAgentId);

            var dbEntry = await db.CallQueueEntries.SingleAsync(e => e.CallId == callId);
            Assert.NotNull(dbEntry.DequeuedAt);
        }
    }

    [Fact]
    public async Task Agent_role_cannot_create_or_edit_queues_rbac()
    {
        await AuthenticateAsync("phase10-agent");

        var createReq = new CreateQueueRequestDto
        {
            Name = "Agent Unauthorized Queue",
            Priority = 1,
            IsActive = true
        };

        var postRes = await client.PostAsJsonAsync("/api/v1/queues", createReq);
        Assert.Equal(HttpStatusCode.Forbidden, postRes.StatusCode);

        var deleteRes = await client.DeleteAsync($"/api/v1/queues/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Forbidden, deleteRes.StatusCode);

        var priorityRes = await client.PostAsJsonAsync($"/api/v1/queues/entries/{Guid.NewGuid()}/priority", new SetEntryPriorityRequestDto { Priority = 5 });
        Assert.Equal(HttpStatusCode.Forbidden, priorityRes.StatusCode);
    }

    [Fact]
    public async Task Agent_can_view_queues_and_entries()
    {
        await AuthenticateAsync("phase10-agent");

        var getRes = await client.GetAsync("/api/v1/queues");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);

        var summaryRes = await client.GetAsync("/api/v1/queues/summary");
        Assert.Equal(HttpStatusCode.OK, summaryRes.StatusCode);
    }
}
