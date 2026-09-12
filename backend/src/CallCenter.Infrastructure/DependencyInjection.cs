using CallCenter.Application.Agents;
using CallCenter.Application.Audit;
using CallCenter.Application.Authentication;
using CallCenter.Application.Calls;
using CallCenter.Application.CRM;
using CallCenter.Application.Customers;
using CallCenter.Application.Dispositions;
using CallCenter.Application.RealTime;
using CallCenter.Application.Queues;
using CallCenter.Application.Recordings;
using CallCenter.Application.Reports;
using CallCenter.Application.Routing;
using CallCenter.Application.Settings;
using CallCenter.Application.Telephony;
using CallCenter.Application.Telephony.Providers;
using CallCenter.Application.Users;

using CallCenter.Infrastructure.Agents;
using CallCenter.Infrastructure.Audit;
using CallCenter.Infrastructure.Authentication;
using CallCenter.Infrastructure.Calls;
using CallCenter.Infrastructure.CRM;
using CallCenter.Infrastructure.Customers;
using CallCenter.Infrastructure.Dispositions;
using CallCenter.Infrastructure.Persistence;
using CallCenter.Infrastructure.Queues;
using CallCenter.Infrastructure.RealTime;
using CallCenter.Infrastructure.Recordings;
using CallCenter.Infrastructure.Recordings.Security;
using CallCenter.Infrastructure.Recordings.Storage;
using CallCenter.Infrastructure.Reports;
using CallCenter.Infrastructure.Routing;
using CallCenter.Infrastructure.Settings;
using CallCenter.Infrastructure.Telephony;
using CallCenter.Infrastructure.Telephony.Providers;
using CallCenter.Infrastructure.Users;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CallCenter.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Connection string 'DefaultConnection' was not found.");

        services.AddDbContext<CallCenterDbContext>(options =>
            options.UseSqlServer(connectionString));

        services.Configure<JwtOptions>(
            configuration.GetSection(JwtOptions.SectionName));

        services.AddPasswordHashing();
        services.AddScoped<JwtTokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IAuditLogService, AuditLogService>();

        services.AddScoped<IAgentService, AgentService>();

        services.AddScoped<ICrmService, MockCrmService>();
        services.AddScoped<ICustomerService, CustomerService>();

        // ✅ Call Service
        services.AddScoped<ICallService, CallService>();
        services.AddScoped<IDispositionService, DispositionService>();

        services.AddScoped<IRoutingStrategyFactory, RoutingStrategyFactory>();
        services.AddScoped<IRoutingService, RoutingService>();
        services.AddScoped<IQueueService, CallCenter.Infrastructure.Queues.QueueService>();

        // ✅ Telephony Abstraction Layer
        services.Configure<TelephonyOptions>(
            configuration.GetSection(TelephonyOptions.SectionName));
        services.AddSingleton<SimulatedTelephonyProvider>();
        services.AddHttpClient<RealTelephonyProvider>();
        services.AddHttpClient<TwilioTelephonyProvider>();
        services.AddScoped<RealTelephonyProvider>();
        services.AddScoped<TwilioTelephonyProvider>();
        services.AddScoped<ITelephonyProviderFactory>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<TelephonyOptions>>();
            var sim = sp.GetRequiredService<SimulatedTelephonyProvider>();
            var real = sp.GetRequiredService<RealTelephonyProvider>();
            var twilio = sp.GetRequiredService<TwilioTelephonyProvider>();
            return new TelephonyProviderFactory([sim, real, twilio], options);
        });
        services.AddScoped<ITelephonyProvider>(sp =>
            sp.GetRequiredService<ITelephonyProviderFactory>().GetActiveProvider());
        services.AddScoped<ITelephonyService, SimulatedTelephonyService>();

        // ✅ Call Recording Subsystem
        services.Configure<RecordingStorageOptions>(
            configuration.GetSection(RecordingStorageOptions.SectionName));
        services.AddSingleton<IRecordingStorage, LocalDiskRecordingStorage>();
        services.AddSingleton<RecordingPlaybackTokenGenerator>();
        services.AddScoped<IRecordingService, RecordingService>();
        services.AddScoped<IRecordingRetentionService, RecordingRetentionService>();
        services.AddHostedService<RecordingRetentionWorker>();

        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<ISettingsService, SettingsService>();
        services.AddScoped<IUserService, UserService>();

        services.AddScoped<IRealTimeNotifier, SignalRRealTimeNotifier>();
        services.AddScoped<IAgentConnectionResolver, AgentConnectionResolver>();

        ValidateJwtOptions(configuration);

        return services;
    }

    private static void ValidateJwtOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection(JwtOptions.SectionName);
        var secret = section["SecretKey"];

        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
        {
            throw new InvalidOperationException(
                "JWT configuration is missing or SecretKey is shorter than 32 characters.");
        }

        var environment = configuration["ASPNETCORE_ENVIRONMENT"] ?? configuration["Environment"];
        if (!string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(environment, "Testing", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(environment))
        {
            if (secret.Contains("FalaqFoodCallCenterSecureJwtKeyChangeMeInProduction", StringComparison.OrdinalIgnoreCase) ||
                secret.Contains("ChangeMeInProduction", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Default development JWT SecretKey cannot be used in production or staging environments.");
            }
        }

        if (string.IsNullOrWhiteSpace(section["Issuer"]))
        {
            throw new InvalidOperationException(
                "JWT Issuer is required.");
        }

        if (string.IsNullOrWhiteSpace(section["Audience"]))
        {
            throw new InvalidOperationException(
                "JWT Audience is required.");
        }
    }
}
