namespace CallCenter.Domain.Security;

public static class AppPermissions
{
    // Calls
    public const string CallsView = "Permissions.Calls.View";
    public const string CallsManage = "Permissions.Calls.Manage";

    // Customers
    public const string CustomersView = "Permissions.Customers.View";
    public const string CustomersCreate = "Permissions.Customers.Create";
    public const string CustomersEdit = "Permissions.Customers.Edit";
    public const string CustomersDelete = "Permissions.Customers.Delete";

    // Agents
    public const string AgentsView = "Permissions.Agents.View";
    public const string AgentsManage = "Permissions.Agents.Manage";
    public const string AgentsDelete = "Permissions.Agents.Delete";
    public const string AgentsStatus = "Permissions.Agents.Status";

    // Routing
    public const string RoutingView = "Permissions.Routing.View";
    public const string RoutingManage = "Permissions.Routing.Manage";

    // Reports
    public const string ReportsView = "Permissions.Reports.View";

    // Dispositions
    public const string DispositionsView = "Permissions.Dispositions.View";
    public const string DispositionsManage = "Permissions.Dispositions.Manage";

    // Settings
    public const string SettingsView = "Permissions.Settings.View";
    public const string SettingsManage = "Permissions.Settings.Manage";

    // Users
    public const string UsersView = "Permissions.Users.View";
    public const string UsersManage = "Permissions.Users.Manage";

    // Audit Logs
    public const string AuditLogsView = "Permissions.AuditLogs.View";

    // Recordings
    public const string RecordingsView = "Permissions.Recordings.View";
    public const string RecordingsListen = "Permissions.Recordings.Listen";
    public const string RecordingsDownload = "Permissions.Recordings.Download";
    public const string RecordingsDelete = "Permissions.Recordings.Delete";

    public static readonly IReadOnlyList<string> All =
    [
        CallsView,
        CallsManage,
        CustomersView,
        CustomersCreate,
        CustomersEdit,
        CustomersDelete,
        AgentsView,
        AgentsManage,
        AgentsDelete,
        AgentsStatus,
        RoutingView,
        RoutingManage,
        ReportsView,
        DispositionsView,
        DispositionsManage,
        SettingsView,
        SettingsManage,
        UsersView,
        UsersManage,
        AuditLogsView,
        RecordingsView,
        RecordingsListen,
        RecordingsDownload,
        RecordingsDelete
    ];
}

public static class RolePermissionMatrix
{
    public const string RoleAdmin = "Admin";
    public const string RoleSupervisor = "Supervisor";
    public const string RoleAgent = "Agent";

    private static readonly Dictionary<string, IReadOnlyList<string>> Matrix =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [RoleAdmin] = AppPermissions.All,

            [RoleSupervisor] =
            [
                AppPermissions.CallsView,
                AppPermissions.CallsManage,
                AppPermissions.CustomersView,
                AppPermissions.CustomersCreate,
                AppPermissions.CustomersEdit,
                AppPermissions.AgentsView,
                AppPermissions.AgentsManage,
                AppPermissions.AgentsStatus,
                AppPermissions.RoutingView,
                AppPermissions.RoutingManage,
                AppPermissions.ReportsView,
                AppPermissions.DispositionsView,
                AppPermissions.DispositionsManage,
                AppPermissions.SettingsView,
                AppPermissions.RecordingsView,
                AppPermissions.RecordingsListen,
                AppPermissions.RecordingsDownload
            ],

            [RoleAgent] =
            [
                AppPermissions.CallsView,
                AppPermissions.CallsManage,
                AppPermissions.CustomersView,
                AppPermissions.AgentsStatus,
                AppPermissions.RoutingView,
                AppPermissions.DispositionsView,
                AppPermissions.RecordingsView,
                AppPermissions.RecordingsListen
            ]
        };

    public static IReadOnlyList<string> GetPermissionsForRole(string? roleName)
    {
        if (string.IsNullOrWhiteSpace(roleName))
            return [];

        return Matrix.TryGetValue(roleName, out var permissions)
            ? permissions
            : [];
    }

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> GetAll() => Matrix;
}
