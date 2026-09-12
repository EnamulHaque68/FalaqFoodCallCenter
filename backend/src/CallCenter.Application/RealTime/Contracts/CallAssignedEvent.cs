namespace CallCenter.Application.RealTime.Contracts;

public sealed record CallAssignedEvent(
    Guid CallId,
    Guid AgentId,
    string? AgentName,
    Guid? CustomerId,
    string? CustomerName,
    string PhoneNumber,
    string Direction,
    string Status,
    DateTime OccurredAtUtc);
