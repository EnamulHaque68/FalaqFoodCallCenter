using CallCenter.Domain.Entities;

namespace CallCenter.Application.Routing;

public sealed class RoutingCandidate
{
    public Agent Agent { get; init; } = null!;
    public int ActiveCallsCount { get; init; }
    public int CompletedCallsTodayCount { get; init; }
    public DateTime? LastCallEndedAt { get; init; }
}
