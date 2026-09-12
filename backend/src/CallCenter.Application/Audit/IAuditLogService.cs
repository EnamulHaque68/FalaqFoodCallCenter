using CallCenter.Application.Audit.DTOs;

namespace CallCenter.Application.Audit;

public interface IAuditLogService
{
    Task LogAsync(
        Guid? actorUserId,
        string action,
        string entityName,
        string? entityId,
        object? details = null,
        CancellationToken cancellationToken = default);

    Task<AuditLogPagedResultDto> GetPagedAsync(
        AuditLogFilterDto filter,
        CancellationToken cancellationToken = default);

    Task<AuditLogDto?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetActionsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetEntitiesAsync(
        CancellationToken cancellationToken = default);
}
