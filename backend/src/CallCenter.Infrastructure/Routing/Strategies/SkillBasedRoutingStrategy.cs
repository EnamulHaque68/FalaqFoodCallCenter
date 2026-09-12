using CallCenter.Application.Routing;
using CallCenter.Domain.Entities;

namespace CallCenter.Infrastructure.Routing.Strategies;

public sealed class SkillBasedRoutingStrategy(LeastBusyRoutingStrategy fallbackStrategy) : IRoutingStrategy
{
    public RoutingStrategyType StrategyType => RoutingStrategyType.SkillBased;

    public Agent? SelectAgent(RoutingContext context)
    {
        if (context.Candidates.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(context.RequiredSkill))
        {
            var skill = context.RequiredSkill.Trim();
            var skillCandidates = context.Candidates
                .Where(c => (!string.IsNullOrWhiteSpace(c.Agent.Team) && c.Agent.Team.Contains(skill, StringComparison.OrdinalIgnoreCase)) ||
                            (!string.IsNullOrWhiteSpace(c.Agent.EmployeeCode) && c.Agent.EmployeeCode.Contains(skill, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (skillCandidates.Count > 0)
            {
                var skillContext = new RoutingContext
                {
                    Call = context.Call,
                    Candidates = skillCandidates,
                    PreferredTeam = context.PreferredTeam,
                    RequiredSkill = context.RequiredSkill,
                    CallPriority = context.CallPriority
                };
                return fallbackStrategy.SelectAgent(skillContext);
            }
        }

        // Fallback to least busy general pool
        return fallbackStrategy.SelectAgent(context);
    }
}
