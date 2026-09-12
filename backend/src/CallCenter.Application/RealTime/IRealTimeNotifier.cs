using CallCenter.Application.RealTime.Contracts;

namespace CallCenter.Application.RealTime;

public interface IRealTimeNotifier
{
    Task NotifyIncomingCallAsync(
        IncomingCallEvent eventData,
        CancellationToken cancellationToken = default);

    Task NotifyAgentStatusChangedAsync(
        AgentStatusChangedEvent eventData,
        CancellationToken cancellationToken = default);

    Task NotifyCallStatusChangedAsync(
        CallStatusChangedEvent eventData,
        CancellationToken cancellationToken = default);

    Task NotifyQueueUpdatedAsync(
        QueueUpdatedEvent eventData,
        CancellationToken cancellationToken = default);

    Task NotifyCallTransferredAsync(
        CallTransferredEvent eventData,
        CancellationToken cancellationToken = default);

    Task NotifyCallAssignedAsync(
        CallAssignedEvent eventData,
        CancellationToken cancellationToken = default);

    Task NotifyNotificationAsync(
        NotificationEvent eventData,
        CancellationToken cancellationToken = default);
}
