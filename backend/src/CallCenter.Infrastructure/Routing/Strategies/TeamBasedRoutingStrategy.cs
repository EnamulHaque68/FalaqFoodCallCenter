using CallCenter.Application.Routing;
using CallCenter.Domain.Entities;

namespace CallCenter.Infrastructure.Routing.Strategies;

public sealed class TeamBasedRoutingStrategy(LeastBusyRoutingStrategy fallbackStrategy) : IRoutingStrategy
{
    public RoutingStrategyType StrategyType => RoutingStrategyType.TeamBased;

    public Agent? SelectAgent(RoutingContext context)
    {
        if (context.Candidates.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(context.PreferredTeam))
        {
            var teamCandidates = context.Candidates
                .Where(c => !string.IsNullOrWhiteSpace(c.Agent.Team) &&
                            c.Agent.Team.Equals(context.PreferredTeam.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (teamCandidates.Count > 0)
            {
                var teamContext = new RoutingContext
                {
                    Call = context.Call,
                    Candidates = teamCandidates,
                    PreferredTeam = context.PreferredTeam,
                    RequiredSkill = context.RequiredSkill,
                    CallPriority = context.CallPriority
                };
                return fallbackStrategy.SelectAgent(teamContext);
            }
        }

        // Fallback to least busy across all teams
        return fallbackStrategy.SelectAgent(context);
    }
}
