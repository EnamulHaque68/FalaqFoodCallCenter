namespace CallCenter.Application.Audit.DTOs;

public sealed class AuditLogDto
{
    public Guid Id { get; init; }
    public Guid? UserId { get; init; }
    public string? UserName { get; init; }
    public string? UserRole { get; init; }
    public string Action { get; init; } = null!;
    public string EntityName { get; init; } = null!;
    public string? EntityId { get; init; }
    public string? DetailsJson { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed class AuditLogPagedResultDto
{
    public IReadOnlyList<AuditLogDto> Items { get; init; } = [];
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages { get; init; }
}

public sealed class AuditLogFilterDto
{
    public string? Search { get; set; }
    public string? Action { get; set; }
    public string? EntityName { get; set; }
    public Guid? UserId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public DateTime? FromUtc { get => FromDate; set => FromDate = value; }
    public DateTime? ToUtc { get => ToDate; set => ToDate = value; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
