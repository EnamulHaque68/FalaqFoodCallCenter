namespace CallCenter.Application.RealTime.Contracts;

public sealed record NotificationEvent(
    Guid Id,
    string Title,
    string Message,
    string Severity,
    string? TargetType,
    string? TargetId,
    DateTime OccurredAtUtc);
