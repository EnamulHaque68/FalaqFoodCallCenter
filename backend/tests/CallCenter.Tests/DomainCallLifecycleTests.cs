using CallCenter.Domain.Entities;
using CallCenter.Domain.Enums;

namespace CallCenter.Tests;

public sealed class DomainCallLifecycleTests
{
    [Fact]
    public void Queued_to_Ringing_to_Connected_is_valid()
    {
        var call = NewCall(CallStatus.Queued);
        var now = DateTime.UtcNow;

        call.TransitionTo(CallStatus.Ringing, now);
        call.TransitionTo(CallStatus.Connected, now.AddSeconds(1));

        Assert.Equal(CallStatus.Connected, call.Status);
        Assert.NotNull(call.AnsweredAt);
    }

    [Theory]
    [InlineData(CallStatus.Queued, CallStatus.Connected)]
    [InlineData(CallStatus.Connected, CallStatus.Ringing)]
    [InlineData(CallStatus.Completed, CallStatus.Ringing)]
    [InlineData(CallStatus.Rejected, CallStatus.Connected)]
    public void Invalid_state_transition_is_rejected(CallStatus current, CallStatus next)
    {
        var call = NewCall(current);
        Assert.Throws<InvalidOperationException>(() => call.TransitionTo(next, DateTime.UtcNow));
    }

    [Fact]
    public void Completion_requires_disposition_and_connected_state()
    {
        var call = NewCall(CallStatus.Connected);
        var dispositionId = Guid.NewGuid();

        call.Complete(dispositionId, DateTime.UtcNow);

        Assert.Equal(CallStatus.Completed, call.Status);
        Assert.Equal(dispositionId, call.CallDispositionId);
        Assert.NotNull(call.EndedAt);
    }

    [Fact]
    public void Terminal_call_cannot_be_completed_again()
    {
        var call = NewCall(CallStatus.Completed);
        Assert.Throws<InvalidOperationException>(() => call.Complete(Guid.NewGuid(), DateTime.UtcNow));
    }

    private static Call NewCall(CallStatus status) => new()
    {
        Id = Guid.NewGuid(), CustomerId = Guid.NewGuid(), CorrelationId = Guid.NewGuid().ToString(),
        PhoneNumber = "8801712345678", Direction = CallDirection.Inbound, Status = status,
        StartedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow
    };
}
