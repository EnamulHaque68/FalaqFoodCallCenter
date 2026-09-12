using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace CallCenter.Infrastructure.RealTime;

public sealed class AgentGroupHubFilter(
    IHubContext<CallCenterHub> hubContext) : IHubFilter
{
    public async ValueTask OnConnectedAsync(
        HubLifetimeContext context,
        Func<HubLifetimeContext, Task> next)
    {
        await next(context);

        var userIdClaim = context.Context.User?.FindFirstValue(
            ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return;
        }

        var agentId = await ResolveAgentIdAsync(
            context,
            userId);

        if (agentId.HasValue)
        {
            await hubContext.Groups.AddToGroupAsync(
                context.Context.ConnectionId,
                CallCenterHub.GetAgentGroup(agentId.Value));
        }
    }

    private static async Task<Guid?> ResolveAgentIdAsync(
        HubLifetimeContext context,
        Guid userId)
    {
        var accessor = context.ServiceProvider
            .GetService<IAgentConnectionResolver>();

        return accessor is null
            ? null
            : await accessor.ResolveAgentIdAsync(userId);
    }
}