using System.Text.Json;
using CallCenter.Application.Users;
using CallCenter.Application.Users.DTOs;
using CallCenter.Domain.Entities;
using CallCenter.Domain.Enums;
using CallCenter.Domain.Security;
using CallCenter.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Infrastructure.Users;

public sealed class UserService(
    CallCenterDbContext dbContext,
    IPasswordHasher<User> passwordHasher) : IUserService
{
    public async Task<IReadOnlyList<UserListItemDto>> GetAllAsync(
        string? search = null,
        string? role = null,
        bool? isActive = null,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Users
            .AsNoTracking()
            .Include(x => x.Role)
            .Include(x => x.Agent)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x =>
                x.UserName.Contains(term) ||
                (x.Agent != null && (x.Agent.DisplayName.Contains(term) || x.Agent.EmployeeCode.Contains(term))));
        }

        if (!string.IsNullOrWhiteSpace(role))
        {
            var roleTerm = role.Trim();
            query = query.Where(x => x.Role.Name == roleTerm);
        }

        if (isActive.HasValue)
        {
            query = query.Where(x => x.IsActive == isActive.Value);
        }

        return await query
            .OrderBy(x => x.UserName)
            .Select(x => Map(x))
            .ToListAsync(cancellationToken);
    }

    public async Task<UserListItemDto?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users
            .AsNoTracking()
            .Include(x => x.Role)
            .Include(x => x.Agent)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        return user is null ? null : Map(user);
    }

    public async Task<UserListItemDto> CreateAsync(
        CreateUserRequestDto request,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var name = request.UserName.Trim();
        if (await dbContext.Users.AnyAsync(x => x.UserName == name, cancellationToken))
            throw new InvalidOperationException("That username is already in use.");

        var role = await ResolveRoleAsync(request.RoleName, cancellationToken);

        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = name,
            RoleId = role.Id,
            Role = role,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
        dbContext.Users.Add(user);

        // If user is created with the Agent role, scaffold an Agent entity to maintain a clean relationship
        if (role.Name.Equals(RolePermissionMatrix.RoleAgent, StringComparison.OrdinalIgnoreCase))
        {
            var employeeCode = !string.IsNullOrWhiteSpace(request.EmployeeCode)
                ? request.EmployeeCode.Trim()
                : $"AGT-{name.ToUpperInvariant()}";

            if (await dbContext.Agents.AnyAsync(x => x.EmployeeCode == employeeCode, cancellationToken))
            {
                employeeCode = $"AGT-{Guid.NewGuid().ToString()[..6].ToUpperInvariant()}";
            }

            var displayName = !string.IsNullOrWhiteSpace(request.DisplayName)
                ? request.DisplayName.Trim()
                : name;

            var agent = new Agent
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                User = user,
                EmployeeCode = employeeCode,
                DisplayName = displayName,
                Team = string.IsNullOrWhiteSpace(request.Team) ? null : request.Team.Trim(),
                IsActive = true,
                Status = AgentStatus.Offline,
                CreatedAt = DateTime.UtcNow
            };

            dbContext.Agents.Add(agent);
            user.Agent = agent;
        }

        await AuditAsync(actorUserId, "CreateUser", user.Id.ToString(), new
        {
            user.UserName,
            Role = role.Name,
            user.IsActive,
            HasAgent = user.Agent != null
        }, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(user);
    }

    public async Task<UserListItemDto?> UpdateAsync(
        Guid id,
        UpdateUserRequestDto request,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users
            .Include(x => x.Role)
            .Include(x => x.Agent)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (user is null)
            return null;

        var name = request.UserName.Trim();
        if (await dbContext.Users.AnyAsync(x => x.Id != id && x.UserName == name, cancellationToken))
            throw new InvalidOperationException("That username is already in use.");

        // Anti-lockout guardrails
        if (actorUserId == id && !request.IsActive)
            throw new InvalidOperationException("You cannot deactivate your own account.");

        if (actorUserId == id && !request.RoleName.Equals(RolePermissionMatrix.RoleAdmin, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("You cannot remove your own Administrator role.");

        if (user.Role.Name == RolePermissionMatrix.RoleAdmin && !request.RoleName.Equals(RolePermissionMatrix.RoleAdmin, StringComparison.OrdinalIgnoreCase))
        {
            var activeAdminCount = await dbContext.Users.CountAsync(x => x.Role.Name == RolePermissionMatrix.RoleAdmin && x.IsActive, cancellationToken);
            if (activeAdminCount <= 1)
                throw new InvalidOperationException("Cannot demote the last active Administrator.");
        }

        if (user.Role.Name == RolePermissionMatrix.RoleAdmin && user.IsActive && !request.IsActive)
        {
            var activeAdminCount = await dbContext.Users.CountAsync(x => x.Role.Name == RolePermissionMatrix.RoleAdmin && x.IsActive, cancellationToken);
            if (activeAdminCount <= 1)
                throw new InvalidOperationException("Cannot deactivate the last active Administrator.");
        }

        var role = await ResolveRoleAsync(request.RoleName, cancellationToken);

        user.UserName = name;
        user.RoleId = role.Id;
        user.Role = role;
        user.IsActive = request.IsActive;
        user.UpdatedAt = DateTime.UtcNow;

        // Clean User & Agent synchronization
        if (user.Agent is not null)
        {
            user.Agent.IsActive = request.IsActive;
            if (!request.IsActive)
            {
                user.Agent.Status = AgentStatus.Offline;
            }

            if (!string.IsNullOrWhiteSpace(request.DisplayName))
            {
                user.Agent.DisplayName = request.DisplayName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(request.Team))
            {
                user.Agent.Team = request.Team.Trim();
            }

            user.Agent.UpdatedAt = DateTime.UtcNow;
        }
        else if (role.Name.Equals(RolePermissionMatrix.RoleAgent, StringComparison.OrdinalIgnoreCase))
        {
            // If user transitioned to Agent role and had no Agent record, create one
            var employeeCode = $"AGT-{name.ToUpperInvariant()}";
            if (await dbContext.Agents.AnyAsync(x => x.EmployeeCode == employeeCode, cancellationToken))
            {
                employeeCode = $"AGT-{Guid.NewGuid().ToString()[..6].ToUpperInvariant()}";
            }

            var agent = new Agent
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                User = user,
                EmployeeCode = employeeCode,
                DisplayName = !string.IsNullOrWhiteSpace(request.DisplayName) ? request.DisplayName.Trim() : name,
                Team = string.IsNullOrWhiteSpace(request.Team) ? null : request.Team.Trim(),
                IsActive = request.IsActive,
                Status = AgentStatus.Offline,
                CreatedAt = DateTime.UtcNow
            };

            dbContext.Agents.Add(agent);
            user.Agent = agent;
        }

        await AuditAsync(actorUserId, "UpdateUser", user.Id.ToString(), new
        {
            user.UserName,
            Role = role.Name,
            user.IsActive
        }, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(user);
    }

    public async Task<UserListItemDto?> DeactivateAsync(
        Guid id,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users
            .Include(x => x.Role)
            .Include(x => x.Agent)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (user is null)
            return null;

        if (actorUserId == id)
            throw new InvalidOperationException("You cannot deactivate your own account.");

        if (user.Role.Name == RolePermissionMatrix.RoleAdmin)
        {
            var activeAdminCount = await dbContext.Users.CountAsync(x => x.Role.Name == RolePermissionMatrix.RoleAdmin && x.IsActive, cancellationToken);
            if (activeAdminCount <= 1)
                throw new InvalidOperationException("Cannot deactivate the last active Administrator.");
        }

        user.IsActive = false;
        user.UpdatedAt = DateTime.UtcNow;

        if (user.Agent is not null)
        {
            user.Agent.IsActive = false;
            user.Agent.Status = AgentStatus.Offline;
            user.Agent.UpdatedAt = DateTime.UtcNow;
        }

        await AuditAsync(actorUserId, "DeactivateUser", user.Id.ToString(), new { user.UserName }, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(user);
    }

    public async Task<UserListItemDto?> ReactivateAsync(
        Guid id,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users
            .Include(x => x.Role)
            .Include(x => x.Agent)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (user is null)
            return null;

        user.IsActive = true;
        user.UpdatedAt = DateTime.UtcNow;

        if (user.Agent is not null)
        {
            user.Agent.IsActive = true;
            user.Agent.UpdatedAt = DateTime.UtcNow;
        }

        await AuditAsync(actorUserId, "ReactivateUser", user.Id.ToString(), new { user.UserName }, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(user);
    }

    public async Task<UserListItemDto?> ResetPasswordAsync(
        Guid id,
        ResetPasswordRequestDto request,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users
            .Include(x => x.Role)
            .Include(x => x.Agent)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (user is null)
            return null;

        user.PasswordHash = passwordHasher.HashPassword(user, request.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;

        await AuditAsync(actorUserId, "ResetUserPassword", user.Id.ToString(), new { user.UserName }, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(user);
    }

    public async Task<bool> ChangePasswordAsync(
        Guid id,
        ChangePasswordRequestDto request,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (user is null)
            return false;

        var verificationResult = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword);
        if (verificationResult == PasswordVerificationResult.Failed)
            return false;

        user.PasswordHash = passwordHasher.HashPassword(user, request.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;

        await AuditAsync(actorUserId, "ChangeUserPassword", user.Id.ToString(), new { user.UserName }, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<RoleDto>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        var roles = await dbContext.Roles
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return roles.Select(r => new RoleDto
        {
            Id = r.Id,
            Name = r.Name,
            Description = r.Description,
            Permissions = RolePermissionMatrix.GetPermissionsForRole(r.Name)
        }).ToList();
    }

    private async Task<Role> ResolveRoleAsync(string roleName, CancellationToken cancellationToken) =>
        await dbContext.Roles.SingleOrDefaultAsync(x => x.Name == roleName.Trim(), cancellationToken)
        ?? throw new ArgumentException($"The selected role '{roleName}' does not exist.");

    private Task AuditAsync(Guid actor, string action, string entityId, object details, CancellationToken _)
    {
        dbContext.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = actor,
            Action = action,
            EntityName = "User",
            EntityId = entityId,
            DetailsJson = JsonSerializer.Serialize(details),
            CreatedAt = DateTime.UtcNow
        });
        return Task.CompletedTask;
    }

    private static UserListItemDto Map(User x) => new()
    {
        Id = x.Id,
        UserName = x.UserName,
        RoleName = x.Role?.Name ?? "Unknown",
        IsActive = x.IsActive,
        CreatedAt = x.CreatedAt,
        UpdatedAt = x.UpdatedAt,
        LastLoginAt = x.LastLoginAt,
        AgentId = x.Agent?.Id,
        AgentName = x.Agent?.DisplayName,
        EmployeeCode = x.Agent?.EmployeeCode,
        Team = x.Agent?.Team,
        AgentStatus = x.Agent?.Status.ToString()
    };
}
