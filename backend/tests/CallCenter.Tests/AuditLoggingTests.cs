using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CallCenter.Application.Agents.DTOs;
using CallCenter.Application.Audit.DTOs;
using CallCenter.Application.Authentication.DTOs;
using CallCenter.Application.Customers.DTOs;
using CallCenter.Application.Settings.DTOs;
using CallCenter.Application.Telephony.DTOs;
using CallCenter.Application.Users.DTOs;
using CallCenter.Domain.Enums;
using CallCenter.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CallCenter.Tests;

public sealed class AuditLoggingTests : IAsyncLifetime
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

    private async Task AuthenticateAsync(string userName, string password = IntegrationTestFactory.TestPassword)
    {
        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto
        {
            UserName = userName,
            Password = password
        });

        loginResponse.EnsureSuccessStatusCode();
        var body = await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(body?.AccessToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);
    }

    [Fact]
    public async Task Login_and_Logout_are_audited()
    {
        // Login as admin
        await AuthenticateAsync("phase10-admin");

        // Verify Login entry exists
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var loginLog = await db.AuditLogs
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(x => x.Action == "Login" && x.UserId == factory.AdminUserId);

            Assert.NotNull(loginLog);
            Assert.Equal("User", loginLog.EntityName);
            Assert.Equal(factory.AdminUserId.ToString(), loginLog.EntityId);
        }

        // Call Logout
        var logoutResponse = await client.PostAsync("/api/v1/auth/logout", null);
        logoutResponse.EnsureSuccessStatusCode();

        // Verify Logout entry exists
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var logoutLog = await db.AuditLogs
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(x => x.Action == "Logout" && x.UserId == factory.AdminUserId);

            Assert.NotNull(logoutLog);
            Assert.Equal("User", logoutLog.EntityName);
            Assert.Equal(factory.AdminUserId.ToString(), logoutLog.EntityId);
        }
    }

    [Fact]
    public async Task Customer_lifecycle_creates_audit_entries()
    {
        await AuthenticateAsync("phase10-admin");

        var phone = "5550199999";
        var createResponse = await client.PostAsJsonAsync("/api/v1/customers", new CreateCustomerRequestDto
        {
            FullName = "Audit Test Customer",
            Phone = phone,
            Email = "auditcustomer@falaqfood.com"
        });
        createResponse.EnsureSuccessStatusCode();
        var customer = await createResponse.Content.ReadFromJsonAsync<CustomerResponseDto>();
        Assert.NotNull(customer);

        // Verify CustomerCreated
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var createdLog = await db.AuditLogs
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(x => x.Action == "CustomerCreated" && x.EntityId == customer.Id.ToString());

            Assert.NotNull(createdLog);
            Assert.Equal("Customer", createdLog.EntityName);
            Assert.Equal(factory.AdminUserId, createdLog.UserId);
        }

        // Update customer
        var updateResponse = await client.PutAsJsonAsync($"/api/v1/customers/{customer.Id}", new UpdateCustomerRequestDto
        {
            FullName = "Audit Test Customer Updated",
            Phone = phone,
            Email = "auditcustomer_updated@falaqfood.com"
        });
        updateResponse.EnsureSuccessStatusCode();

        // Verify CustomerUpdated
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var updatedLog = await db.AuditLogs
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(x => x.Action == "CustomerUpdated" && x.EntityId == customer.Id.ToString());

            Assert.NotNull(updatedLog);
            Assert.Equal("Customer", updatedLog.EntityName);
            Assert.Equal(factory.AdminUserId, updatedLog.UserId);
        }

        // Delete customer
        var deleteResponse = await client.DeleteAsync($"/api/v1/customers/{customer.Id}");
        deleteResponse.EnsureSuccessStatusCode();

        // Verify CustomerDeleted
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var deletedLog = await db.AuditLogs
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(x => x.Action == "CustomerDeleted" && x.EntityId == customer.Id.ToString());

            Assert.NotNull(deletedLog);
            Assert.Equal("Customer", deletedLog.EntityName);
            Assert.Equal(factory.AdminUserId, deletedLog.UserId);
        }
    }

    [Fact]
    public async Task Agent_lifecycle_and_status_changes_are_audited()
    {
        await AuthenticateAsync("phase10-admin");

        var agentCode = $"AGT-AUD-{Guid.NewGuid().ToString()[..6].ToUpperInvariant()}";
        var agentName = $"Audit Agent {Guid.NewGuid().ToString()[..4]}";

        var createResponse = await client.PostAsJsonAsync("/api/v1/agents", new CreateAgentRequestDto
        {
            UserName = $"agent_{Guid.NewGuid().ToString()[..6]}",
            Password = "Password123!",
            EmployeeCode = agentCode,
            DisplayName = agentName,
            Team = "Audit Team"
        });
        createResponse.EnsureSuccessStatusCode();
        var agent = await createResponse.Content.ReadFromJsonAsync<AgentResponseDto>();
        Assert.NotNull(agent);

        // Verify AgentCreated
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var createdLog = await db.AuditLogs
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(x => x.Action == "AgentCreated" && x.EntityId == agent.Id.ToString());

            Assert.NotNull(createdLog);
            Assert.Equal("Agent", createdLog.EntityName);
            Assert.Equal(factory.AdminUserId, createdLog.UserId);
        }

        // Change Status to Available
        var statusResponse = await client.PutAsJsonAsync($"/api/v1/agents/{agent.Id}/status", new UpdateAgentStatusRequestDto
        {
            Status = AgentStatus.Available
        });
        statusResponse.EnsureSuccessStatusCode();

        // Verify AgentStatusChanged
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var statusLog = await db.AuditLogs
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(x => x.Action == "AgentStatusChanged" && x.EntityId == agent.Id.ToString());

            Assert.NotNull(statusLog);
            Assert.Equal("Agent", statusLog.EntityName);
            Assert.Contains("Available", statusLog.DetailsJson ?? "");
        }

        // Update Agent Details
        var updateResponse = await client.PutAsJsonAsync($"/api/v1/agents/{agent.Id}", new UpdateAgentRequestDto
        {
            EmployeeCode = agentCode,
            DisplayName = agentName + " Updated",
            Team = "Audit Team Tier 2"
        });
        updateResponse.EnsureSuccessStatusCode();

        // Verify AgentUpdated
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var updatedLog = await db.AuditLogs
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(x => x.Action == "AgentUpdated" && x.EntityId == agent.Id.ToString());

            Assert.NotNull(updatedLog);
            Assert.Equal("Agent", updatedLog.EntityName);
        }
    }

    [Fact]
    public async Task Settings_changes_are_audited()
    {
        await AuthenticateAsync("phase10-admin");

        var getResponse = await client.GetAsync("/api/v1/settings");
        getResponse.EnsureSuccessStatusCode();
        var settings = await getResponse.Content.ReadFromJsonAsync<SystemSettingsDto>();
        Assert.NotNull(settings);

        settings.General.CallCenterName = "Falaq Food Audited Call Center " + Guid.NewGuid().ToString()[..4];
        var response = await client.PutAsJsonAsync("/api/v1/settings", settings);
        response.EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
        var settingsLog = await db.AuditLogs
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(x => x.EntityName == "SystemSetting");

        Assert.NotNull(settingsLog);
        Assert.StartsWith("SettingsChanged", settingsLog.Action);
        Assert.Equal(factory.AdminUserId, settingsLog.UserId);
    }

    [Fact]
    public async Task User_changes_are_audited()
    {
        await AuthenticateAsync("phase10-admin");

        var userName = $"audited_user_{Guid.NewGuid().ToString()[..6]}";
        var createResponse = await client.PostAsJsonAsync("/api/v1/users", new CreateUserRequestDto
        {
            UserName = userName,
            Password = "Password123!",
            RoleName = "Supervisor"
        });
        createResponse.EnsureSuccessStatusCode();
        var createdUser = await createResponse.Content.ReadFromJsonAsync<UserListItemDto>();
        Assert.NotNull(createdUser);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
        var userLog = await db.AuditLogs
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(x => x.EntityName == "User" && x.EntityId == createdUser.Id.ToString());

        Assert.NotNull(userLog);
        Assert.Equal("CreateUser", userLog.Action);
        Assert.Equal(factory.AdminUserId, userLog.UserId);
    }

    [Fact]
    public async Task Admin_can_query_audit_logs_with_pagination_and_filters()
    {
        await AuthenticateAsync("phase10-admin");

        // 1. Get paged audit logs
        var response = await client.GetAsync("/api/v1/audit-logs?page=1&pageSize=10");
        response.EnsureSuccessStatusCode();

        var paged = await response.Content.ReadFromJsonAsync<AuditLogPagedResultDto>();
        Assert.NotNull(paged);
        Assert.True(paged.TotalCount > 0);
        Assert.NotEmpty(paged.Items);
        Assert.Equal(1, paged.Page);
        Assert.Equal(10, paged.PageSize);

        // 2. Filter by search
        var searchResponse = await client.GetAsync("/api/v1/audit-logs?search=Login");
        searchResponse.EnsureSuccessStatusCode();
        var searchResult = await searchResponse.Content.ReadFromJsonAsync<AuditLogPagedResultDto>();
        Assert.NotNull(searchResult);
        Assert.NotEmpty(searchResult.Items);

        // 3. Filter by Action
        var actionResponse = await client.GetAsync("/api/v1/audit-logs?action=Login");
        actionResponse.EnsureSuccessStatusCode();
        var actionResult = await actionResponse.Content.ReadFromJsonAsync<AuditLogPagedResultDto>();
        Assert.NotNull(actionResult);
        Assert.All(actionResult.Items, x => Assert.Equal("Login", x.Action));

        // 4. Filter by EntityName
        var entityResponse = await client.GetAsync("/api/v1/audit-logs?entityName=User");
        entityResponse.EnsureSuccessStatusCode();
        var entityResult = await entityResponse.Content.ReadFromJsonAsync<AuditLogPagedResultDto>();
        Assert.NotNull(entityResult);
        Assert.All(entityResult.Items, x => Assert.Equal("User", x.EntityName));

        // 5. Get Distinct Actions
        var actionsResponse = await client.GetAsync("/api/v1/audit-logs/actions");
        actionsResponse.EnsureSuccessStatusCode();
        var actions = await actionsResponse.Content.ReadFromJsonAsync<List<string>>();
        Assert.NotNull(actions);
        Assert.Contains("Login", actions);

        // 6. Get Distinct Entities
        var entitiesResponse = await client.GetAsync("/api/v1/audit-logs/entities");
        entitiesResponse.EnsureSuccessStatusCode();
        var entities = await entitiesResponse.Content.ReadFromJsonAsync<List<string>>();
        Assert.NotNull(entities);
        Assert.Contains("User", entities);

        // 7. Get by ID
        var firstLogId = paged.Items[0].Id;
        var singleResponse = await client.GetAsync($"/api/v1/audit-logs/{firstLogId}");
        singleResponse.EnsureSuccessStatusCode();
        var singleLog = await singleResponse.Content.ReadFromJsonAsync<AuditLogDto>();
        Assert.NotNull(singleLog);
        Assert.Equal(firstLogId, singleLog.Id);
    }

    [Fact]
    public async Task Non_admin_cannot_access_audit_logs()
    {
        // 1. Anonymous request returns 401 Unauthorized
        client.DefaultRequestHeaders.Authorization = null;
        var anonResponse = await client.GetAsync("/api/v1/audit-logs");
        Assert.Equal(HttpStatusCode.Unauthorized, anonResponse.StatusCode);

        // 2. Agent login returns 403 Forbidden
        await AuthenticateAsync("phase10-agent");
        var agentResponse = await client.GetAsync("/api/v1/audit-logs");
        Assert.Equal(HttpStatusCode.Forbidden, agentResponse.StatusCode);

        // 3. Supervisor login returns 403 Forbidden
        await AuthenticateAsync("phase10-supervisor");
        var supResponse = await client.GetAsync("/api/v1/audit-logs");
        Assert.Equal(HttpStatusCode.Forbidden, supResponse.StatusCode);
    }

    [Fact]
    public async Task Audit_logs_never_contain_passwords_or_secrets()
    {
        await AuthenticateAsync("phase10-admin");

        // Create a user with sensitive password
        var secretPassword = "SuperSecretAdminPassword123!#";
        await client.PostAsJsonAsync("/api/v1/users", new CreateUserRequestDto
        {
            UserName = $"secret_user_{Guid.NewGuid().ToString()[..6]}",
            Password = secretPassword,
            RoleName = "Agent"
        });

        // Retrieve all audit logs via API
        var response = await client.GetAsync("/api/v1/audit-logs?pageSize=100");
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AuditLogPagedResultDto>();
        Assert.NotNull(result);

        foreach (var log in result.Items)
        {
            if (log.DetailsJson != null)
            {
                // Assert raw plaintext password is never recorded in any DetailsJson
                Assert.DoesNotContain(secretPassword, log.DetailsJson);
                // Assert password hashes are never leaked in audit logs
                Assert.DoesNotContain("AQAAAAIAAYagAAAAE", log.DetailsJson);
            }
        }
    }
}
