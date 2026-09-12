using System.Globalization;
using System.Text.Json;
using CallCenter.Application.RealTime;
using CallCenter.Application.RealTime.Contracts;
using CallCenter.Application.Settings;
using CallCenter.Application.Settings.DTOs;
using CallCenter.Domain.Entities;
using CallCenter.Domain.Enums;
using CallCenter.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Infrastructure.Settings;

public sealed class SettingsService(
    CallCenterDbContext dbContext,
    IRealTimeNotifier? realTimeNotifier = null) : ISettingsService
{
    private static readonly (string Key, string Value, string Category, string DataType, string Description)[] CanonicalDefaults =
    [
        // General
        ("CallCenterName", "Falaq Food Call Center", "General", "String", "Call center brand and display name"),
        ("OperatingHours", "08:00-23:00", "General", "String", "Daily operating hours in 24-hour format (HH:mm-HH:mm)"),
        ("SupportEmail", "support@falaqfood.com", "General", "String", "Official customer support contact email"),
        ("DefaultLocale", "en-US", "General", "String", "Default platform locale and language"),
        ("Timezone", "UTC", "General", "String", "System reference timezone"),

        // Call
        ("RingTimeoutSeconds", "30", "Call", "Number", "Timeout in seconds before an unanswered ringing call moves to queue or fallback"),
        ("AutoAssignmentEnabled", "true", "Call", "Boolean", "Automatically route incoming calls to available agents vs queue for manual pickup"),
        ("RecordingEnabled", "true", "Call", "Boolean", "Automatically initialize call recording on connect"),
        ("AllowCallTransfer", "true", "Call", "Boolean", "Enable agents and supervisors to transfer active calls"),
        ("RequireDisposition", "true", "Call", "Boolean", "Require a valid outcome disposition when completing a call"),
        ("MaxCallDurationMinutes", "60", "Call", "Number", "Maximum allowed call duration before supervisor alert"),

        // Queue
        ("MaxQueueWaitSeconds", "300", "Queue", "Number", "Maximum wait time in queue before timeout or abandonment"),
        ("MaxQueueCapacity", "50", "Queue", "Number", "Maximum concurrent calls held in waiting queues"),
        ("QueueTimeoutAction", "Abandon", "Queue", "String", "Action when queue wait exceeds threshold (Abandon, Voicemail, Transfer)"),
        ("PriorityRoutingEnabled", "true", "Queue", "Boolean", "Prioritize high-value or VIP queue calls"),
        ("AnnounceQueuePosition", "true", "Queue", "Boolean", "Announce estimated position to callers"),

        // Notification
        ("SoundAlertsEnabled", "true", "Notification", "Boolean", "Play audio alerts for incoming calls and urgent events"),
        ("QueueWaitAlertThresholdSeconds", "120", "Notification", "Number", "Alert supervisors when caller wait time exceeds this threshold"),
        ("MissedCallAlertsEnabled", "true", "Notification", "Boolean", "Notify supervisor when an incoming call is missed"),
        ("DesktopNotificationsEnabled", "true", "Notification", "Boolean", "Display browser desktop push notifications"),

        // Security
        ("SessionTimeoutMinutes", "60", "Security", "Number", "Inactivity duration before automatic user logout"),
        ("MaxLoginAttempts", "5", "Security", "Number", "Maximum failed login attempts before temporary account lockout"),
        ("RequireStrongPassword", "true", "Security", "Boolean", "Require numbers and special characters in user passwords"),
        ("AuditLoggingEnabled", "true", "Security", "Boolean", "Log administrative changes to AuditLogs table"),
        ("RestrictAgentOutbound", "false", "Security", "Boolean", "Restrict agents to only dialing registered customers")
    ];

    public async Task<SystemSettingsDto> GetAllSettingsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureDefaultsAsync(cancellationToken);

        var entities = await dbContext.SystemSettings
            .AsNoTracking()
            .OrderBy(x => x.Category)
            .ThenBy(x => x.Key)
            .ToListAsync(cancellationToken);

        return MapToDto(entities);
    }

    public async Task<T> GetValueAsync<T>(string key, T defaultValue, CancellationToken cancellationToken = default)
    {
        var setting = await dbContext.SystemSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == key, cancellationToken);

        if (setting is null)
        {
            var fallback = CanonicalDefaults.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
            if (fallback.Key is not null)
            {
                return ConvertValue(fallback.Value, defaultValue);
            }
            return defaultValue;
        }

        return ConvertValue(setting.Value, defaultValue);
    }

    public async Task<SystemSettingsDto> UpdateCategorySettingsAsync(
        string category,
        JsonElement payload,
        Guid? adminUserId,
        CancellationToken cancellationToken = default)
    {
        await EnsureDefaultsAsync(cancellationToken);

        var canonicalCategory = CanonicalDefaults
            .Select(x => x.Category)
            .FirstOrDefault(c => string.Equals(c, category, StringComparison.OrdinalIgnoreCase))
            ?? category;

        var existingList = await dbContext.SystemSettings
            .Where(x => x.Category == canonicalCategory || x.Category.ToLower() == category.ToLower())
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var modifiedKeys = new List<string>();

        if (payload.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in payload.EnumerateObject())
            {
                var setting = existingList.FirstOrDefault(x =>
                    string.Equals(x.Key, prop.Name, StringComparison.OrdinalIgnoreCase));

                var stringVal = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString() ?? string.Empty,
                    JsonValueKind.Number => prop.Value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => prop.Value.GetRawText()
                };

                if (setting is not null)
                {
                    if (setting.Value != stringVal)
                    {
                        setting.Value = stringVal;
                        setting.UpdatedAt = now;
                        modifiedKeys.Add(setting.Key);
                    }
                }
                else
                {
                    var canonicalDef = CanonicalDefaults.FirstOrDefault(x =>
                        string.Equals(x.Key, prop.Name, StringComparison.OrdinalIgnoreCase));

                    var newSetting = new SystemSetting
                    {
                        Id = Guid.NewGuid(),
                        Key = canonicalDef.Key ?? prop.Name,
                        Value = stringVal,
                        Category = canonicalCategory,
                        DataType = canonicalDef.DataType ?? "String",
                        Description = canonicalDef.Description ?? "Custom setting",
                        UpdatedAt = now
                    };
                    dbContext.SystemSettings.Add(newSetting);
                    modifiedKeys.Add(newSetting.Key);
                }
            }
        }

        if (modifiedKeys.Count > 0)
        {
            dbContext.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                UserId = adminUserId,
                Action = $"SettingsChanged:{canonicalCategory}",
                EntityName = "SystemSetting",
                EntityId = canonicalCategory,
                DetailsJson = JsonSerializer.Serialize(new { Category = canonicalCategory, ModifiedKeys = modifiedKeys, Payload = payload }),
                CreatedAt = now
            });

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return await GetAllSettingsAsync(cancellationToken);
    }

    public async Task<SystemSettingsDto> UpdateAllSettingsAsync(
        SystemSettingsDto dto,
        Guid? adminUserId,
        CancellationToken cancellationToken = default)
    {
        Validate(dto);
        await EnsureDefaultsAsync(cancellationToken);

        var existing = await dbContext.SystemSettings.ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var modifiedKeys = new List<string>();

        void Apply(string key, string category, string dataType, string val, string desc)
        {
            var s = existing.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
            if (s is not null)
            {
                if (s.Value != val)
                {
                    s.Value = val;
                    s.UpdatedAt = now;
                    modifiedKeys.Add(key);
                }
            }
            else
            {
                dbContext.SystemSettings.Add(new SystemSetting
                {
                    Id = Guid.NewGuid(),
                    Key = key,
                    Value = val,
                    Category = category,
                    DataType = dataType,
                    Description = desc,
                    UpdatedAt = now
                });
                modifiedKeys.Add(key);
            }
        }

        // General
        Apply("CallCenterName", "General", "String", dto.General.CallCenterName, "Call center brand and display name");
        Apply("OperatingHours", "General", "String", dto.General.OperatingHours, "Daily operating hours");
        Apply("SupportEmail", "General", "String", dto.General.SupportEmail, "Official customer support contact email");
        Apply("DefaultLocale", "General", "String", dto.General.DefaultLocale, "Default platform locale");
        Apply("Timezone", "General", "String", dto.General.Timezone, "System reference timezone");

        // Call
        Apply("RingTimeoutSeconds", "Call", "Number", dto.Call.RingTimeoutSeconds.ToString(CultureInfo.InvariantCulture), "Ring timeout in seconds");
        Apply("AutoAssignmentEnabled", "Call", "Boolean", dto.Call.AutoAssignmentEnabled ? "true" : "false", "Auto assignment toggle");
        Apply("RecordingEnabled", "Call", "Boolean", dto.Call.RecordingEnabled ? "true" : "false", "Recording toggle");
        Apply("AllowCallTransfer", "Call", "Boolean", dto.Call.AllowCallTransfer ? "true" : "false", "Allow call transfer toggle");
        Apply("RequireDisposition", "Call", "Boolean", dto.Call.RequireDisposition ? "true" : "false", "Require disposition toggle");
        Apply("MaxCallDurationMinutes", "Call", "Number", dto.Call.MaxCallDurationMinutes.ToString(CultureInfo.InvariantCulture), "Max call duration in minutes");

        // Queue
        Apply("MaxQueueWaitSeconds", "Queue", "Number", dto.Queue.MaxQueueWaitSeconds.ToString(CultureInfo.InvariantCulture), "Max queue wait in seconds");
        Apply("MaxQueueCapacity", "Queue", "Number", dto.Queue.MaxQueueCapacity.ToString(CultureInfo.InvariantCulture), "Max concurrent queue callers");
        Apply("QueueTimeoutAction", "Queue", "String", dto.Queue.QueueTimeoutAction, "Action on queue timeout");
        Apply("PriorityRoutingEnabled", "Queue", "Boolean", dto.Queue.PriorityRoutingEnabled ? "true" : "false", "Priority routing toggle");
        Apply("AnnounceQueuePosition", "Queue", "Boolean", dto.Queue.AnnounceQueuePosition ? "true" : "false", "Announce position toggle");

        // Notification
        Apply("SoundAlertsEnabled", "Notification", "Boolean", dto.Notification.SoundAlertsEnabled ? "true" : "false", "Sound alerts toggle");
        Apply("QueueWaitAlertThresholdSeconds", "Notification", "Number", dto.Notification.QueueWaitAlertThresholdSeconds.ToString(CultureInfo.InvariantCulture), "Queue wait alert threshold in seconds");
        Apply("MissedCallAlertsEnabled", "Notification", "Boolean", dto.Notification.MissedCallAlertsEnabled ? "true" : "false", "Missed call alerts toggle");
        Apply("DesktopNotificationsEnabled", "Notification", "Boolean", dto.Notification.DesktopNotificationsEnabled ? "true" : "false", "Desktop notifications toggle");

        // Security
        Apply("SessionTimeoutMinutes", "Security", "Number", dto.Security.SessionTimeoutMinutes.ToString(CultureInfo.InvariantCulture), "Session timeout in minutes");
        Apply("MaxLoginAttempts", "Security", "Number", dto.Security.MaxLoginAttempts.ToString(CultureInfo.InvariantCulture), "Max login attempts");
        Apply("RequireStrongPassword", "Security", "Boolean", dto.Security.RequireStrongPassword ? "true" : "false", "Require strong password toggle");
        Apply("AuditLoggingEnabled", "Security", "Boolean", dto.Security.AuditLoggingEnabled ? "true" : "false", "Audit logging toggle");
        Apply("RestrictAgentOutbound", "Security", "Boolean", dto.Security.RestrictAgentOutbound ? "true" : "false", "Restrict agent outbound toggle");

        if (modifiedKeys.Count > 0)
        {
            dbContext.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                UserId = adminUserId,
                Action = "SettingsChanged",
                EntityName = "SystemSetting",
                EntityId = "All",
                DetailsJson = JsonSerializer.Serialize(new { ModifiedCount = modifiedKeys.Count, ModifiedKeys = modifiedKeys }),
                CreatedAt = now
            });

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return await GetAllSettingsAsync(cancellationToken);
    }

    public async Task<SystemSettingsDto> ResetToDefaultsAsync(
        Guid? adminUserId,
        CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.SystemSettings.ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;

        foreach (var def in CanonicalDefaults)
        {
            var s = existing.FirstOrDefault(x => string.Equals(x.Key, def.Key, StringComparison.OrdinalIgnoreCase));
            if (s is not null)
            {
                s.Value = def.Value;
                s.Category = def.Category;
                s.DataType = def.DataType;
                s.Description = def.Description;
                s.UpdatedAt = now;
            }
            else
            {
                dbContext.SystemSettings.Add(new SystemSetting
                {
                    Id = Guid.NewGuid(),
                    Key = def.Key,
                    Value = def.Value,
                    Category = def.Category,
                    DataType = def.DataType,
                    Description = def.Description,
                    UpdatedAt = now
                });
            }
        }

        dbContext.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = adminUserId,
            Action = "SettingsChanged:ResetToDefaults",
            EntityName = "SystemSetting",
            EntityId = "All",
            DetailsJson = JsonSerializer.Serialize(new { Message = "All system settings were reset to defaults." }),
            CreatedAt = now
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetAllSettingsAsync(cancellationToken);
    }

    public async Task<TimeoutProcessResultDto> ProcessTimeoutsAsync(CancellationToken cancellationToken = default)
    {
        var ringTimeoutSeconds = await GetValueAsync("RingTimeoutSeconds", 30, cancellationToken);
        var maxQueueWaitSeconds = await GetValueAsync("MaxQueueWaitSeconds", 300, cancellationToken);
        var timeoutAction = await GetValueAsync("QueueTimeoutAction", "Abandon", cancellationToken);

        var now = DateTime.UtcNow;
        var ringCutoff = now.AddSeconds(-ringTimeoutSeconds);
        var queueCutoff = now.AddSeconds(-maxQueueWaitSeconds);

        var affectedCalls = new List<Guid>();

        // 1. Process Ringing Timeouts
        var ringingCalls = await dbContext.Calls
            .Include(c => c.Events)
            .Where(c => c.Status == CallStatus.Ringing && (c.UpdatedAt ?? c.StartedAt) < ringCutoff)
            .ToListAsync(cancellationToken);

        foreach (var call in ringingCalls)
        {
            var oldAgentId = call.AssignedAgentId;
            call.AssignedAgentId = null;
            call.Status = CallStatus.Queued;
            call.UpdatedAt = now;

            dbContext.CallEvents.Add(new CallEvent
            {
                Id = Guid.NewGuid(),
                CallId = call.Id,
                AgentId = oldAgentId,
                EventType = "RingTimeout",
                OccurredAt = now,
                MetadataJson = $"{{\"timeoutSeconds\":{ringTimeoutSeconds},\"previousAgentId\":\"{oldAgentId}\",\"reason\":\"Unanswered ring timed out\"}}"
            });

            affectedCalls.Add(call.Id);

            if (realTimeNotifier is not null)
            {
                await realTimeNotifier.NotifyCallStatusChangedAsync(
                    new CallStatusChangedEvent(call.Id, null, call.CustomerId, call.Status.ToString(), now),
                    cancellationToken);
            }
        }

        // 2. Process Queue Timeouts
        var expiredQueueEntries = await dbContext.CallQueueEntries
            .Include(e => e.Call)
            .Where(e => e.DequeuedAt == null && e.EnqueuedAt < queueCutoff)
            .ToListAsync(cancellationToken);

        foreach (var entry in expiredQueueEntries)
        {
            entry.DequeuedAt = now;

            if (entry.Call is not null && !entry.Call.IsTerminal)
            {
                var nextStatus = string.Equals(timeoutAction, "Voicemail", StringComparison.OrdinalIgnoreCase)
                    ? CallStatus.Completed
                    : CallStatus.Abandoned;

                entry.Call.Status = nextStatus;
                entry.Call.EndedAt = now;
                entry.Call.UpdatedAt = now;

                dbContext.CallEvents.Add(new CallEvent
                {
                    Id = Guid.NewGuid(),
                    CallId = entry.Call.Id,
                    EventType = "QueueTimeout",
                    OccurredAt = now,
                    MetadataJson = $"{{\"queueId\":\"{entry.CallQueueId}\",\"waitSeconds\":{maxQueueWaitSeconds},\"action\":\"{timeoutAction}\"}}"
                });

                affectedCalls.Add(entry.Call.Id);

                if (realTimeNotifier is not null)
                {
                    await realTimeNotifier.NotifyCallStatusChangedAsync(
                        new CallStatusChangedEvent(entry.Call.Id, null, entry.Call.CustomerId, entry.Call.Status.ToString(), now),
                        cancellationToken);
                }
            }
        }

        if (ringingCalls.Count > 0 || expiredQueueEntries.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new TimeoutProcessResultDto
        {
            RingingCallsTimedOut = ringingCalls.Count,
            QueueEntriesTimedOut = expiredQueueEntries.Count,
            AffectedCallIds = affectedCalls.Distinct().ToList(),
            Timestamp = now,
            Message = $"Processed {ringingCalls.Count} ringing timeouts and {expiredQueueEntries.Count} queue wait timeouts."
        };
    }

    private async Task EnsureDefaultsAsync(CancellationToken cancellationToken)
    {
        var existingKeys = await dbContext.SystemSettings
            .Select(x => x.Key)
            .ToListAsync(cancellationToken);

        var missing = CanonicalDefaults
            .Where(d => !existingKeys.Contains(d.Key, StringComparer.OrdinalIgnoreCase))
            .Select(d => new SystemSetting
            {
                Id = Guid.NewGuid(),
                Key = d.Key,
                Value = d.Value,
                Category = d.Category,
                DataType = d.DataType,
                Description = d.Description,
                UpdatedAt = DateTime.UtcNow
            })
            .ToList();

        if (missing.Count > 0)
        {
            dbContext.SystemSettings.AddRange(missing);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static SystemSettingsDto MapToDto(List<SystemSetting> list)
    {
        var dto = new SystemSettingsDto
        {
            RawSettings = list.Select(x => new SettingItemDto
            {
                Id = x.Id,
                Key = x.Key,
                Value = x.Value,
                Category = x.Category,
                DataType = x.DataType,
                Description = x.Description,
                UpdatedAt = x.UpdatedAt
            }).ToList()
        };

        string Get(string key, string fallback = "") =>
            list.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase))?.Value ?? fallback;

        int GetInt(string key, int fallback = 0) =>
            int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

        bool GetBool(string key, bool fallback = false) =>
            bool.TryParse(Get(key), out var v) ? v : fallback;

        // General
        dto.General.CallCenterName = Get("CallCenterName", "Falaq Food Call Center");
        dto.General.OperatingHours = Get("OperatingHours", "08:00-23:00");
        dto.General.SupportEmail = Get("SupportEmail", "support@falaqfood.com");
        dto.General.DefaultLocale = Get("DefaultLocale", "en-US");
        dto.General.Timezone = Get("Timezone", "UTC");

        // Call
        dto.Call.RingTimeoutSeconds = GetInt("RingTimeoutSeconds", 30);
        dto.Call.AutoAssignmentEnabled = GetBool("AutoAssignmentEnabled", true);
        dto.Call.RecordingEnabled = GetBool("RecordingEnabled", true);
        dto.Call.AllowCallTransfer = GetBool("AllowCallTransfer", true);
        dto.Call.RequireDisposition = GetBool("RequireDisposition", true);
        dto.Call.MaxCallDurationMinutes = GetInt("MaxCallDurationMinutes", 60);

        // Queue
        dto.Queue.MaxQueueWaitSeconds = GetInt("MaxQueueWaitSeconds", 300);
        dto.Queue.MaxQueueCapacity = GetInt("MaxQueueCapacity", 50);
        dto.Queue.QueueTimeoutAction = Get("QueueTimeoutAction", "Abandon");
        dto.Queue.PriorityRoutingEnabled = GetBool("PriorityRoutingEnabled", true);
        dto.Queue.AnnounceQueuePosition = GetBool("AnnounceQueuePosition", true);

        // Notification
        dto.Notification.SoundAlertsEnabled = GetBool("SoundAlertsEnabled", true);
        dto.Notification.QueueWaitAlertThresholdSeconds = GetInt("QueueWaitAlertThresholdSeconds", 120);
        dto.Notification.MissedCallAlertsEnabled = GetBool("MissedCallAlertsEnabled", true);
        dto.Notification.DesktopNotificationsEnabled = GetBool("DesktopNotificationsEnabled", true);

        // Security
        dto.Security.SessionTimeoutMinutes = GetInt("SessionTimeoutMinutes", 60);
        dto.Security.MaxLoginAttempts = GetInt("MaxLoginAttempts", 5);
        dto.Security.RequireStrongPassword = GetBool("RequireStrongPassword", true);
        dto.Security.AuditLoggingEnabled = GetBool("AuditLoggingEnabled", true);
        dto.Security.RestrictAgentOutbound = GetBool("RestrictAgentOutbound", false);

        return dto;
    }

    private static T ConvertValue<T>(string raw, T defaultValue)
    {
        if (string.IsNullOrWhiteSpace(raw)) return defaultValue;

        try
        {
            var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

            if (targetType == typeof(int))
                return (T)(object)int.Parse(raw, CultureInfo.InvariantCulture);
            if (targetType == typeof(bool))
                return (T)(object)bool.Parse(raw);
            if (targetType == typeof(double))
                return (T)(object)double.Parse(raw, CultureInfo.InvariantCulture);
            if (targetType == typeof(string))
                return (T)(object)raw;

            return (T)Convert.ChangeType(raw, targetType, CultureInfo.InvariantCulture);
        }
        catch
        {
            return defaultValue;
        }
    }

    private static void Validate(SystemSettingsDto settings)
    {
        if (string.IsNullOrWhiteSpace(settings.General.CallCenterName) || settings.General.CallCenterName.Length > 100)
            throw new ArgumentException("Call center name is required and must be at most 100 characters.");
        if (string.IsNullOrWhiteSpace(settings.General.SupportEmail) || !settings.General.SupportEmail.Contains('@'))
            throw new ArgumentException("A valid support email is required.");
        if (settings.Call.RingTimeoutSeconds is < 5 or > 600)
            throw new ArgumentException("Ring timeout must be between 5 and 600 seconds.");
        if (settings.Call.MaxCallDurationMinutes is < 1 or > 1440)
            throw new ArgumentException("Maximum call duration must be between 1 and 1440 minutes.");
        if (settings.Queue.MaxQueueWaitSeconds is < 10 or > 86400 || settings.Queue.MaxQueueCapacity is < 1 or > 10000)
            throw new ArgumentException("Queue timeout or capacity is outside the permitted range.");
        if (!new[] { "Abandon", "Voicemail", "Transfer" }.Contains(settings.Queue.QueueTimeoutAction, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Queue timeout behavior must be Abandon, Voicemail, or Transfer.");
        if (settings.Notification.QueueWaitAlertThresholdSeconds is < 1 or > 86400)
            throw new ArgumentException("Queue wait alert threshold must be between 1 and 86400 seconds.");
        if (settings.Security.SessionTimeoutMinutes is < 5 or > 1440 || settings.Security.MaxLoginAttempts is < 1 or > 20)
            throw new ArgumentException("Security timeout or maximum login attempts is outside the permitted range.");
    }
}
