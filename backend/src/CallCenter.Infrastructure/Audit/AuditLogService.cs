using System.Text.Json;
using System.Text.Json.Nodes;
using CallCenter.Application.Audit;
using CallCenter.Application.Audit.DTOs;
using CallCenter.Domain.Entities;
using CallCenter.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Infrastructure.Audit;

public sealed class AuditLogService(CallCenterDbContext dbContext) : IAuditLogService
{
    private static readonly string[] SensitiveKeywords =
    [
        "password", "token", "secret", "jwt", "key", "hash", "accesstoken", "refreshtoken",
        "auth", "pin", "cvv", "credential", "card"
    ];

    public async Task LogAsync(
        Guid? actorUserId,
        string action,
        string entityName,
        string? entityId,
        object? details = null,
        CancellationToken cancellationToken = default)
    {
        string? detailsJson = null;
        if (details != null)
        {
            detailsJson = SanitizeAndSerialize(details);
        }

        var auditLog = new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = actorUserId,
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            DetailsJson = detailsJson,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.AuditLogs.Add(auditLog);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<AuditLogPagedResultDto> GetPagedAsync(
        AuditLogFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);

        var query = dbContext.AuditLogs
            .AsNoTracking()
            .Include(x => x.User)
                .ThenInclude(u => u!.Role)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(x =>
                x.Action.Contains(term) ||
                x.EntityName.Contains(term) ||
                (x.EntityId != null && x.EntityId.Contains(term)) ||
                (x.User != null && x.User.UserName.Contains(term)) ||
                (x.DetailsJson != null && x.DetailsJson.Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(filter.Action))
        {
            var act = filter.Action.Trim();
            query = query.Where(x => x.Action == act);
        }

        if (!string.IsNullOrWhiteSpace(filter.EntityName))
        {
            var ent = filter.EntityName.Trim();
            query = query.Where(x => x.EntityName == ent);
        }

        if (filter.UserId.HasValue)
        {
            query = query.Where(x => x.UserId == filter.UserId.Value);
        }

        if (filter.FromDate.HasValue)
        {
            query = query.Where(x => x.CreatedAt >= filter.FromDate.Value);
        }

        if (filter.ToDate.HasValue)
        {
            query = query.Where(x => x.CreatedAt <= filter.ToDate.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new AuditLogDto
            {
                Id = x.Id,
                UserId = x.UserId,
                UserName = x.User != null ? x.User.UserName : (x.UserId.HasValue ? "System/Deleted" : "System"),
                UserRole = x.User != null && x.User.Role != null ? x.User.Role.Name : null,
                Action = x.Action,
                EntityName = x.EntityName,
                EntityId = x.EntityId,
                DetailsJson = x.DetailsJson,
                CreatedAt = x.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return new AuditLogPagedResultDto
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages
        };
    }

    public async Task<AuditLogDto?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var item = await dbContext.AuditLogs
            .AsNoTracking()
            .Include(x => x.User)
                .ThenInclude(u => u!.Role)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (item is null) return null;

        return new AuditLogDto
        {
            Id = item.Id,
            UserId = item.UserId,
            UserName = item.User != null ? item.User.UserName : (item.UserId.HasValue ? "System/Deleted" : "System"),
            UserRole = item.User != null && item.User.Role != null ? item.User.Role.Name : null,
            Action = item.Action,
            EntityName = item.EntityName,
            EntityId = item.EntityId,
            DetailsJson = item.DetailsJson,
            CreatedAt = item.CreatedAt
        };
    }

    public async Task<IReadOnlyList<string>> GetActionsAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.AuditLogs
            .AsNoTracking()
            .Select(x => x.Action)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetEntitiesAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.AuditLogs
            .AsNoTracking()
            .Select(x => x.EntityName)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(cancellationToken);
    }

    public static string SanitizeAndSerialize(object value)
    {
        try
        {
            var rawJson = JsonSerializer.Serialize(value);
            var node = JsonNode.Parse(rawJson);
            if (node != null)
            {
                SanitizeNode(node);
                return node.ToJsonString();
            }
            return rawJson;
        }
        catch
        {
            return JsonSerializer.Serialize(new { Value = value.ToString() });
        }
    }

    private static void SanitizeNode(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            var keys = obj.Select(kv => kv.Key).ToList();
            foreach (var key in keys)
            {
                var lowerKey = key.ToLowerInvariant();
                if (SensitiveKeywords.Any(k => lowerKey.Contains(k)))
                {
                    obj[key] = "[REDACTED]";
                }
                else if (obj[key] is JsonNode child)
                {
                    SanitizeNode(child);
                }
            }
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is not null)
                {
                    SanitizeNode(item);
                }
            }
        }
    }
}
