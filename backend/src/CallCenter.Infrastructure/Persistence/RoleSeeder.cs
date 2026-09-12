using CallCenter.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Infrastructure.Persistence;

public static class RoleSeeder
{
    // NOTE: this default admin account exists only so a freshly migrated
    // database is actually usable (there is no self-registration endpoint).
    // Change this password immediately in any shared/non-local environment.
    private const string DefaultAdminUserName = "admin";
    private const string DefaultAdminPassword = "ChangeMe123!";

    public static async Task SeedAsync(
        CallCenterDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        var roleNames = new[]
        {
            ("Admin", "System administrator"),
            ("Supervisor", "Call center supervisor"),
            ("Agent", "Call center agent")
        };

        var existingNames = await dbContext.Roles
            .Select(x => x.Name)
            .ToListAsync(cancellationToken);

        var missingRoles = roleNames
            .Where(x => !existingNames.Contains(x.Item1))
            .Select(x => new Role
            {
                Id = Guid.NewGuid(),
                Name = x.Item1,
                Description = x.Item2,
                CreatedAt = DateTime.UtcNow
            })
            .ToList();

        if (missingRoles.Count > 0)
        {
            dbContext.Roles.AddRange(missingRoles);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Seeds a default admin login on a brand-new database. Callers (e.g.
    /// integration test factories) that seed their own fixed users should
    /// skip this to keep their datasets deterministic.
    /// </summary>
    public static async Task SeedDefaultAdminAsync(
        CallCenterDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        var hasAnyUser = await dbContext.Users.AnyAsync(cancellationToken);

        if (hasAnyUser)
        {
            // Backfill: Ensure existing Admin / Supervisor accounts have an Agent entity for softphone operations
            var adminUsersWithoutAgent = await dbContext.Users
                .Include(u => u.Role)
                .Where(u => (u.Role!.Name == "Admin" || u.Role!.Name == "Supervisor") &&
                            !dbContext.Agents.Any(a => a.UserId == u.Id))
                .ToListAsync(cancellationToken);

            foreach (var user in adminUsersWithoutAgent)
            {
                var prefix = user.Role?.Name == "Admin" ? "ADM" : "SUP";
                var shortId = user.Id.ToString("N")[..6].ToUpperInvariant();
                var code = $"{prefix}-{shortId}";

                if (await dbContext.Agents.AnyAsync(a => a.EmployeeCode == code, cancellationToken))
                {
                    code = $"{prefix}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
                }

                dbContext.Agents.Add(new Agent
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    EmployeeCode = code,
                    DisplayName = user.UserName ?? (user.Role?.Name == "Admin" ? "Administrator" : "Supervisor"),
                    Team = "Management",
                    Status = Domain.Enums.AgentStatus.Available,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });
            }

            if (adminUsersWithoutAgent.Count > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return;
        }

        var adminRole = await dbContext.Roles
            .SingleAsync(x => x.Name == "Admin", cancellationToken);

        var admin = new User
        {
            Id = Guid.NewGuid(),
            RoleId = adminRole.Id,
            UserName = DefaultAdminUserName,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var hasher = new PasswordHasher<User>();
        admin.PasswordHash = hasher.HashPassword(admin, DefaultAdminPassword);

        dbContext.Users.Add(admin);

        var adminAgent = new Agent
        {
            Id = Guid.NewGuid(),
            UserId = admin.Id,
            EmployeeCode = "ADM-001",
            DisplayName = "Administrator",
            Team = "Management",
            Status = Domain.Enums.AgentStatus.Available,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        dbContext.Agents.Add(adminAgent);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Seeds essential operational reference data (dispositions, default queue,
    /// system settings) for non-test environments.
    /// </summary>
    public static async Task SeedReferenceDataAsync(
        CallCenterDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        // 1. Seed Call Dispositions
        var dispositions = new[]
        {
            ("ORDER_COMPLETED", "Order Completed", "Food order successfully taken and confirmed.", false, false, 1),
            ("CUSTOMER_INTERESTED", "Customer Interested", "Customer expressed interest in food items, offers, or catering.", false, false, 2),
            ("FOLLOW_UP_REQUIRED", "Follow Up Required", "Customer interaction mandates scheduled follow-up or callback.", true, false, 3),
            ("INFO_PROVIDED", "Information Provided", "Customer inquired and received requested information.", false, false, 4),
            ("COMPLAINT", "Complaint", "Customer registered an issue or dissatisfaction requiring notes.", false, true, 5),
            ("NO_RESOLUTION", "No Resolution", "Issue or inquiry could not be resolved during this call.", false, true, 6),
            ("WRONG_NUMBER", "Wrong Number", "Caller dialed wrong number or misdialed.", false, false, 7),
            ("OTHER", "Other", "Miscellaneous business outcome requiring detailed notes.", false, true, 8)
        };

        var existingCodes = await dbContext.CallDispositions
            .Select(x => x.Code)
            .ToListAsync(cancellationToken);

        var missingDispositions = dispositions
            .Where(x => !existingCodes.Contains(x.Item1))
            .Select(x => new CallDisposition
            {
                Id = Guid.NewGuid(),
                Code = x.Item1,
                Name = x.Item2,
                Description = x.Item3,
                RequiresFollowUp = x.Item4,
                RequiresNotes = x.Item5,
                SortOrder = x.Item6,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            })
            .ToList();

        if (missingDispositions.Count > 0)
        {
            dbContext.CallDispositions.AddRange(missingDispositions);
        }

        // 2. Seed Default Queue
        var hasQueue = await dbContext.CallQueues.AnyAsync(cancellationToken);
        if (!hasQueue)
        {
            dbContext.CallQueues.Add(new CallQueue
            {
                Id = Guid.NewGuid(),
                Name = "General Inbound",
                Priority = 1,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
        }

        // 3. Seed Default System Settings
        var settings = new (string Key, string Value, string Category, string DataType, string Description)[]
        {
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
        };

        var existingKeys = await dbContext.SystemSettings
            .Select(x => x.Key)
            .ToListAsync(cancellationToken);

        var missingSettings = settings
            .Where(x => !existingKeys.Contains(x.Key))
            .Select(x => new SystemSetting
            {
                Id = Guid.NewGuid(),
                Key = x.Key,
                Value = x.Value,
                Category = x.Category,
                DataType = x.DataType,
                Description = x.Description,
                UpdatedAt = DateTime.UtcNow
            })
            .ToList();

        if (missingSettings.Count > 0)
        {
            dbContext.SystemSettings.AddRange(missingSettings);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
