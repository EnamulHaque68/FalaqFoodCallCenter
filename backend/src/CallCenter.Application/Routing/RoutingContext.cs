using CallCenter.Domain.Entities;

namespace CallCenter.Application.Routing;

public sealed class RoutingContext
{
    public Call Call { get; init; } = null!;
    public IReadOnlyList<RoutingCandidate> Candidates { get; init; } = [];
    public string? PreferredTeam { get; init; }
    public string? RequiredSkill { get; init; }
    public int CallPriority { get; init; }
}
