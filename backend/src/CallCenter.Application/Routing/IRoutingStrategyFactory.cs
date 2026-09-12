namespace CallCenter.Application.Routing;

public interface IRoutingStrategyFactory
{
    IRoutingStrategy GetStrategy(RoutingStrategyType strategyType);
    IRoutingStrategy GetDefaultStrategy();
}
