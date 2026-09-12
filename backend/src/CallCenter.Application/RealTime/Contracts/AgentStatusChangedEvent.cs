namespace CallCenter.Application.RealTime.Contracts;

public sealed record AgentStatusChangedEvent(
    Guid AgentId,
    Guid UserId,
    string Status,
    DateTime OccurredAtUtc);
