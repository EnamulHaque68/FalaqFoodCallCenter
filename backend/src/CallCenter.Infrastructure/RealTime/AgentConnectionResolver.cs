using CallCenter.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Infrastructure.RealTime;

public sealed class AgentConnectionResolver(
    CallCenterDbContext dbContext) : IAgentConnectionResolver
{
    public async Task<Guid?> ResolveAgentIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var agentId = await dbContext.Agents
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);

        if (agentId.HasValue)
        {
            return agentId.Value;
        }

        var user = await dbContext.Users
            .Include(u => u.Role)
            .SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is not null && (user.Role?.Name == "Admin" || user.Role?.Name == "Supervisor"))
        {
            var existingAgent = await dbContext.Agents
                .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);

            if (existingAgent != null)
            {
                return existingAgent.Id;
            }

            var prefix = user.Role.Name == "Admin" ? "ADM" : "SUP";
            var shortId = user.Id.ToString("N")[..6].ToUpperInvariant();
            var code = $"{prefix}-{shortId}";

            if (await dbContext.Agents.AnyAsync(a => a.EmployeeCode == code, cancellationToken))
            {
                code = $"{prefix}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
            }

            var newAgent = new CallCenter.Domain.Entities.Agent
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                EmployeeCode = code,
                DisplayName = user.UserName ?? (user.Role.Name == "Admin" ? "Administrator" : "Supervisor"),
                Team = "Management",
                Status = CallCenter.Domain.Enums.AgentStatus.Available,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            try
            {
                dbContext.Agents.Add(newAgent);
                await dbContext.SaveChangesAsync(cancellationToken);
                return newAgent.Id;
            }
            catch (DbUpdateException)
            {
                return await dbContext.Agents
                    .AsNoTracking()
                    .Where(x => x.UserId == userId)
                    .Select(x => (Guid?)x.Id)
                    .FirstOrDefaultAsync(cancellationToken);
            }
        }

        return null;
    }
}
