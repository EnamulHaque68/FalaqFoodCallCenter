namespace CallCenter.Application.RealTime.Contracts;

public sealed record IncomingCallEvent(
    Guid CallId,
    Guid? CustomerId,
    string PhoneNumber,
    string Direction,
    string Status,
    DateTime OccurredAtUtc);
