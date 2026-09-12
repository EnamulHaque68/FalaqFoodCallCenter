using CallCenter.Application.Audit;
using CallCenter.Application.Authentication;
using CallCenter.Application.Authentication.DTOs;
using CallCenter.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Infrastructure.Authentication;

public sealed class AuthService(
    CallCenterDbContext dbContext,
    IPasswordHasher<Domain.Entities.User> passwordHasher,
    JwtTokenService jwtTokenService,
    IAuditLogService? auditLogService = null) : IAuthService
{
    public async Task<LoginResponseDto?> LoginAsync(
        LoginRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users
            .Include(x => x.Role)
            .SingleOrDefaultAsync(
                x => x.UserName == request.UserName,
                cancellationToken);

        var now = DateTime.UtcNow;

        if (user is null || !user.IsActive)
        {
            if (auditLogService is not null)
            {
                await auditLogService.LogAsync(
                    user?.Id,
                    "LoginFailed",
                    "User",
                    user?.Id.ToString() ?? request.UserName,
                    new { request.UserName, Reason = user is null ? "UserNotFound" : "UserInactive" },
                    cancellationToken);
            }
            return null;
        }

        if (user.IsLockedOut(now))
        {
            if (auditLogService is not null)
            {
                await auditLogService.LogAsync(
                    user.Id,
                    "LoginRejectedLocked",
                    "User",
                    user.Id.ToString(),
                    new { user.UserName, LockoutEndUtc = user.LockoutEndUtc },
                    cancellationToken);
            }

            var remainingMinutes = Math.Max(1, (int)Math.Ceiling((user.LockoutEndUtc!.Value - now).TotalMinutes));
            throw new InvalidOperationException($"This account is temporarily locked due to multiple failed login attempts. Please try again in {remainingMinutes} minute(s).");
        }

        var verification = passwordHasher.VerifyHashedPassword(
            user,
            user.PasswordHash,
            request.Password);

        if (verification == PasswordVerificationResult.Failed)
        {
            user.FailedLoginAttempts++;

            if (user.FailedLoginAttempts >= 5)
            {
                user.LockoutEndUtc = now.AddMinutes(15);

                if (auditLogService is not null)
                {
                    await auditLogService.LogAsync(
                        user.Id,
                        "AccountLocked",
                        "User",
                        user.Id.ToString(),
                        new { user.UserName, LockoutEndUtc = user.LockoutEndUtc, FailedAttempts = user.FailedLoginAttempts },
                        cancellationToken);
                }
            }
            else if (auditLogService is not null)
            {
                await auditLogService.LogAsync(
                    user.Id,
                    "LoginFailed",
                    "User",
                    user.Id.ToString(),
                    new { user.UserName, FailedAttempts = user.FailedLoginAttempts },
                    cancellationToken);
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            if (user.IsLockedOut(now))
            {
                throw new InvalidOperationException("This account has been locked due to 5 consecutive failed login attempts. Please try again in 15 minutes.");
            }

            return null;
        }

        // Successful authentication: reset lockout & failed counter
        user.FailedLoginAttempts = 0;
        user.LockoutEndUtc = null;
        user.LastLoginAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        if (auditLogService is not null)
        {
            await auditLogService.LogAsync(
                user.Id,
                "Login",
                "User",
                user.Id.ToString(),
                new { user.UserName, Role = user.Role.Name },
                cancellationToken);
        }

        var (token, expiresAtUtc) = jwtTokenService.CreateToken(user);
        var permissions = CallCenter.Domain.Security.RolePermissionMatrix.GetPermissionsForRole(user.Role.Name);

        return new LoginResponseDto
        {
            AccessToken = token,
            ExpiresAtUtc = expiresAtUtc,
            UserId = user.Id,
            UserName = user.UserName,
            Role = user.Role.Name,
            Permissions = permissions
        };
    }

    public async Task LogoutAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (auditLogService is not null)
        {
            await auditLogService.LogAsync(
                userId,
                "Logout",
                "User",
                userId.ToString(),
                null,
                cancellationToken);
        }
    }

    public async Task<LoginResponseDto?> GetCurrentUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users
            .Include(x => x.Role)
            .SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);

        if (user is null || !user.IsActive)
        {
            return null;
        }

        var (token, expiresAtUtc) = jwtTokenService.CreateToken(user);
        var permissions = CallCenter.Domain.Security.RolePermissionMatrix.GetPermissionsForRole(user.Role.Name);

        return new LoginResponseDto
        {
            AccessToken = token,
            ExpiresAtUtc = expiresAtUtc,
            UserId = user.Id,
            UserName = user.UserName,
            Role = user.Role.Name,
            Permissions = permissions
        };
    }
}
