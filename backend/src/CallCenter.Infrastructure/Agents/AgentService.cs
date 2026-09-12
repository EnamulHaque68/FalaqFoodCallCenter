using CallCenter.Application.Audit;
using CallCenter.Application.Agents;
using CallCenter.Application.Agents.DTOs;
using CallCenter.Application.Calls.DTOs;
using CallCenter.Application.RealTime;
using CallCenter.Application.RealTime.Contracts;
using CallCenter.Application.Queues;
using CallCenter.Domain.Entities;
using CallCenter.Domain.Enums;
using CallCenter.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Infrastructure.Agents;

public sealed class AgentService(
    CallCenterDbContext dbContext,
    IRealTimeNotifier realTimeNotifier,
    IQueueService? queueService = null,
    IAuditLogService? auditLogService = null) : IAgentService
{
    public async Task<IReadOnlyList<AgentResponseDto>> GetAllAsync(
        string? search = null,
        AgentStatus? status = null,
        string? team = null,
        bool? isActive = null,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Agents
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x =>
                x.EmployeeCode.Contains(term) ||
                x.DisplayName.Contains(term) ||
                (x.User != null && x.User.UserName.Contains(term)) ||
                (x.Team != null && x.Team.Contains(term)));
        }

        if (status.HasValue)
        {
            query = query.Where(x => x.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(team))
        {
            var teamTerm = team.Trim();
            query = query.Where(x => x.Team != null && x.Team == teamTerm);
        }

        if (isActive.HasValue)
        {
            query = query.Where(x => x.IsActive == isActive.Value);
        }

        return await query
            .OrderBy(x => x.DisplayName)
            .Select(x => new AgentResponseDto
            {
                Id = x.Id,
                UserId = x.UserId,
                EmployeeCode = x.EmployeeCode,
                DisplayName = x.DisplayName,
                Team = x.Team,
                IsActive = x.IsActive,
                RoleName = x.User != null && x.User.Role != null ? x.User.Role.Name : null,
                UserName = x.User != null ? x.User.UserName : null,
                Status = x.Status,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<AgentResponseDto?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var agent = await dbContext.Agents
            .AsNoTracking()
            .Include(a => a.User)
                .ThenInclude(u => u!.Role)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        return agent is null ? null : ToDto(agent);
    }

    public async Task<AgentDetailsResponseDto?> GetDetailsAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var agent = await dbContext.Agents
            .AsNoTracking()
            .Include(a => a.User)
                .ThenInclude(u => u!.Role)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (agent is null) return null;

        var totalCalls = await dbContext.Calls
            .AsNoTracking()
            .CountAsync(x => x.AssignedAgentId == id, cancellationToken);

        var completedCalls = await dbContext.Calls
            .AsNoTracking()
            .CountAsync(x => x.AssignedAgentId == id && x.Status == CallStatus.Completed, cancellationToken);

        var activeCallId = await dbContext.Calls
            .AsNoTracking()
            .Where(x => x.AssignedAgentId == id && (x.Status == CallStatus.Ringing || x.Status == CallStatus.Connected))
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var recentCalls = await dbContext.Calls
            .AsNoTracking()
            .Where(x => x.AssignedAgentId == id)
            .OrderByDescending(x => x.StartedAt)
            .ThenByDescending(x => x.Id)
            .Take(10)
            .Select(x => ToCallDto(x))
            .ToListAsync(cancellationToken);

        return new AgentDetailsResponseDto
        {
            Id = agent.Id,
            UserId = agent.UserId,
            EmployeeCode = agent.EmployeeCode,
            DisplayName = agent.DisplayName,
            Team = agent.Team,
            IsActive = agent.IsActive,
            RoleName = agent.User?.Role?.Name,
            UserName = agent.User?.UserName,
            Status = agent.Status,
            CreatedAt = agent.CreatedAt,
            UpdatedAt = agent.UpdatedAt,
            TotalAssignedCalls = totalCalls,
            CompletedCalls = completedCalls,
            ActiveCallId = activeCallId,
            RecentCalls = recentCalls
        };
    }

    public async Task<IReadOnlyList<CallResponseDto>> GetAgentCallsAsync(
        Guid id,
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Calls
            .AsNoTracking()
            .Where(x => x.AssignedAgentId == id)
            .OrderByDescending(x => x.StartedAt)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => ToCallDto(x))
            .ToListAsync(cancellationToken);
    }

    public async Task<AgentDashboardResponseDto?> GetDashboardByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var agent = await dbContext.Agents
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);

        if (agent is null)
        {
            var user = await dbContext.Users
                .Include(u => u.Role)
                .SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);

            if (user is not null && (user.Role?.Name == "Admin" || user.Role?.Name == "Supervisor"))
            {
                var existingAgent = await dbContext.Agents
                    .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);

                if (existingAgent != null)
                {
                    agent = existingAgent;
                }
                else
                {
                    var prefix = user.Role.Name == "Admin" ? "ADM" : "SUP";
                    var shortId = user.Id.ToString("N")[..6].ToUpperInvariant();
                    var code = $"{prefix}-{shortId}";

                    if (await dbContext.Agents.AnyAsync(a => a.EmployeeCode == code, cancellationToken))
                    {
                        code = $"{prefix}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
                    }

                    var newAgent = new Agent
                    {
                        Id = Guid.NewGuid(),
                        UserId = user.Id,
                        EmployeeCode = code,
                        DisplayName = user.UserName ?? (user.Role.Name == "Admin" ? "Administrator" : "Supervisor"),
                        Team = "Management",
                        Status = AgentStatus.Available,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    };

                    try
                    {
                        dbContext.Agents.Add(newAgent);
                        await dbContext.SaveChangesAsync(cancellationToken);
                        agent = newAgent;
                    }
                    catch (DbUpdateException)
                    {
                        agent = await dbContext.Agents
                            .AsNoTracking()
                            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);

                        if (agent is null)
                        {
                            throw;
                        }
                    }
                }
            }
            else
            {
                return null;
            }
        }

        var todayUtc = DateTime.UtcNow.Date;

        var todaysCallsQuery = dbContext.Calls
            .AsNoTracking()
            .Where(x => x.AssignedAgentId == agent.Id && x.StartedAt >= todayUtc);

        var todaysCallsCount = await todaysCallsQuery.CountAsync(cancellationToken);

        var completedCallsCount = await todaysCallsQuery
            .CountAsync(x => x.Status == CallStatus.Completed, cancellationToken);

        var missedCallsCount = await todaysCallsQuery
            .CountAsync(x => x.Status == CallStatus.Abandoned ||
                             x.Status == CallStatus.Rejected ||
                             x.Status == CallStatus.Failed, cancellationToken);

        var completedDurations = await todaysCallsQuery
            .Where(x => x.Status == CallStatus.Completed && x.EndedAt.HasValue)
            .Select(x => new { Started = x.AnsweredAt ?? x.StartedAt, Ended = x.EndedAt!.Value })
            .ToListAsync(cancellationToken);

        var avgDuration = completedDurations.Count > 0
            ? Math.Round(completedDurations.Average(x => (x.Ended - x.Started).TotalSeconds), 1)
            : 0.0;

        var currentCall = await dbContext.Calls
            .AsNoTracking()
            .Include(x => x.Customer)
            .Include(x => x.CallDisposition)
            .Where(x => x.AssignedAgentId == agent.Id &&
                        (x.Status == CallStatus.Ringing || x.Status == CallStatus.Connected))
            .OrderByDescending(x => x.UpdatedAt ?? x.StartedAt)
            .ThenByDescending(x => x.Id)
            .Select(x => ToCallDto(x))
            .FirstOrDefaultAsync(cancellationToken);

        var queueCount = await dbContext.CallQueueEntries
            .AsNoTracking()
            .CountAsync(x => x.DequeuedAt == null, cancellationToken);

        var incomingQueue = await dbContext.CallQueueEntries
            .AsNoTracking()
            .Include(x => x.Call)
            .Include(x => x.CallQueue)
            .Where(x => x.DequeuedAt == null)
            .OrderBy(x => x.Position)
            .Take(5)
            .Select(x => new QueueItemSummaryDto
            {
                CallId = x.CallId,
                PhoneNumber = x.Call != null ? x.Call.PhoneNumber : "Unknown",
                Position = x.Position,
                EnqueuedAt = x.EnqueuedAt,
                QueueName = x.CallQueue != null ? x.CallQueue.Name : "General Queue"
            })
            .ToListAsync(cancellationToken);

        var recentCalls = await dbContext.Calls
            .AsNoTracking()
            .Include(x => x.Customer)
            .Include(x => x.CallDisposition)
            .Where(x => x.AssignedAgentId == agent.Id)
            .OrderByDescending(x => x.StartedAt)
            .ThenByDescending(x => x.Id)
            .Take(10)
            .Select(x => ToCallDto(x))
            .ToListAsync(cancellationToken);

        return new AgentDashboardResponseDto
        {
            AgentId = agent.Id,
            UserId = agent.UserId,
            EmployeeCode = agent.EmployeeCode,
            DisplayName = agent.DisplayName,
            Team = agent.Team,
            Status = agent.Status,
            TodaysCallsCount = todaysCallsCount,
            CompletedCallsCount = completedCallsCount,
            MissedCallsCount = missedCallsCount,
            AverageDurationSeconds = avgDuration,
            CurrentCall = currentCall,
            QueueCount = queueCount,
            IncomingQueue = incomingQueue,
            RecentCalls = recentCalls
        };
    }

    public async Task<AgentResponseDto> CreateAsync(
        CreateAgentRequestDto request,
        Guid? actorUserId = null,
        CancellationToken cancellationToken = default)
    {
        Guid userId;
        User user;

        if (request.UserId.HasValue && request.UserId.Value != Guid.Empty)
        {
            userId = request.UserId.Value;
            var existingUser = await dbContext.Users
                .Include(u => u.Role)
                .SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);

            if (existingUser is null)
                throw new KeyNotFoundException("The specified user was not found.");

            if (await dbContext.Agents.AnyAsync(x => x.UserId == userId, cancellationToken))
                throw new InvalidOperationException("The specified user is already linked to an agent.");

            user = existingUser;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.UserName))
                throw new ArgumentException("UserName is required when creating a new agent without an existing user.");

            var userName = request.UserName.Trim();
            if (await dbContext.Users.AnyAsync(x => x.UserName == userName, cancellationToken))
                throw new InvalidOperationException($"A user with username '{userName}' already exists.");

            var roleName = string.IsNullOrWhiteSpace(request.RoleName) ? "Agent" : request.RoleName.Trim();
            var role = await dbContext.Roles.SingleOrDefaultAsync(x => x.Name == roleName, cancellationToken)
                ?? throw new InvalidOperationException($"Role '{roleName}' does not exist.");

            userId = Guid.NewGuid();
            user = new User
            {
                Id = userId,
                RoleId = role.Id,
                Role = role,
                UserName = userName,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            var password = string.IsNullOrWhiteSpace(request.Password) ? "Agent123!" : request.Password;
            var hasher = new PasswordHasher<User>();
            user.PasswordHash = hasher.HashPassword(user, password);

            dbContext.Users.Add(user);
        }

        var employeeCode = request.EmployeeCode.Trim();
        if (await dbContext.Agents.AnyAsync(x => x.EmployeeCode == employeeCode, cancellationToken))
            throw new InvalidOperationException("An agent with the specified employee code already exists.");

        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            User = user,
            EmployeeCode = employeeCode,
            DisplayName = request.DisplayName.Trim(),
            Team = string.IsNullOrWhiteSpace(request.Team) ? null : request.Team.Trim(),
            IsActive = true,
            Status = AgentStatus.Offline,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.Agents.Add(agent);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (auditLogService is not null)
        {
            await auditLogService.LogAsync(
                actorUserId,
                "AgentCreated",
                "Agent",
                agent.Id.ToString(),
                new { agent.EmployeeCode, agent.DisplayName, agent.Team, user.UserName },
                cancellationToken);
        }

        return ToDto(agent, user);
    }

    public async Task<AgentResponseDto?> UpdateAsync(
        Guid id,
        UpdateAgentRequestDto request,
        Guid? actorUserId = null,
        CancellationToken cancellationToken = default)
    {
        var agent = await dbContext.Agents
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (agent is null) return null;

        var employeeCode = request.EmployeeCode.Trim();
        if (await dbContext.Agents.AnyAsync(
                x => x.Id != id && x.EmployeeCode == employeeCode,
                cancellationToken))
            throw new InvalidOperationException("An agent with the specified employee code already exists.");

        agent.EmployeeCode = employeeCode;
        agent.DisplayName = request.DisplayName.Trim();

        if (request.Team != null)
        {
            agent.Team = string.IsNullOrWhiteSpace(request.Team) ? null : request.Team.Trim();
        }

        var user = await dbContext.Users
            .Include(u => u.Role)
            .SingleOrDefaultAsync(u => u.Id == agent.UserId, cancellationToken);

        if (request.IsActive.HasValue)
        {
            agent.IsActive = request.IsActive.Value;
            if (!agent.IsActive)
            {
                agent.Status = AgentStatus.Offline;
                if (user != null) user.IsActive = false;
            }
            else
            {
                if (user != null) user.IsActive = true;
            }
        }

        if (!string.IsNullOrWhiteSpace(request.RoleName) && user != null)
        {
            var roleName = request.RoleName.Trim();
            var role = await dbContext.Roles.SingleOrDefaultAsync(x => x.Name == roleName, cancellationToken);
            if (role != null)
            {
                user.RoleId = role.Id;
                user.Role = role;
            }
        }

        if (!string.IsNullOrWhiteSpace(request.Password) && user != null)
        {
            var hasher = new PasswordHasher<User>();
            user.PasswordHash = hasher.HashPassword(user, request.Password);
        }

        agent.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        if (auditLogService is not null)
        {
            await auditLogService.LogAsync(
                actorUserId,
                "AgentUpdated",
                "Agent",
                agent.Id.ToString(),
                new { agent.EmployeeCode, agent.DisplayName, agent.Team, agent.IsActive },
                cancellationToken);
        }

        return ToDto(agent, user);
    }

    public async Task<AgentResponseDto?> DeactivateAsync(
        Guid id,
        Guid? actorUserId = null,
        CancellationToken cancellationToken = default)
    {
        var agent = await dbContext.Agents
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (agent is null) return null;

        agent.IsActive = false;
        agent.Status = AgentStatus.Offline;
        agent.UpdatedAt = DateTime.UtcNow;

        var user = await dbContext.Users
            .Include(u => u.Role)
            .SingleOrDefaultAsync(u => u.Id == agent.UserId, cancellationToken);

        if (user != null)
        {
            user.IsActive = false;
            user.UpdatedAt = DateTime.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        if (auditLogService is not null)
        {
            await auditLogService.LogAsync(
                actorUserId,
                "AgentUpdated",
                "Agent",
                agent.Id.ToString(),
                new { Action = "Deactivated", agent.EmployeeCode, agent.DisplayName },
                cancellationToken);
        }

        await realTimeNotifier.NotifyAgentStatusChangedAsync(
            new AgentStatusChangedEvent(
                agent.Id,
                agent.UserId,
                agent.Status.ToString(),
                DateTime.UtcNow),
            cancellationToken);

        return ToDto(agent, user);
    }

    public async Task<AgentResponseDto?> ReactivateAsync(
        Guid id,
        Guid? actorUserId = null,
        CancellationToken cancellationToken = default)
    {
        var agent = await dbContext.Agents
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (agent is null) return null;

        agent.IsActive = true;
        agent.UpdatedAt = DateTime.UtcNow;

        var user = await dbContext.Users
            .Include(u => u.Role)
            .SingleOrDefaultAsync(u => u.Id == agent.UserId, cancellationToken);

        if (user != null)
        {
            user.IsActive = true;
            user.UpdatedAt = DateTime.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        if (auditLogService is not null)
        {
            await auditLogService.LogAsync(
                actorUserId,
                "AgentUpdated",
                "Agent",
                agent.Id.ToString(),
                new { Action = "Reactivated", agent.EmployeeCode, agent.DisplayName },
                cancellationToken);
        }

        return ToDto(agent, user);
    }

    public async Task<bool> DeleteAsync(
        Guid id,
        Guid? actorUserId = null,
        CancellationToken cancellationToken = default)
    {
        var agent = await dbContext.Agents.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (agent is null) return false;

        if (await dbContext.Calls.AnyAsync(x => x.AssignedAgentId == id, cancellationToken))
            throw new InvalidOperationException("The agent cannot be deleted while call records reference the agent. Please deactivate the agent instead.");

        dbContext.Agents.Remove(agent);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (auditLogService is not null)
        {
            await auditLogService.LogAsync(
                actorUserId,
                "AgentDeleted",
                "Agent",
                id.ToString(),
                new { agent.EmployeeCode, agent.DisplayName },
                cancellationToken);
        }

        return true;
    }

    public async Task<AgentResponseDto?> UpdateStatusAsync(
        Guid id,
        AgentStatus status,
        Guid? actorUserId = null,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));

        var agent = await dbContext.Agents
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (agent is null) return null;

        if (!agent.IsActive && status != AgentStatus.Offline)
            throw new InvalidOperationException("Deactivated agents cannot change status until reactivated.");

        if (agent.Status == status)
        {
            var existingUser = await dbContext.Users
                .AsNoTracking()
                .Include(u => u.Role)
                .SingleOrDefaultAsync(u => u.Id == agent.UserId, cancellationToken);
            return ToDto(agent, existingUser);
        }

        var oldStatus = agent.Status;
        agent.Status = status;
        agent.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        if (auditLogService is not null)
        {
            await auditLogService.LogAsync(
                actorUserId,
                "AgentStatusChanged",
                "Agent",
                agent.Id.ToString(),
                new { agent.DisplayName, OldStatus = oldStatus.ToString(), NewStatus = status.ToString() },
                cancellationToken);
        }

        await realTimeNotifier.NotifyAgentStatusChangedAsync(
            new AgentStatusChangedEvent(
                agent.Id,
                agent.UserId,
                agent.Status.ToString(),
                DateTime.UtcNow),
            cancellationToken);

        if (status == AgentStatus.Available && queueService is not null)
        {
            await queueService.TryAutoAssignNextCallAsync(agent.Id, cancellationToken);
        }

        var updatedUser = await dbContext.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .SingleOrDefaultAsync(u => u.Id == agent.UserId, cancellationToken);

        return ToDto(agent, updatedUser);
    }

    private static CallResponseDto ToCallDto(Call call) => new()
    {
        Id = call.Id,
        CustomerId = call.CustomerId,
        CustomerName = call.Customer?.DisplayName ?? "Customer",
        CustomerPhone = call.Customer?.PhoneNumber ?? call.PhoneNumber,
        AssignedAgentId = call.AssignedAgentId,
        AgentName = call.AssignedAgent?.DisplayName,
        CallQueueId = call.CallQueueId,
        CallDispositionId = call.CallDispositionId,
        DispositionName = call.CallDisposition?.Name,
        ProviderCallId = call.ProviderCallId,
        CorrelationId = call.CorrelationId,
        Direction = call.Direction,
        Status = call.Status,
        PhoneNumber = call.PhoneNumber,
        Notes = call.Notes,
        StartedAt = call.StartedAt,
        AnsweredAt = call.AnsweredAt,
        EndedAt = call.EndedAt,
        DurationSeconds = call.EndedAt.HasValue
            ? (int)Math.Max(0, (call.EndedAt.Value - (call.AnsweredAt ?? call.StartedAt)).TotalSeconds)
            : null,
        CreatedAt = call.CreatedAt,
        UpdatedAt = call.UpdatedAt
    };

    private static AgentResponseDto ToDto(Agent agent, User? user = null) => new()
    {
        Id = agent.Id,
        UserId = agent.UserId,
        EmployeeCode = agent.EmployeeCode,
        DisplayName = agent.DisplayName,
        Team = agent.Team,
        IsActive = agent.IsActive,
        RoleName = user?.Role?.Name ?? agent.User?.Role?.Name,
        UserName = user?.UserName ?? agent.User?.UserName,
        Status = agent.Status,
        CreatedAt = agent.CreatedAt,
        UpdatedAt = agent.UpdatedAt
    };
}
