using CallCenter.Infrastructure.RealTime;
using CallCenter.Api.Middleware;
using CallCenter.Api.Health;
using CallCenter.Infrastructure;
using CallCenter.Infrastructure.Authentication;
using CallCenter.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
using System.Threading.RateLimiting;

using System.IO.Compression;
using Microsoft.AspNetCore.ResponseCompression;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
    {
        "application/json",
        "text/json",
        "application/problem+json"
    });
});

builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
});

builder.Services.Configure<GzipCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
});

builder.Services.AddControllers();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("LoginLimiter", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown_client",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

builder.Services.AddHsts(options =>
{
    options.Preload = true;
    options.IncludeSubDomains = true;
    options.MaxAge = TimeSpan.FromDays(365);
});

var corsOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy.WithOrigins(corsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddScoped<AgentGroupHubFilter>();
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: new[] { "ready" });

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((options, jwtOpt) =>
    {
        var config = jwtOpt.Value;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = config.Issuer,
            ValidAudience = config.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(config.SecretKey)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) &&
                    path.StartsWithSegments("/hubs/call-center"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    foreach (var permission in CallCenter.Domain.Security.AppPermissions.All)
    {
        options.AddPolicy(permission, policy =>
            policy.RequireAssertion(ctx =>
                ctx.User.IsInRole(CallCenter.Domain.Security.RolePermissionMatrix.RoleAdmin) ||
                ctx.User.HasClaim(c => c.Type == "permission" && c.Value == permission)));
    }
});

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Falaq Food Call Center API",
        Version = "v1"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter: Bearer {your JWT token}"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

app.UseResponseCompression();
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseSecurityHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// The Angular app (and its documented run instructions) talk to the API
// over plain http://localhost:5289 in local development. Forcing an https
// redirect here would break that origin (and SignalR's websocket upgrade)
// against the local dev certificate, so it's only enabled outside Development.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseCors("Frontend");
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready");

app.MapControllers();
app.MapHub<CallCenterHub>(CallCenterHub.HubRoute);

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
    if (dbContext.Database.IsRelational())
    {
        try
        {
            var pending = (await dbContext.Database.GetPendingMigrationsAsync()).ToList();
            if (pending.Contains("20260908085747_InitialCreate"))
            {
                var conn = dbContext.Database.GetDbConnection();
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'CallDispositions'";
                var tableExists = (await cmd.ExecuteScalarAsync()) is not null;

                if (tableExists)
                {
                    await dbContext.Database.ExecuteSqlRawAsync(
                        """
                        IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '__EFMigrationsHistory')
                        BEGIN
                            CREATE TABLE [__EFMigrationsHistory] (
                                [MigrationId] nvarchar(150) NOT NULL,
                                [ProductVersion] nvarchar(32) NOT NULL,
                                CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
                            );
                        END

                        IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = '20260908085747_InitialCreate')
                        BEGIN
                            INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
                            VALUES ('20260908085747_InitialCreate', '8.0.30');
                        END

                        IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = '20260909113937_AddReportingAnalyticsIndexes')
                        BEGIN
                            INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
                            VALUES ('20260909113937_AddReportingAnalyticsIndexes', '8.0.30');
                        END
                        """);
                }
            }

            await dbContext.Database.MigrateAsync();
        }
        catch
        {
            await dbContext.Database.EnsureCreatedAsync();
        }
    }
    else
    {
        await dbContext.Database.EnsureCreatedAsync();
    }
    await RoleSeeder.SeedAsync(dbContext);

    // Integration tests seed their own fixed set of users; skip the
    // default admin and reference data there so test data stays deterministic.
    if (!app.Environment.IsEnvironment("Testing"))
    {
        await RoleSeeder.SeedDefaultAdminAsync(dbContext);
        await RoleSeeder.SeedReferenceDataAsync(dbContext);
    }
}

app.Run();

public partial class Program { }
