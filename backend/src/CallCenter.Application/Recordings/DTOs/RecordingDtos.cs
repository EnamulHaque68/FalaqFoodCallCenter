using CallCenter.Domain.Enums;

namespace CallCenter.Application.Recordings.DTOs;

public sealed class CallRecordingDto
{
    public Guid Id { get; init; }
    public Guid CallId { get; init; }
    public string? CustomerPhone { get; init; }
    public Guid? AssignedAgentId { get; init; }
    public string? AgentName { get; init; }
    public string? ProviderRecordingId { get; init; }
    public int? DurationSeconds { get; init; }
    public long? FileSizeBytes { get; init; }
    public string? ContentType { get; init; }
    public RecordingStatus Status { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public DateTime? RetentionUntil { get; init; }
    public bool IsDeleted { get; init; }
}

public sealed class RecordingQueryDto
{
    public string? Search { get; set; }
    public Guid? CallId { get; set; }
    public Guid? AgentId { get; set; }
    public Guid? CustomerId { get; set; }
    public RecordingStatus? Status { get; set; }
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public sealed class RecordingPagedResultDto
{
    public IReadOnlyList<CallRecordingDto> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
}

public sealed class PlaybackTokenResponseDto
{
    public Guid RecordingId { get; init; }
    public string Token { get; init; } = null!;
    public DateTime ExpiresAtUtc { get; init; }
    public string StreamUrl { get; init; } = null!;
}

public sealed class RetentionCleanupResultDto
{
    public int PurgedCount { get; init; }
    public IReadOnlyList<Guid> PurgedRecordingIds { get; init; } = [];
    public DateTime ExecutedAt { get; init; }
    public string Message { get; init; } = null!;
}

public sealed class RetentionSummaryDto
{
    public int TotalRecordings { get; init; }
    public int ActiveRecordings { get; init; }
    public int ExpiredPendingPurge { get; init; }
    public int PurgedRecordings { get; init; }
    public DateTime? OldestActiveCreatedAt { get; init; }
    public int DefaultRetentionDays { get; init; }
}
