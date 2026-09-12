using CallCenter.Application.Routing;
using CallCenter.Domain.Entities;

namespace CallCenter.Infrastructure.Routing.Strategies;

public sealed class RoundRobinRoutingStrategy : IRoutingStrategy
{
    private static int counter;

    public RoutingStrategyType StrategyType => RoutingStrategyType.RoundRobin;

    public Agent? SelectAgent(RoutingContext context)
    {
        if (context.Candidates.Count == 0)
        {
            return null;
        }

        var orderedCandidates = context.Candidates
            .OrderBy(c => c.Agent.CreatedAt)
            .ThenBy(c => c.Agent.Id)
            .ToList();

        var index = Math.Abs(Interlocked.Increment(ref counter)) % orderedCandidates.Count;
        return orderedCandidates[index].Agent;
    }
}
