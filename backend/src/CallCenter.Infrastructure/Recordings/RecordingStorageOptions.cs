namespace CallCenter.Infrastructure.Recordings;

public sealed class RecordingStorageOptions
{
    public const string SectionName = "RecordingStorage";

    public string StoragePath { get; set; } = "App_Data/Recordings";
    public string? TokenSecretKey { get; set; }
    public int TokenExpirationSeconds { get; set; } = 60;
    public int DefaultRetentionDays { get; set; } = 90;
    public bool EnableRetentionCleanupWorker { get; set; } = true;
    public int CleanupIntervalHours { get; set; } = 24;
}
