using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace CallCenter.Infrastructure.RealTime;

[Authorize]
public sealed class CallCenterHub(
    IAgentConnectionResolver agentConnectionResolver,
    ILogger<CallCenterHub> logger) : Hub<ICallCenterHubClient>
{
    public const string HubRoute = "/hubs/call-center";

    public const string GroupAdmins = "role:admin";
    public const string GroupSupervisors = "role:supervisor";
    public const string GroupAgents = "role:agent";

    public const string AgentGroupPrefix = "agent:";
    public const string UserGroupPrefix = "user:";
    public const string QueueGroupPrefix = "queue:";
    public const string CallGroupPrefix = "call:";

    public static string GetAgentGroup(Guid agentId) =>
        $"{AgentGroupPrefix}{agentId:N}";

    public static string GetUserGroup(Guid userId) =>
        $"{UserGroupPrefix}{userId:N}";

    public static string GetQueueGroup(Guid queueId) =>
        $"{QueueGroupPrefix}{queueId:N}";

    public static string GetCallGroup(Guid callId) =>
        $"{CallGroupPrefix}{callId:N}";

    public override async Task OnConnectedAsync()
    {
        var connectionId = Context.ConnectionId;
        var user = Context.User;
        var userIdClaim = user?.FindFirstValue(ClaimTypes.NameIdentifier);

        if (Guid.TryParse(userIdClaim, out var userId))
        {
            // 1. Add connection to user-specific group
            await Groups.AddToGroupAsync(connectionId, GetUserGroup(userId));

            // 2. Add connection to role-based group(s)
            var isAdmin = user?.IsInRole("Admin") ?? false;
            var isSupervisor = user?.IsInRole("Supervisor") ?? false;
            var isAgent = user?.IsInRole("Agent") ?? false;

            if (isAdmin)
            {
                await Groups.AddToGroupAsync(connectionId, GroupAdmins);
                await Groups.AddToGroupAsync(connectionId, GroupSupervisors);
            }
            else if (isSupervisor)
            {
                await Groups.AddToGroupAsync(connectionId, GroupSupervisors);
            }
            else if (isAgent)
            {
                await Groups.AddToGroupAsync(connectionId, GroupAgents);
            }

            // 3. Resolve linked agent if applicable
            var agentId = await agentConnectionResolver.ResolveAgentIdAsync(userId);
            if (agentId.HasValue)
            {
                await Groups.AddToGroupAsync(connectionId, GetAgentGroup(agentId.Value));
                if (!isAdmin && !isSupervisor)
                {
                    await Groups.AddToGroupAsync(connectionId, GroupAgents);
                }
            }

            logger.LogInformation(
                "SignalR client {ConnectionId} connected for user {UserId} (Admin: {IsAdmin}, Supervisor: {IsSupervisor}, AgentId: {AgentId})",
                connectionId, userId, isAdmin, isSupervisor, agentId);
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        logger.LogDebug(
            "SignalR client {ConnectionId} disconnected (Exception: {ExceptionMessage})",
            Context.ConnectionId, exception?.Message);

        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinQueue(Guid queueId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, GetQueueGroup(queueId));
    }

    public async Task LeaveQueue(Guid queueId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetQueueGroup(queueId));
    }

    public async Task JoinCall(
        Guid callId,
        [Microsoft.AspNetCore.Mvc.FromServices] CallCenter.Infrastructure.Persistence.CallCenterDbContext dbContext)
    {
        var user = Context.User;
        var isAdmin = user?.IsInRole("Admin") ?? false;
        var isSupervisor = user?.IsInRole("Supervisor") ?? false;

        if (!isAdmin && !isSupervisor)
        {
            var userIdClaim = user?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdClaim, out var userId))
            {
                return;
            }

            var agentId = await agentConnectionResolver.ResolveAgentIdAsync(userId);
            var hasAccess = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(
                dbContext.Calls,
                c => c.Id == callId && (c.AssignedAgentId == agentId || c.AssignedAgentId == null));

            if (!hasAccess)
            {
                logger.LogWarning("Unauthorized attempt by user {UserId} (Agent: {AgentId}) to join call group {CallId}", userId, agentId, callId);
                return;
            }
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GetCallGroup(callId));
    }

    public async Task LeaveCall(Guid callId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetCallGroup(callId));
    }
}