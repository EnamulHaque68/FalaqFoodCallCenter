using CallCenter.Domain.Entities;

namespace CallCenter.Application.Routing;

public interface IRoutingStrategy
{
    RoutingStrategyType StrategyType { get; }
    Agent? SelectAgent(RoutingContext context);
}
