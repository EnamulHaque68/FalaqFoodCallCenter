using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CallCenter.Application.Authentication.DTOs;
using CallCenter.Domain.Security;

namespace CallCenter.Tests;

public sealed class AuthenticationAndAuthorizationTests : IAsyncLifetime
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
    public async Task ValidLogin_Admin_ReturnsTokenAndPermissions()
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto
        {
            UserName = "phase10-admin",
            Password = IntegrationTestFactory.TestPassword
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.AccessToken));
        Assert.Equal(RolePermissionMatrix.RoleAdmin, result.Role);
        Assert.NotEmpty(result.Permissions);
        Assert.Contains(AppPermissions.CustomersDelete, result.Permissions);
        Assert.Contains(AppPermissions.AgentsDelete, result.Permissions);
    }

    [Fact]
    public async Task ValidLogin_Supervisor_ReturnsTokenAndSupervisorPermissions()
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto
        {
            UserName = "phase10-supervisor",
            Password = IntegrationTestFactory.TestPassword
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(result);
        Assert.Equal(RolePermissionMatrix.RoleSupervisor, result.Role);
        Assert.Contains(AppPermissions.AgentsView, result.Permissions);
        Assert.Contains(AppPermissions.ReportsView, result.Permissions);
        Assert.DoesNotContain(AppPermissions.CustomersDelete, result.Permissions);
        Assert.DoesNotContain(AppPermissions.AgentsDelete, result.Permissions);
    }

    [Fact]
    public async Task ValidLogin_Agent_ReturnsTokenAndAgentPermissions()
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto
        {
            UserName = "phase10-agent",
            Password = IntegrationTestFactory.TestPassword
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(result);
        Assert.Equal(RolePermissionMatrix.RoleAgent, result.Role);
        Assert.Contains(AppPermissions.CallsView, result.Permissions);
        Assert.DoesNotContain(AppPermissions.AgentsView, result.Permissions);
        Assert.DoesNotContain(AppPermissions.ReportsView, result.Permissions);
    }

    [Fact]
    public async Task InvalidLogin_WrongPassword_ReturnsUnauthorized()
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto
        {
            UserName = "phase10-admin",
            Password = "WrongPassword999!"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task InvalidLogin_UnknownUser_ReturnsUnauthorized()
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto
        {
            UserName = "non-existent-user",
            Password = IntegrationTestFactory.TestPassword
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnauthenticatedAccess_ReturnsUnauthorized()
    {
        client.DefaultRequestHeaders.Authorization = null;

        var adminTestResponse = await client.GetAsync("/api/v1/auth/admin-test");
        Assert.Equal(HttpStatusCode.Unauthorized, adminTestResponse.StatusCode);

        var customersResponse = await client.GetAsync("/api/v1/customers");
        Assert.Equal(HttpStatusCode.Unauthorized, customersResponse.StatusCode);

        var logoutResponse = await client.PostAsync("/api/v1/auth/logout", null);
        Assert.Equal(HttpStatusCode.Unauthorized, logoutResponse.StatusCode);
    }

    [Fact]
    public async Task ExpiredToken_ReturnsUnauthorized()
    {
        var expiredToken = factory.CreateExpiredToken(factory.AdminUserId, "phase10-admin", "Admin");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", expiredToken);

        var response = await client.GetAsync("/api/v1/customers");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task InvalidToken_Malformed_ReturnsUnauthorized()
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid.jwt.token.string");

        var response = await client.GetAsync("/api/v1/customers");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AdminAccess_SucceedsOnAdminAndSupervisorEndpoints()
    {
        var adminToken = await LoginAsync("phase10-admin", IntegrationTestFactory.TestPassword);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var adminTest = await client.GetAsync("/api/v1/auth/admin-test");
        Assert.Equal(HttpStatusCode.OK, adminTest.StatusCode);

        var permissionTest = await client.GetAsync("/api/v1/auth/permission-test");
        Assert.Equal(HttpStatusCode.OK, permissionTest.StatusCode);

        var agentsList = await client.GetAsync("/api/v1/agents");
        Assert.Equal(HttpStatusCode.OK, agentsList.StatusCode);

        var deleteCustomer = await client.DeleteAsync($"/api/v1/customers/{factory.CustomerId}");
        Assert.True(deleteCustomer.StatusCode == HttpStatusCode.NoContent || deleteCustomer.StatusCode == HttpStatusCode.OK);
    }

    [Fact]
    public async Task SupervisorAccess_AllowedOnSupervisor_ForbiddenOnAdmin()
    {
        var supervisorToken = await LoginAsync("phase10-supervisor", IntegrationTestFactory.TestPassword);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", supervisorToken);

        var supervisorTest = await client.GetAsync("/api/v1/auth/supervisor-test");
        Assert.Equal(HttpStatusCode.OK, supervisorTest.StatusCode);

        var agentsList = await client.GetAsync("/api/v1/agents");
        Assert.Equal(HttpStatusCode.OK, agentsList.StatusCode);

        // Forbidden on Admin endpoint
        var adminTest = await client.GetAsync("/api/v1/auth/admin-test");
        Assert.Equal(HttpStatusCode.Forbidden, adminTest.StatusCode);

        // Forbidden on Admin-only Customer Delete
        var deleteCustomer = await client.DeleteAsync($"/api/v1/customers/{factory.CustomerId}");
        Assert.Equal(HttpStatusCode.Forbidden, deleteCustomer.StatusCode);
    }

    [Fact]
    public async Task AgentAccess_AllowedOnAgent_ForbiddenOnSupervisorAndAdmin()
    {
        var agentToken = await LoginAsync("phase10-agent", IntegrationTestFactory.TestPassword);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", agentToken);

        var agentTest = await client.GetAsync("/api/v1/auth/agent-test");
        Assert.Equal(HttpStatusCode.OK, agentTest.StatusCode);

        var myDashboard = await client.GetAsync("/api/v1/agents/me/dashboard");
        Assert.Equal(HttpStatusCode.OK, myDashboard.StatusCode);

        // Forbidden on all agents list (Supervisor/Admin only)
        var allAgents = await client.GetAsync("/api/v1/agents");
        Assert.Equal(HttpStatusCode.Forbidden, allAgents.StatusCode);

        // Forbidden on supervisor test
        var supervisorTest = await client.GetAsync("/api/v1/auth/supervisor-test");
        Assert.Equal(HttpStatusCode.Forbidden, supervisorTest.StatusCode);

        // Forbidden on admin test
        var adminTest = await client.GetAsync("/api/v1/auth/admin-test");
        Assert.Equal(HttpStatusCode.Forbidden, adminTest.StatusCode);
    }

    [Fact]
    public async Task Logout_WhenAuthenticated_ReturnsOk()
    {
        var token = await LoginAsync("phase10-agent", IntegrationTestFactory.TestPassword);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsync("/api/v1/auth/logout", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetCurrentUser_Me_ReturnsProfileAndPermissions()
    {
        var token = await LoginAsync("phase10-supervisor", IntegrationTestFactory.TestPassword);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var user = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(user);
        Assert.Equal("phase10-supervisor", user.UserName);
        Assert.Equal(RolePermissionMatrix.RoleSupervisor, user.Role);
        Assert.NotEmpty(user.Permissions);
    }
}
