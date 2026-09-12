using CallCenter.Domain.Enums;

namespace CallCenter.Application.Calls.DTOs;

public sealed class CallResponseDto
{
    public Guid Id { get; init; }
    public Guid CustomerId { get; init; }
    public string CustomerName { get; init; } = null!;
    public string CustomerPhone { get; init; } = null!;
    public Guid? AssignedAgentId { get; init; }
    public string? AgentName { get; init; }
    public Guid? CallQueueId { get; init; }
    public Guid? CallDispositionId { get; init; }
    public string? DispositionName { get; init; }
    public string? ProviderCallId { get; init; }
    public string CorrelationId { get; init; } = null!;
    public CallDirection Direction { get; init; }
    public CallStatus Status { get; init; }
    public string PhoneNumber { get; init; } = null!;
    public string? Notes { get; init; }
    public DateTime? FollowUpAt { get; init; }
    public string? FollowUpNotes { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? AnsweredAt { get; init; }
    public DateTime? EndedAt { get; init; }
    public int? DurationSeconds { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}
