using CallCenter.Application.Routing;
using CallCenter.Infrastructure.Routing.Strategies;

namespace CallCenter.Infrastructure.Routing;

public sealed class RoutingStrategyFactory : IRoutingStrategyFactory
{
    private readonly Dictionary<RoutingStrategyType, IRoutingStrategy> strategies;
    private readonly IRoutingStrategy defaultStrategy;

    public RoutingStrategyFactory()
    {
        var leastBusy = new LeastBusyRoutingStrategy();
        defaultStrategy = leastBusy;

        strategies = new Dictionary<RoutingStrategyType, IRoutingStrategy>
        {
            [RoutingStrategyType.LeastBusy] = leastBusy,
            [RoutingStrategyType.RoundRobin] = new RoundRobinRoutingStrategy(),
            [RoutingStrategyType.TeamBased] = new TeamBasedRoutingStrategy(leastBusy),
            [RoutingStrategyType.SkillBased] = new SkillBasedRoutingStrategy(leastBusy),
            [RoutingStrategyType.Priority] = new PriorityRoutingStrategy(leastBusy)
        };
    }

    public IRoutingStrategy GetStrategy(RoutingStrategyType strategyType)
    {
        return strategies.TryGetValue(strategyType, out var strategy)
            ? strategy
            : defaultStrategy;
    }

    public IRoutingStrategy GetDefaultStrategy() => defaultStrategy;
}
