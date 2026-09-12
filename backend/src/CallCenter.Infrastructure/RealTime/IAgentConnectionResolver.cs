namespace CallCenter.Infrastructure.RealTime;

public interface IAgentConnectionResolver
{
    Task<Guid?> ResolveAgentIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
