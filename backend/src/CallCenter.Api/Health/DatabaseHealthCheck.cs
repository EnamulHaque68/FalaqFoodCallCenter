using CallCenter.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CallCenter.Api.Health;

public class DatabaseHealthCheck : IHealthCheck
{
    private readonly CallCenterDbContext _dbContext;

    public DatabaseHealthCheck(CallCenterDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? HealthCheckResult.Healthy("Database connection is healthy.")
                : HealthCheckResult.Unhealthy("Database connection failed: cannot connect.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database connection threw an exception.", ex);
        }
    }
}
