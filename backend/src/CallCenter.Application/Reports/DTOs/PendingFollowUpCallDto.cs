namespace CallCenter.Application.Reports.DTOs;

public sealed class PendingFollowUpCallDto
{
    public Guid CallId { get; init; }
    public Guid CustomerId { get; init; }
    public string CustomerName { get; init; } = null!;
    public string PhoneNumber { get; init; } = null!;
    public Guid? AssignedAgentId { get; init; }
    public string? AgentName { get; init; }
    public string DispositionName { get; init; } = null!;
    public DateTime? FollowUpAt { get; init; }
    public string? FollowUpNotes { get; init; }
    public DateTime CallEndedAt { get; init; }
}
