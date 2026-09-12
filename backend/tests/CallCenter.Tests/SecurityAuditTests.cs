using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CallCenter.Application.Authentication.DTOs;
using CallCenter.Application.Routing.DTOs;
using CallCenter.Domain.Entities;
using CallCenter.Domain.Security;
using CallCenter.Infrastructure.Audit;
using CallCenter.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CallCenter.Tests;

public sealed class SecurityAuditTests : IAsyncLifetime
{
    private readonly IntegrationTestFactory factory = new();
    private HttpClient client = null!;

    public async Task InitializeAsync()
    {
        client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

        await factory.SeedAsync();
    }

    public Task DisposeAsync()
    {
        client.Dispose();
        factory.Dispose();
        return Task.CompletedTask;
    }

    private async Task<string> LoginAsync(string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto
        {
            UserName = username,
            Password = password
        });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        return body!.AccessToken;
    }

    [Fact]
    public async Task AccountLockout_AfterFiveFailedAttempts_LocksAccountAndReturns423()
    {
        const string targetUser = "phase10-agent";

        // Attempts 1 through 4 should fail with 401 Unauthorized
        for (var i = 1; i <= 4; i++)
        {
            var failResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto
            {
                UserName = targetUser,
                Password = "WrongPassword123!"
            });

            Assert.Equal(HttpStatusCode.Unauthorized, failResponse.StatusCode);
        }

        // 5th attempt reaches the threshold -> Account becomes locked (HTTP 423)
        var lockedResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto
        {
            UserName = targetUser,
            Password = "WrongPassword123!"
        });

        Assert.Equal((HttpStatusCode)423, lockedResponse.StatusCode);
        var lockedBody = await lockedResponse.Content.ReadAsStringAsync();
        Assert.Contains("locked", lockedBody, StringComparison.OrdinalIgnoreCase);

        // Even with the CORRECT password, locked account is rejected with HTTP 423
        var correctPassResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto
        {
            UserName = targetUser,
            Password = IntegrationTestFactory.TestPassword
        });

        Assert.Equal((HttpStatusCode)423, correctPassResponse.StatusCode);

        // Verify audit trail logged failed attempts and lockout
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
        var user = await db.Users.SingleAsync(u => u.UserName == targetUser);

        Assert.True(user.FailedLoginAttempts >= 5);
        Assert.NotNull(user.LockoutEndUtc);
        Assert.True(user.LockoutEndUtc > DateTime.UtcNow);

        var auditLogs = await db.AuditLogs
            .Where(a => a.UserId == user.Id)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();

        Assert.Contains(auditLogs, a => a.Action == "AccountLocked");
        Assert.Contains(auditLogs, a => a.Action == "LoginFailed");
    }

    [Fact]
    public async Task SuccessfulLogin_ResetsFailureCounter()
    {
        const string targetUser = "phase10-supervisor";

        // 2 failed attempts
        for (var i = 1; i <= 2; i++)
        {
            var failResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto
            {
                UserName = targetUser,
                Password = "WrongPassword123!"
            });
            Assert.Equal(HttpStatusCode.Unauthorized, failResponse.StatusCode);
        }

        // Verify DB recorded 2 attempts
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var user = await db.Users.SingleAsync(u => u.UserName == targetUser);
            Assert.Equal(2, user.FailedLoginAttempts);
        }

        // Successful login
        var successToken = await LoginAsync(targetUser, IntegrationTestFactory.TestPassword);
        Assert.False(string.IsNullOrWhiteSpace(successToken));

        // Verify counter reset to 0
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var user = await db.Users.SingleAsync(u => u.UserName == targetUser);
            Assert.Equal(0, user.FailedLoginAttempts);
            Assert.Null(user.LockoutEndUtc);
        }
    }

    [Fact]
    public async Task SecurityHeaders_AreAppliedToResponses()
    {
        var response = await client.GetAsync("/api/v1/auth/permissions");

        // Verify security response headers
        Assert.True(response.Headers.Contains("X-Content-Type-Options"));
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").First());

        Assert.True(response.Headers.Contains("X-Frame-Options"));
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").First());

        Assert.True(response.Headers.Contains("Referrer-Policy"));
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").First());

        Assert.True(response.Headers.Contains("Content-Security-Policy"));
        var csp = response.Headers.GetValues("Content-Security-Policy").First();
        Assert.Contains("frame-ancestors 'none'", csp);

        Assert.True(response.Headers.Contains("X-XSS-Protection"));
        Assert.Equal("1; mode=block", response.Headers.GetValues("X-XSS-Protection").First());
    }

    [Fact]
    public async Task SecurityHeaders_Swagger_PermitsInlineScriptsAndStyles()
    {
        var response = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.True(response.Headers.Contains("Content-Security-Policy"));
        var csp = response.Headers.GetValues("Content-Security-Policy").First();
        Assert.Contains("script-src 'self' 'unsafe-inline'", csp);
        Assert.Contains("style-src 'self' 'unsafe-inline'", csp);
    }

    [Fact]
    public async Task PermissionsEndpoint_RequiresAuthentication()
    {
        // Unauthenticated request should be 401 Unauthorized
        client.DefaultRequestHeaders.Authorization = null;
        var unauthResponse = await client.GetAsync("/api/v1/auth/permissions");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthResponse.StatusCode);

        // Authenticated request should succeed with 200 OK
        var token = await LoginAsync("phase10-admin", IntegrationTestFactory.TestPassword);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var authResponse = await client.GetAsync("/api/v1/auth/permissions");
        Assert.Equal(HttpStatusCode.OK, authResponse.StatusCode);
    }

    [Fact]
    public async Task PolicyEnforcement_AgentCannotRouteOrCancelQueues()
    {
        // Login as Agent
        var agentToken = await LoginAsync("phase10-agent", IntegrationTestFactory.TestPassword);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", agentToken);

        var dummyCallId = Guid.NewGuid();

        // Agent lacks RoutingManage policy -> 403 Forbidden
        var routeResponse = await client.PostAsync($"/api/v1/routing/calls/{dummyCallId}/route", null);
        Assert.Equal(HttpStatusCode.Forbidden, routeResponse.StatusCode);

        var cancelResponse = await client.PostAsync($"/api/v1/queues/calls/{dummyCallId}/cancel", null);
        Assert.Equal(HttpStatusCode.Forbidden, cancelResponse.StatusCode);

        // Admin has RoutingManage policy -> Route & Cancel are not forbidden (will return 404 since call doesn't exist)
        var adminToken = await LoginAsync("phase10-admin", IntegrationTestFactory.TestPassword);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var adminRouteResponse = await client.PostAsync($"/api/v1/routing/calls/{dummyCallId}/route", null);
        Assert.NotEqual(HttpStatusCode.Forbidden, adminRouteResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, adminRouteResponse.StatusCode);
    }

    [Fact]
    public void SensitiveData_IsRedactedInAuditLogSerialization()
    {
        var sensitivePayload = new
        {
            UserName = "testuser",
            Password = "SuperSecretPassword123!",
            Token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9",
            SecretKey = "SuperTopSecretKey",
            AuthToken = "secret-auth-token",
            CreditCard = "4111-2222-3333-4444",
            Pin = "1234",
            Cvv = "999",
            SafeData = "ThisShouldNotBeRedacted"
        };

        var sanitizedJson = AuditLogService.SanitizeAndSerialize(sensitivePayload);
        Assert.NotNull(sanitizedJson);

        using var doc = JsonDocument.Parse(sanitizedJson);
        var root = doc.RootElement;

        Assert.Equal("testuser", root.GetProperty("UserName").GetString());
        Assert.Equal("ThisShouldNotBeRedacted", root.GetProperty("SafeData").GetString());

        Assert.Equal("[REDACTED]", root.GetProperty("Password").GetString());
        Assert.Equal("[REDACTED]", root.GetProperty("Token").GetString());
        Assert.Equal("[REDACTED]", root.GetProperty("SecretKey").GetString());
        Assert.Equal("[REDACTED]", root.GetProperty("AuthToken").GetString());
        Assert.Equal("[REDACTED]", root.GetProperty("CreditCard").GetString());
        Assert.Equal("[REDACTED]", root.GetProperty("Pin").GetString());
        Assert.Equal("[REDACTED]", root.GetProperty("Cvv").GetString());
    }

    [Fact]
    public void ProductionValidation_RejectsDefaultDevSecretKey()
    {
        var services = new ServiceCollection();
        var inMemorySettings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=dummy;Database=dummy;",
            ["Jwt:Issuer"] = "CallCenter",
            ["Jwt:Audience"] = "CallCenter.Client",
            ["Jwt:SecretKey"] = "FalaqFoodCallCenterSecureJwtKeyChangeMeInProduction_AtLeast32CharsLong!",
            ["ASPNETCORE_ENVIRONMENT"] = "Production"
        };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            CallCenter.Infrastructure.DependencyInjection.AddInfrastructure(services, config);
        });

        Assert.Contains("Default development JWT SecretKey cannot be used", exception.Message);
    }

    [Fact]
    public async Task RateLimiter_ThrottlesLoginRequests_WhenIpLimitExceeded()
    {
        // 10 requests are permitted in the 1-minute window
        for (var i = 1; i <= 10; i++)
        {
            await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto
            {
                UserName = $"unknown-user-{i}",
                Password = "RandomPassword123!"
            });
        }

        // The 11th request exceeds the rate limit for this client IP -> 429 Too Many Requests
        var throttledResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto
        {
            UserName = "unknown-user-11",
            Password = "RandomPassword123!"
        });

        Assert.Equal((HttpStatusCode)429, throttledResponse.StatusCode);
    }
}
