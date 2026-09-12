using CallCenter.Domain.Enums;

namespace CallCenter.Application.Routing.DTOs;

public sealed class RoutingResultDto
{
    public Guid CallId { get; init; }
    public Guid? QueueId { get; init; }
    public Guid? AgentId { get; init; }
    public CallStatus CallStatus { get; init; }
    public string? StrategyUsed { get; init; }
    public string Result { get; init; } = null!;
}
