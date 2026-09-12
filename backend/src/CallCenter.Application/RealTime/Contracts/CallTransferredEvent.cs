namespace CallCenter.Application.RealTime.Contracts;

public sealed record CallTransferredEvent(
    Guid CallId,
    Guid? SourceAgentId,
    Guid? TargetAgentId,
    Guid? TargetQueueId,
    string TransferType,
    string? Reason,
    DateTime OccurredAtUtc);
