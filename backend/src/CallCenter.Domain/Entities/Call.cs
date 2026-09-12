using CallCenter.Domain.Enums;

namespace CallCenter.Domain.Entities;

public sealed class Call
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public Guid? AssignedAgentId { get; set; }
    public Guid? CallQueueId { get; set; }
    public Guid? CallDispositionId { get; set; }
    public string? ProviderCallId { get; set; }
    public string CorrelationId { get; set; } = null!;
    public string? IdempotencyKey { get; set; }
    public CallDirection Direction { get; set; }
    public CallStatus Status { get; set; }
    public string PhoneNumber { get; set; } = null!;
    public string? Notes { get; set; }
    public DateTime? FollowUpAt { get; set; }
    public string? FollowUpNotes { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? AnsweredAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public Customer Customer { get; set; } = null!;
    public Agent? AssignedAgent { get; set; }
    public CallQueue? CallQueue { get; set; }
    public CallDisposition? CallDisposition { get; set; }
    public ICollection<CallEvent> Events { get; set; } = new List<CallEvent>();
    public ICollection<CallRecording> Recordings { get; set; } = new List<CallRecording>();
    public ICollection<CallQueueEntry> QueueEntries { get; set; } = new List<CallQueueEntry>();

    public bool IsTerminal => Status is
        CallStatus.Completed or
        CallStatus.Abandoned or
        CallStatus.Rejected or
        CallStatus.Failed;

    public void TransitionTo(CallStatus nextStatus, DateTime utcNow)
    {
        if (Status == nextStatus)
        {
            return;
        }

        if (nextStatus == CallStatus.Completed)
        {
            throw new InvalidOperationException(
                "Use the call completion operation with a disposition to complete a call.");
        }

        if (!CanTransition(Status, nextStatus))
        {
            throw new InvalidOperationException(
                $"Invalid call state transition from '{Status}' to '{nextStatus}'.");
        }

        Status = nextStatus;
        UpdatedAt = utcNow;

        if (nextStatus == CallStatus.Connected && AnsweredAt is null)
        {
            AnsweredAt = utcNow;
        }

        if (nextStatus is CallStatus.Completed or CallStatus.Abandoned or CallStatus.Rejected or CallStatus.Failed)
        {
            EndedAt ??= utcNow;
        }
    }

    public void Complete(Guid dispositionId, DateTime utcNow) =>
        Complete(dispositionId, utcNow, null, null, null);

    public void Complete(
        Guid dispositionId,
        DateTime utcNow,
        string? notes = null,
        DateTime? followUpAt = null,
        string? followUpNotes = null)
    {
        if (dispositionId == Guid.Empty)
        {
            throw new ArgumentException("A disposition is required to complete a call.", nameof(dispositionId));
        }

        if (Status != CallStatus.Connected && Status != CallStatus.OnHold)
        {
            throw new InvalidOperationException(
                "Only a connected or on-hold call can be completed.");
        }

        CallDispositionId = dispositionId;
        Status = CallStatus.Completed;
        EndedAt ??= utcNow;
        UpdatedAt = utcNow;

        if (!string.IsNullOrWhiteSpace(notes))
        {
            Notes = notes.Trim();
        }

        FollowUpAt = followUpAt;
        if (!string.IsNullOrWhiteSpace(followUpNotes))
        {
            FollowUpNotes = followUpNotes.Trim();
        }
    }

    private static bool CanTransition(CallStatus current, CallStatus next) => current switch
    {
        CallStatus.Queued => next is CallStatus.Ringing or CallStatus.Abandoned or CallStatus.Rejected or CallStatus.Failed,
        CallStatus.Ringing => next is CallStatus.Connected or CallStatus.Abandoned or CallStatus.Rejected or CallStatus.Failed,
        CallStatus.Connected => next is CallStatus.OnHold or CallStatus.Completed or CallStatus.Abandoned or CallStatus.Failed,
        CallStatus.OnHold => next is CallStatus.Connected or CallStatus.Completed or CallStatus.Abandoned or CallStatus.Failed,
        _ => false
    };
}
