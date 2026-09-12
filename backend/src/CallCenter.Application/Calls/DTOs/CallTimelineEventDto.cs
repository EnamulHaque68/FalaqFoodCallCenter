namespace CallCenter.Application.Calls.DTOs;

public sealed class CallTimelineEventDto
{
    public Guid Id { get; init; }
    public Guid CallId { get; init; }
    public string EventType { get; init; } = null!;
    public string? Description { get; init; }
    public string? MetadataJson { get; init; }
    public Guid? AgentId { get; init; }
    public string? AgentName { get; init; }
    public DateTime OccurredAt { get; init; }
}
