using CallCenter.Domain.Enums;

namespace CallCenter.Domain.Entities;

public sealed class CallRecording
{
    public Guid Id { get; set; }
    public Guid CallId { get; set; }
    public string StorageUrl { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public string? StorageProvider { get; set; }
    public string? ProviderRecordingId { get; set; }
    public RecordingStatus Status { get; set; } = RecordingStatus.Available;
    public TimeSpan? Duration { get; set; }
    public long? FileSizeBytes { get; set; }
    public string? ContentType { get; set; }
    public string? ChecksumSha256 { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? RetentionUntil { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedByUserId { get; set; }

    public Call Call { get; set; } = null!;
}
