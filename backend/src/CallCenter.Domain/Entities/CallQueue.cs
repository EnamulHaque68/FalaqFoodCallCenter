namespace CallCenter.Domain.Entities;

public sealed class CallQueue
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public int Priority { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public ICollection<Call> Calls { get; set; } = new List<Call>();
    public ICollection<CallQueueEntry> Entries { get; set; } = new List<CallQueueEntry>();
}
