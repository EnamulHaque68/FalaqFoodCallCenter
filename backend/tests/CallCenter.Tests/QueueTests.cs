using CallCenter.Application.Queues.DTOs;
using CallCenter.Application.RealTime;
using CallCenter.Domain.Entities;
using CallCenter.Domain.Enums;
using CallCenter.Infrastructure.Queues;
using CallCenter.Infrastructure.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CallCenter.Tests;

public sealed class QueueTests
{
    private static Call CreateCall(Guid customerId, CallStatus status = CallStatus.Queued, Guid? callId = null) => new()
    {
        Id = callId ?? Guid.NewGuid(),
        CustomerId = customerId,
        Direction = CallDirection.Inbound,
        Status = status,
        PhoneNumber = "8801712345678",
        CorrelationId = $"QUEUE-TEST-{Guid.NewGuid():N}",
        StartedAt = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task Create_queue_and_get_summary_calculates_metrics()
    {
        await using var db = TestDbContextFactory.Create();
        var notifier = new Mock<IRealTimeNotifier>();
        var factory = new RoutingStrategyFactory();
        var service = new QueueService(db, notifier.Object, factory, NullLogger<QueueService>.Instance);

        var queue = await service.CreateQueueAsync(new CreateQueueRequestDto
        {
            Name = "Support Queue",
            Priority = 2,
            IsActive = true
        });

        Assert.Equal("Support Queue", queue.Name);
        Assert.Equal(2, queue.Priority);

        var summary = await service.GetQueueSummaryAsync();
        Assert.True(summary.TotalQueues >= 1);
        Assert.True(summary.ActiveQueues >= 1);
        Assert.Equal(0, summary.TotalWaitingCalls);
    }

    [Fact]
    public async Task Cancelling_call_compacts_remaining_queue_positions()
    {
        await using var db = TestDbContextFactory.Create();
        var notifier = new Mock<IRealTimeNotifier>();
        var factory = new RoutingStrategyFactory();
        var service = new QueueService(db, notifier.Object, factory, NullLogger<QueueService>.Instance);

        var queueId = Guid.NewGuid();
        var queue = new CallQueue
        {
            Id = queueId,
            Name = "Compaction Queue",
            Priority = 1,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            DisplayName = "Test Customer",
            PhoneNumber = "8801712345678",
            CreatedAt = DateTime.UtcNow
        };

        var call1 = CreateCall(customer.Id);
        var call2 = CreateCall(customer.Id);
        var call3 = CreateCall(customer.Id);

        var entry1 = new CallQueueEntry { Id = Guid.NewGuid(), CallQueueId = queueId, CallId = call1.Id, Position = 1, Priority = 0, EnqueuedAt = DateTime.UtcNow.AddMinutes(-5) };
        var entry2 = new CallQueueEntry { Id = Guid.NewGuid(), CallQueueId = queueId, CallId = call2.Id, Position = 2, Priority = 0, EnqueuedAt = DateTime.UtcNow.AddMinutes(-4) };
        var entry3 = new CallQueueEntry { Id = Guid.NewGuid(), CallQueueId = queueId, CallId = call3.Id, Position = 3, Priority = 0, EnqueuedAt = DateTime.UtcNow.AddMinutes(-3) };

        db.Customers.Add(customer);
        db.CallQueues.Add(queue);
        db.Calls.AddRange(call1, call2, call3);
        db.CallQueueEntries.AddRange(entry1, entry2, entry3);
        await db.SaveChangesAsync();

        // Cancel middle call (call 2)
        var cancelled = await service.CancelQueueEntryAsync(call2.Id, "Customer hung up");
        Assert.True(cancelled);

        // Verify call 2 is marked Abandoned and dequeued
        var dbCall2 = await db.Calls.SingleAsync(c => c.Id == call2.Id);
        Assert.Equal(CallStatus.Abandoned, dbCall2.Status);
        Assert.Contains("Customer hung up", dbCall2.Notes);

        var dbEntry2 = await db.CallQueueEntries.SingleAsync(e => e.CallId == call2.Id);
        Assert.NotNull(dbEntry2.DequeuedAt);

        // Verify remaining entries are compacted to positions 1 and 2
        var remaining = await service.GetQueueEntriesAsync(queueId);
        Assert.Equal(2, remaining.Count);
        Assert.Equal(call1.Id, remaining[0].CallId);
        Assert.Equal(1, remaining[0].Position);
        Assert.Equal(call3.Id, remaining[1].CallId);
        Assert.Equal(2, remaining[1].Position);
    }

    [Fact]
    public async Task Higher_priority_entries_are_dispatched_first()
    {
        await using var db = TestDbContextFactory.Create();
        var notifier = new Mock<IRealTimeNotifier>();
        var factory = new RoutingStrategyFactory();
        var service = new QueueService(db, notifier.Object, factory, NullLogger<QueueService>.Instance);

        var queueId = Guid.NewGuid();
        var queue = new CallQueue { Id = queueId, Name = "VIP Queue", Priority = 1, IsActive = true, CreatedAt = DateTime.UtcNow };

        var customer = new Customer { Id = Guid.NewGuid(), DisplayName = "VIP Customer", PhoneNumber = "8801712345678", CreatedAt = DateTime.UtcNow };

        var normalCall = CreateCall(customer.Id);
        var vipCall = CreateCall(customer.Id);

        // normal call was enqueued earlier (10m ago), VIP call later (2m ago) but with Priority = 5
        var entryNormal = new CallQueueEntry { Id = Guid.NewGuid(), CallQueueId = queueId, CallId = normalCall.Id, Position = 1, Priority = 0, EnqueuedAt = DateTime.UtcNow.AddMinutes(-10) };
        var entryVip = new CallQueueEntry { Id = Guid.NewGuid(), CallQueueId = queueId, CallId = vipCall.Id, Position = 2, Priority = 5, EnqueuedAt = DateTime.UtcNow.AddMinutes(-2) };

        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            EmployeeCode = "AG01",
            DisplayName = "Ready Agent",
            Status = AgentStatus.Available,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        db.Customers.Add(customer);
        db.CallQueues.Add(queue);
        db.Calls.AddRange(normalCall, vipCall);
        db.CallQueueEntries.AddRange(entryNormal, entryVip);
        db.Agents.Add(agent);
        await db.SaveChangesAsync();

        // Run auto-assignment
        var result = await service.TryAutoAssignNextCallAsync();
        Assert.NotNull(result);

        // Must assign the higher priority VIP call first, even though it arrived later
        Assert.Equal(vipCall.Id, result.CallId);
        Assert.Equal(agent.Id, result.AgentId);
        Assert.Equal(CallStatus.Ringing, result.CallStatus);

        // Verify in DB
        var dbVipCall = await db.Calls.SingleAsync(c => c.Id == vipCall.Id);
        Assert.Equal(CallStatus.Ringing, dbVipCall.Status);
        Assert.Equal(agent.Id, dbVipCall.AssignedAgentId);

        var dbNormalCall = await db.Calls.SingleAsync(c => c.Id == normalCall.Id);
        Assert.Equal(CallStatus.Queued, dbNormalCall.Status);
    }

    [Fact]
    public async Task Prioritize_entry_updates_priority_and_reorders_positions()
    {
        await using var db = TestDbContextFactory.Create();
        var notifier = new Mock<IRealTimeNotifier>();
        var factory = new RoutingStrategyFactory();
        var service = new QueueService(db, notifier.Object, factory, NullLogger<QueueService>.Instance);

        var queueId = Guid.NewGuid();
        var queue = new CallQueue { Id = queueId, Name = "Reprioritize Queue", Priority = 1, IsActive = true, CreatedAt = DateTime.UtcNow };
        var customer = new Customer { Id = Guid.NewGuid(), DisplayName = "Customer", PhoneNumber = "8801712345678", CreatedAt = DateTime.UtcNow };

        var call1 = CreateCall(customer.Id);
        var call2 = CreateCall(customer.Id);

        var entry1 = new CallQueueEntry { Id = Guid.NewGuid(), CallQueueId = queueId, CallId = call1.Id, Position = 1, Priority = 0, EnqueuedAt = DateTime.UtcNow.AddMinutes(-5) };
        var entry2 = new CallQueueEntry { Id = Guid.NewGuid(), CallQueueId = queueId, CallId = call2.Id, Position = 2, Priority = 0, EnqueuedAt = DateTime.UtcNow.AddMinutes(-4) };

        db.Customers.Add(customer);
        db.CallQueues.Add(queue);
        db.Calls.AddRange(call1, call2);
        db.CallQueueEntries.AddRange(entry1, entry2);
        await db.SaveChangesAsync();

        // Elevate entry2 priority to 10
        var updated = await service.PrioritizeEntryAsync(entry2.Id, 10);
        Assert.Equal(10, updated.Priority);

        // Verify entry2 is now Position #1
        var entries = await service.GetQueueEntriesAsync(queueId);
        Assert.Equal(entry2.Id, entries[0].Id);
        Assert.Equal(1, entries[0].Position);
        Assert.Equal(entry1.Id, entries[1].Id);
        Assert.Equal(2, entries[1].Position);
    }

    [Fact]
    public async Task Concurrent_assignment_protects_against_duplicate_call_dispatch()
    {
        await using var db = TestDbContextFactory.Create();
        var notifier = new Mock<IRealTimeNotifier>();
        var factory = new RoutingStrategyFactory();
        var service = new QueueService(db, notifier.Object, factory, NullLogger<QueueService>.Instance);

        var queueId = Guid.NewGuid();
        var queue = new CallQueue { Id = queueId, Name = "Concurrency Queue", Priority = 1, IsActive = true, CreatedAt = DateTime.UtcNow };
        var customer = new Customer { Id = Guid.NewGuid(), DisplayName = "Customer", PhoneNumber = "8801712345678", CreatedAt = DateTime.UtcNow };

        var singleCall = CreateCall(customer.Id);
        var entry = new CallQueueEntry { Id = Guid.NewGuid(), CallQueueId = queueId, CallId = singleCall.Id, Position = 1, Priority = 0, EnqueuedAt = DateTime.UtcNow };

        var agent1 = new Agent { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), EmployeeCode = "AG01", DisplayName = "Agent 1", Status = AgentStatus.Available, IsActive = true, CreatedAt = DateTime.UtcNow };
        var agent2 = new Agent { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), EmployeeCode = "AG02", DisplayName = "Agent 2", Status = AgentStatus.Available, IsActive = true, CreatedAt = DateTime.UtcNow };

        db.Customers.Add(customer);
        db.CallQueues.Add(queue);
        db.Calls.Add(singleCall);
        db.CallQueueEntries.Add(entry);
        db.Agents.AddRange(agent1, agent2);
        await db.SaveChangesAsync();

        // Run 2 parallel assignment tasks targeting the single call
        var task1 = service.TryAutoAssignNextCallAsync(agent1.Id);
        var task2 = service.TryAutoAssignNextCallAsync(agent2.Id);

        var results = await Task.WhenAll(task1, task2);

        // Exactly one task must successfully assign the call, and the other must return null
        var nonNullResults = results.Where(r => r != null).ToList();
        Assert.Single(nonNullResults);

        // Call in DB is assigned to exactly one agent and status is Ringing
        var dbCall = await db.Calls.SingleAsync(c => c.Id == singleCall.Id);
        Assert.Equal(CallStatus.Ringing, dbCall.Status);
        Assert.NotNull(dbCall.AssignedAgentId);

        // Queue entry is dequeued
        var dbEntry = await db.CallQueueEntries.SingleAsync(e => e.Id == entry.Id);
        Assert.NotNull(dbEntry.DequeuedAt);
    }
}
