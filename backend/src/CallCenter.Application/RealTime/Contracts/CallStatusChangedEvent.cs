namespace CallCenter.Application.RealTime.Contracts;

public sealed record CallStatusChangedEvent(
    Guid CallId,
    Guid? AgentId,
    Guid? CustomerId,
    string Status,
    DateTime OccurredAtUtc);
