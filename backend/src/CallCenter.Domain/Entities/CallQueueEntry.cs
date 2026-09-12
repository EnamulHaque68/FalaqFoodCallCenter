namespace CallCenter.Domain.Entities;

public sealed class CallQueueEntry
{
    public Guid Id { get; set; }
    public Guid CallQueueId { get; set; }
    public Guid CallId { get; set; }
    public DateTime EnqueuedAt { get; set; }
    public DateTime? DequeuedAt { get; set; }
    public int Position { get; set; }
    public int Priority { get; set; } = 0;

    public CallQueue CallQueue { get; set; } = null!;
    public Call Call { get; set; } = null!;
}
