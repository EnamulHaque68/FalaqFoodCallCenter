using CallCenter.Application.RealTime;
using CallCenter.Application.RealTime.Contracts;
using Microsoft.AspNetCore.SignalR;

namespace CallCenter.Infrastructure.RealTime;

public sealed class SignalRRealTimeNotifier(
    IHubContext<CallCenterHub, ICallCenterHubClient> hubContext)
    : IRealTimeNotifier
{
    public Task NotifyIncomingCallAsync(
        IncomingCallEvent eventData,
        CancellationToken cancellationToken = default)
    {
        // Deliver to supervisors, administrators, and agents
        return hubContext.Clients.Groups(
            CallCenterHub.GroupSupervisors,
            CallCenterHub.GroupAdmins,
            CallCenterHub.GroupAgents
        ).IncomingCall(eventData);
    }

    public Task NotifyCallStatusChangedAsync(
        CallStatusChangedEvent eventData,
        CancellationToken cancellationToken = default)
    {
        var targetGroups = new HashSet<string>
        {
            CallCenterHub.GroupSupervisors,
            CallCenterHub.GroupAdmins,
            CallCenterHub.GetCallGroup(eventData.CallId)
        };

        if (eventData.AgentId.HasValue)
        {
            // Direct assigned call status change: only assigned agent + leadership
            targetGroups.Add(CallCenterHub.GetAgentGroup(eventData.AgentId.Value));
        }
        else
        {
            // Unassigned / queued state: available to agent pool
            targetGroups.Add(CallCenterHub.GroupAgents);
        }

        return hubContext.Clients.Groups(targetGroups.ToList()).CallStatusChanged(eventData);
    }

    public Task NotifyAgentStatusChangedAsync(
        AgentStatusChangedEvent eventData,
        CancellationToken cancellationToken = default)
    {
        // Agent roster status is visible to supervisors, admins, and fellow agents
        return hubContext.Clients.Groups(
            CallCenterHub.GroupSupervisors,
            CallCenterHub.GroupAdmins,
            CallCenterHub.GroupAgents
        ).AgentStatusChanged(eventData);
    }

    public Task NotifyCallAssignedAsync(
        CallAssignedEvent eventData,
        CancellationToken cancellationToken = default)
    {
        // Strictly targeted to the assigned agent, supervisors, and admins.
        // Other agents are excluded to protect call privacy.
        var targetGroups = new List<string>
        {
            CallCenterHub.GetAgentGroup(eventData.AgentId),
            CallCenterHub.GroupSupervisors,
            CallCenterHub.GroupAdmins,
            CallCenterHub.GetCallGroup(eventData.CallId)
        };

        return hubContext.Clients.Groups(targetGroups).CallAssigned(eventData);
    }

    public Task NotifyCallTransferredAsync(
        CallTransferredEvent eventData,
        CancellationToken cancellationToken = default)
    {
        var targetGroups = new HashSet<string>
        {
            CallCenterHub.GroupSupervisors,
            CallCenterHub.GroupAdmins,
            CallCenterHub.GetCallGroup(eventData.CallId)
        };

        if (eventData.SourceAgentId.HasValue)
        {
            targetGroups.Add(CallCenterHub.GetAgentGroup(eventData.SourceAgentId.Value));
        }

        if (eventData.TargetAgentId.HasValue)
        {
            targetGroups.Add(CallCenterHub.GetAgentGroup(eventData.TargetAgentId.Value));
        }

        if (eventData.TargetQueueId.HasValue)
        {
            targetGroups.Add(CallCenterHub.GetQueueGroup(eventData.TargetQueueId.Value));
            targetGroups.Add(CallCenterHub.GroupAgents);
        }

        return hubContext.Clients.Groups(targetGroups.ToList()).CallTransferred(eventData);
    }

    public Task NotifyQueueUpdatedAsync(
        QueueUpdatedEvent eventData,
        CancellationToken cancellationToken = default)
    {
        var targetGroups = new List<string>
        {
            CallCenterHub.GetQueueGroup(eventData.QueueId),
            CallCenterHub.GroupSupervisors,
            CallCenterHub.GroupAdmins,
            CallCenterHub.GroupAgents
        };

        return hubContext.Clients.Groups(targetGroups).QueueUpdated(eventData);
    }

    public Task NotifyNotificationAsync(
        NotificationEvent eventData,
        CancellationToken cancellationToken = default)
    {
        // Route notifications based on target type
        if (string.Equals(eventData.TargetType, "User", StringComparison.OrdinalIgnoreCase) &&
            Guid.TryParse(eventData.TargetId, out var userId))
        {
            return hubContext.Clients.Group(CallCenterHub.GetUserGroup(userId)).Notification(eventData);
        }

        if (string.Equals(eventData.TargetType, "Agent", StringComparison.OrdinalIgnoreCase) &&
            Guid.TryParse(eventData.TargetId, out var agentId))
        {
            return hubContext.Clients.Group(CallCenterHub.GetAgentGroup(agentId)).Notification(eventData);
        }

        if (string.Equals(eventData.TargetType, "Role", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(eventData.TargetId))
        {
            var role = eventData.TargetId.Trim().ToLowerInvariant();
            var roleGroup = role switch
            {
                "admin" => CallCenterHub.GroupAdmins,
                "supervisor" => CallCenterHub.GroupSupervisors,
                "agent" => CallCenterHub.GroupAgents,
                _ => CallCenterHub.GroupAgents
            };
            return hubContext.Clients.Group(roleGroup).Notification(eventData);
        }

        // Default or "All": deliver to all authenticated workforce groups
        return hubContext.Clients.Groups(
            CallCenterHub.GroupAdmins,
            CallCenterHub.GroupSupervisors,
            CallCenterHub.GroupAgents
        ).Notification(eventData);
    }
}
