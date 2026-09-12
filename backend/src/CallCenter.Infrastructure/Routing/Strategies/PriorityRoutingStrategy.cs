using CallCenter.Application.Routing;
using CallCenter.Domain.Entities;

namespace CallCenter.Infrastructure.Routing.Strategies;

public sealed class PriorityRoutingStrategy(LeastBusyRoutingStrategy fallbackStrategy) : IRoutingStrategy
{
    public RoutingStrategyType StrategyType => RoutingStrategyType.Priority;

    public Agent? SelectAgent(RoutingContext context)
    {
        if (context.Candidates.Count == 0)
        {
            return null;
        }

        // For prioritized calls, prioritize completely unoccupied agents (0 active calls and lowest load today)
        var prioritizedCandidates = context.Candidates
            .OrderBy(c => c.ActiveCallsCount)
            .ThenBy(c => c.CompletedCallsTodayCount)
            .ThenBy(c => c.Agent.CreatedAt)
            .ToList();

        return prioritizedCandidates.FirstOrDefault()?.Agent ?? fallbackStrategy.SelectAgent(context);
    }
}
