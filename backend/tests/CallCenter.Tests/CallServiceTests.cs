using CallCenter.Application.RealTime;
using CallCenter.Application.Calls.DTOs;
using CallCenter.Domain.Entities;
using CallCenter.Domain.Enums;
using CallCenter.Infrastructure.Calls;
using Moq;

namespace CallCenter.Tests;

public sealed class CallServiceTests
{
    [Fact]
    public async Task Duplicate_idempotency_key_returns_existing_call()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        db.Customers.Add(new Customer { Id = customerId, DisplayName = "Customer", PhoneNumber = "8801712345678", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var notifier = new Mock<IRealTimeNotifier>();
        var service = new CallService(db, notifier.Object);
        var request = new CreateIncomingCallRequestDto { CustomerId = customerId, PhoneNumber = "+8801712345678", CorrelationId = "corr-1" };

        var first = await service.CreateIncomingAsync(request, "idem-1");
        var second = await service.CreateIncomingAsync(new CreateIncomingCallRequestDto { CustomerId = customerId, PhoneNumber = "8801712345678", CorrelationId = "corr-2" }, "idem-1");

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, db.Calls.Count());
    }

    [Fact]
    public async Task Duplicate_correlation_id_is_rejected_when_no_idempotency_key_exists()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        db.Customers.Add(new Customer { Id = customerId, DisplayName = "Customer", PhoneNumber = "8801712345678", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var service = new CallService(db, new Mock<IRealTimeNotifier>().Object);
        var request = new CreateIncomingCallRequestDto { CustomerId = customerId, PhoneNumber = "8801712345678", CorrelationId = "same" };

        await service.CreateIncomingAsync(request, null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateIncomingAsync(request, null));
    }

    [Fact]
    public async Task Duplicate_state_event_is_ignored_without_creating_an_extra_event()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        db.Customers.Add(new Customer { Id = customerId, DisplayName = "Customer", PhoneNumber = "8801712345678", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var service = new CallService(db, new Mock<IRealTimeNotifier>().Object);
        var created = await service.CreateIncomingAsync(new CreateIncomingCallRequestDto { CustomerId = customerId, PhoneNumber = "8801712345678", CorrelationId = "dup-event" }, null);

        await service.TransitionAsync(created.Id, CallStatus.Ringing);
        var eventsAfterFirstTransition = db.CallEvents.Count();
        await service.TransitionAsync(created.Id, CallStatus.Ringing);

        Assert.Equal(eventsAfterFirstTransition, db.CallEvents.Count());
    }

    [Fact]
    public async Task Call_completion_sets_disposition_and_completed_state()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var dispositionId = Guid.NewGuid();
        db.Customers.Add(new Customer { Id = customerId, DisplayName = "Customer", PhoneNumber = "8801712345678", CreatedAt = DateTime.UtcNow });
        db.CallDispositions.Add(new CallDisposition { Id = dispositionId, Code = "SALE", Name = "Sale", IsActive = true, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var service = new CallService(db, new Mock<IRealTimeNotifier>().Object);
        var created = await service.CreateIncomingAsync(new CreateIncomingCallRequestDto { CustomerId = customerId, PhoneNumber = "8801712345678", CorrelationId = "c1" }, null);
        await service.TransitionAsync(created.Id, CallStatus.Ringing);
        await service.TransitionAsync(created.Id, CallStatus.Connected);

        var completed = await service.CompleteAsync(created.Id, dispositionId);

        Assert.Equal(CallStatus.Completed, completed!.Status);
        Assert.Equal(dispositionId, completed.CallDispositionId);
        Assert.NotNull(completed.EndedAt);
    }

    [Fact]
    public async Task CreateOutgoingAsync_ValidRequest_CreatesOutboundCall()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        db.Customers.Add(new Customer { Id = customerId, DisplayName = "Outbound Customer", PhoneNumber = "8801799999999", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var service = new CallService(db, new Mock<IRealTimeNotifier>().Object);
        var request = new CreateOutgoingCallRequestDto
        {
            CustomerId = customerId,
            PhoneNumber = "+880 1799 999999",
            CorrelationId = "out-unit-1"
        };

        var created = await service.CreateOutgoingAsync(request, "idem-out-1");

        Assert.NotNull(created);
        Assert.Equal(CallDirection.Outbound, created.Direction);
        Assert.Equal(CallStatus.Ringing, created.Status);
        Assert.Equal("8801799999999", created.PhoneNumber);
        Assert.Single(db.Calls);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsCallWithCustomerDetails()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var customer = new Customer { Id = customerId, DisplayName = "Jane Doe", PhoneNumber = "8801711223344", CreatedAt = DateTime.UtcNow };
        var call = new Call
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            Customer = customer,
            PhoneNumber = "8801711223344",
            CorrelationId = "corr-getbyid",
            Direction = CallDirection.Inbound,
            Status = CallStatus.Connected,
            StartedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        db.Customers.Add(customer);
        db.Calls.Add(call);
        await db.SaveChangesAsync();

        var service = new CallService(db, new Mock<IRealTimeNotifier>().Object);
        var result = await service.GetByIdAsync(call.Id);

        Assert.NotNull(result);
        Assert.Equal(call.Id, result.Id);
        Assert.Equal("Jane Doe", result.CustomerName);
        Assert.Equal(CallStatus.Connected, result.Status);

        var notFound = await service.GetByIdAsync(Guid.NewGuid());
        Assert.Null(notFound);
    }

    [Fact]
    public async Task UpdateNotesAsync_AppendsNotesAndTimelineEvent()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var customer = new Customer { Id = customerId, DisplayName = "Caller", PhoneNumber = "8801700000000", CreatedAt = DateTime.UtcNow };
        var call = new Call
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            Customer = customer,
            PhoneNumber = "8801700000000",
            CorrelationId = "corr-notes",
            Direction = CallDirection.Inbound,
            Status = CallStatus.Connected,
            StartedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        db.Customers.Add(customer);
        db.Calls.Add(call);
        await db.SaveChangesAsync();

        var service = new CallService(db, new Mock<IRealTimeNotifier>().Object);
        var updated = await service.UpdateNotesAsync(call.Id, "VIP customer requested urgent callback");

        Assert.NotNull(updated);
        Assert.Equal("VIP customer requested urgent callback", updated.Notes);
        Assert.True(db.CallEvents.Any(e => e.CallId == call.Id && e.EventType == "NotesUpdated"));
    }

    [Fact]
    public async Task GetHistoryAsync_AppliesFiltersAndPagination()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var customer = new Customer { Id = customerId, DisplayName = "History Customer", PhoneNumber = "8801788888888", CreatedAt = DateTime.UtcNow };
        db.Customers.Add(customer);

        for (int i = 0; i < 5; i++)
        {
            db.Calls.Add(new Call
            {
                Id = Guid.NewGuid(),
                CustomerId = customerId,
                Customer = customer,
                PhoneNumber = "8801788888888",
                CorrelationId = $"hist-{i}",
                Direction = i % 2 == 0 ? CallDirection.Inbound : CallDirection.Outbound,
                Status = CallStatus.Completed,
                StartedAt = DateTime.UtcNow.AddMinutes(-i * 10),
                CreatedAt = DateTime.UtcNow.AddMinutes(-i * 10)
            });
        }
        await db.SaveChangesAsync();

        var service = new CallService(db, new Mock<IRealTimeNotifier>().Object);

        // Test paging
        var paged = await service.GetHistoryAsync(page: 1, pageSize: 2);
        Assert.Equal(2, paged.Items.Count);
        Assert.Equal(5, paged.TotalCount);
        Assert.Equal(3, paged.TotalPages);

        // Test filter by direction
        var inboundOnly = await service.GetHistoryAsync(direction: CallDirection.Inbound, page: 1, pageSize: 10);
        Assert.Equal(3, inboundOnly.Items.Count);
        Assert.All(inboundOnly.Items, c => Assert.Equal(CallDirection.Inbound, c.Direction));
    }

    [Fact]
    public async Task GetTimelineAsync_ReturnsOrderedChronologicalEvents()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var customer = new Customer { Id = customerId, DisplayName = "Timeline Customer", PhoneNumber = "8801755555555", CreatedAt = DateTime.UtcNow };
        var callId = Guid.NewGuid();
        var call = new Call
        {
            Id = callId,
            CustomerId = customerId,
            Customer = customer,
            PhoneNumber = "8801755555555",
            CorrelationId = "corr-timeline",
            Direction = CallDirection.Inbound,
            Status = CallStatus.Connected,
            StartedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        db.Customers.Add(customer);
        db.Calls.Add(call);

        var t0 = DateTime.UtcNow.AddMinutes(-5);
        var t1 = DateTime.UtcNow.AddMinutes(-3);
        var t2 = DateTime.UtcNow.AddMinutes(-1);

        db.CallEvents.AddRange(
            new CallEvent { Id = Guid.NewGuid(), CallId = callId, EventType = "Incoming", OccurredAt = t0 },
            new CallEvent { Id = Guid.NewGuid(), CallId = callId, EventType = "Ringing", OccurredAt = t1 },
            new CallEvent { Id = Guid.NewGuid(), CallId = callId, EventType = "Connected", OccurredAt = t2 }
        );
        await db.SaveChangesAsync();

        var service = new CallService(db, new Mock<IRealTimeNotifier>().Object);
        var timeline = await service.GetTimelineAsync(callId);

        Assert.Equal(3, timeline.Count);
        Assert.Equal("Incoming", timeline[0].EventType);
        Assert.Equal("Ringing", timeline[1].EventType);
        Assert.Equal("Connected", timeline[2].EventType);
    }

    [Fact]
    public async Task CompleteAsync_ValidatesDispositionRules()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        var dispositionId = Guid.NewGuid();
        db.Customers.Add(new Customer { Id = customerId, DisplayName = "Customer", PhoneNumber = "8801712345678", CreatedAt = DateTime.UtcNow });
        db.CallDispositions.Add(new CallDisposition
        {
            Id = dispositionId,
            Code = "FOLLOWUP",
            Name = "Follow Up Required",
            RequiresFollowUp = true,
            RequiresNotes = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new CallService(db, new Mock<IRealTimeNotifier>().Object);
        var created = await service.CreateIncomingAsync(new CreateIncomingCallRequestDto { CustomerId = customerId, PhoneNumber = "8801712345678", CorrelationId = "c-disp-rule" }, null);
        await service.TransitionAsync(created.Id, CallStatus.Ringing);
        await service.TransitionAsync(created.Id, CallStatus.Connected);

        // Missing follow-up datetime -> throws ArgumentException
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CompleteAsync(created.Id, dispositionId, notes: "Some notes", followUpAt: null));

        // Past follow-up datetime -> throws ArgumentException
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CompleteAsync(created.Id, dispositionId, notes: "Some notes", followUpAt: DateTime.UtcNow.AddDays(-1)));

        // Missing notes -> throws ArgumentException
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CompleteAsync(created.Id, dispositionId, notes: "", followUpAt: DateTime.UtcNow.AddDays(1)));

        // Valid follow-up and notes -> succeeds
        var completed = await service.CompleteAsync(created.Id, dispositionId, notes: "Call resolved cleanly", followUpAt: DateTime.UtcNow.AddDays(1));
        Assert.NotNull(completed);
        Assert.Equal(CallStatus.Completed, completed.Status);
    }
}
