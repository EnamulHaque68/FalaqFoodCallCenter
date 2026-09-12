namespace CallCenter.Application.RealTime.Contracts;

public sealed record QueueUpdatedEvent(
    Guid QueueId,
    int WaitingCount,
    DateTime OccurredAtUtc);
