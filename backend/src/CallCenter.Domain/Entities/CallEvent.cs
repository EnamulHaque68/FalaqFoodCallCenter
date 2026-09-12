namespace CallCenter.Domain.Entities;

public sealed class CallEvent
{
    public Guid Id { get; set; }
    public Guid CallId { get; set; }
    public Guid? AgentId { get; set; }
    public string EventType { get; set; } = null!;
    public string? MetadataJson { get; set; }
    public DateTime OccurredAt { get; set; }

    public Call Call { get; set; } = null!;
    public Agent? Agent { get; set; }
}
