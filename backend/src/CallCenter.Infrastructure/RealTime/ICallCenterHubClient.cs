using CallCenter.Application.RealTime.Contracts;

namespace CallCenter.Infrastructure.RealTime;

public interface ICallCenterHubClient
{
    Task IncomingCall(IncomingCallEvent eventData);
    Task AgentStatusChanged(AgentStatusChangedEvent eventData);
    Task CallStatusChanged(CallStatusChangedEvent eventData);
    Task QueueUpdated(QueueUpdatedEvent eventData);
    Task CallTransferred(CallTransferredEvent eventData);
    Task CallAssigned(CallAssignedEvent eventData);
    Task Notification(NotificationEvent eventData);
}
