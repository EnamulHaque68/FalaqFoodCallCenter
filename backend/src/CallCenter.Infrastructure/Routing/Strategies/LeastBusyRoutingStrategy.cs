using CallCenter.Application.Routing;
using CallCenter.Domain.Entities;

namespace CallCenter.Infrastructure.Routing.Strategies;

public sealed class LeastBusyRoutingStrategy : IRoutingStrategy
{
    public RoutingStrategyType StrategyType => RoutingStrategyType.LeastBusy;

    public Agent? SelectAgent(RoutingContext context)
    {
        if (context.Candidates.Count == 0)
        {
            return null;
        }

        // 1. Least active calls
        // 2. Least calls completed today
        // 3. Longest idle (agents with no calls today prioritized first, then earliest LastCallEndedAt)
        // 4. Deterministic tie breaker: CreatedAt, Id
        var bestCandidate = context.Candidates
            .OrderBy(c => c.ActiveCallsCount)
            .ThenBy(c => c.CompletedCallsTodayCount)
            .ThenBy(c => c.LastCallEndedAt.HasValue ? 1 : 0)
            .ThenBy(c => c.LastCallEndedAt ?? DateTime.MinValue)
            .ThenBy(c => c.Agent.CreatedAt)
            .ThenBy(c => c.Agent.Id)
            .FirstOrDefault();

        return bestCandidate?.Agent;
    }
}
