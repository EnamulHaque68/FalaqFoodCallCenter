namespace CallCenter.Application.Settings.DTOs;

public sealed class GeneralSettingsDto
{
    public string CallCenterName { get; set; } = "Falaq Food Call Center";
    public string OperatingHours { get; set; } = "08:00-23:00";
    public string SupportEmail { get; set; } = "support@falaqfood.com";
    public string DefaultLocale { get; set; } = "en-US";
    public string Timezone { get; set; } = "UTC";
}

public sealed class CallSettingsDto
{
    public int RingTimeoutSeconds { get; set; } = 30;
    public bool AutoAssignmentEnabled { get; set; } = true;
    public bool RecordingEnabled { get; set; } = true;
    public bool AllowCallTransfer { get; set; } = true;
    public bool RequireDisposition { get; set; } = true;
    public int MaxCallDurationMinutes { get; set; } = 60;
}

public sealed class QueueSettingsDto
{
    public int MaxQueueWaitSeconds { get; set; } = 300;
    public int MaxQueueCapacity { get; set; } = 50;
    public string QueueTimeoutAction { get; set; } = "Abandon";
    public bool PriorityRoutingEnabled { get; set; } = true;
    public bool AnnounceQueuePosition { get; set; } = true;
}

public sealed class NotificationSettingsDto
{
    public bool SoundAlertsEnabled { get; set; } = true;
    public int QueueWaitAlertThresholdSeconds { get; set; } = 120;
    public bool MissedCallAlertsEnabled { get; set; } = true;
    public bool DesktopNotificationsEnabled { get; set; } = true;
}

public sealed class SecuritySettingsDto
{
    public int SessionTimeoutMinutes { get; set; } = 60;
    public int MaxLoginAttempts { get; set; } = 5;
    public bool RequireStrongPassword { get; set; } = true;
    public bool AuditLoggingEnabled { get; set; } = true;
    public bool RestrictAgentOutbound { get; set; } = false;
}

public sealed class SettingItemDto
{
    public Guid Id { get; set; }
    public string Key { get; set; } = null!;
    public string Value { get; set; } = null!;
    public string Category { get; set; } = "General";
    public string DataType { get; set; } = "String";
    public string? Description { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class SystemSettingsDto
{
    public GeneralSettingsDto General { get; set; } = new();
    public CallSettingsDto Call { get; set; } = new();
    public QueueSettingsDto Queue { get; set; } = new();
    public NotificationSettingsDto Notification { get; set; } = new();
    public SecuritySettingsDto Security { get; set; } = new();
    public List<SettingItemDto> RawSettings { get; set; } = [];
}

public sealed class TimeoutProcessResultDto
{
    public int RingingCallsTimedOut { get; set; }
    public int QueueEntriesTimedOut { get; set; }
    public List<Guid> AffectedCallIds { get; set; } = [];
    public DateTime Timestamp { get; set; }
    public string Message { get; set; } = null!;
}
