using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CallCenter.Application.Authentication.DTOs;
using CallCenter.Application.Users.DTOs;
using CallCenter.Domain.Entities;
using CallCenter.Domain.Enums;
using CallCenter.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CallCenter.Tests;

public sealed class UserManagementTests : IAsyncLifetime
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
    public async Task Admin_can_list_and_filter_users()
    {
        await AuthenticateAsync("phase10-admin");

        // List all users
        var response = await client.GetAsync("/api/v1/users");
        response.EnsureSuccessStatusCode();

        var users = await response.Content.ReadFromJsonAsync<List<UserListItemDto>>();
        Assert.NotNull(users);
        Assert.True(users.Count >= 3);

        // Filter by search
        var searchResponse = await client.GetAsync("/api/v1/users?search=phase10-admin");
        searchResponse.EnsureSuccessStatusCode();
        var searchUsers = await searchResponse.Content.ReadFromJsonAsync<List<UserListItemDto>>();
        Assert.NotNull(searchUsers);
        Assert.Single(searchUsers);
        Assert.Equal("phase10-admin", searchUsers[0].UserName);

        // Filter by role
        var roleResponse = await client.GetAsync("/api/v1/users?role=Agent");
        roleResponse.EnsureSuccessStatusCode();
        var agentUsers = await roleResponse.Content.ReadFromJsonAsync<List<UserListItemDto>>();
        Assert.NotNull(agentUsers);
        Assert.All(agentUsers, u => Assert.Equal("Agent", u.RoleName));

        // Filter by status
        var activeResponse = await client.GetAsync("/api/v1/users?isActive=true");
        activeResponse.EnsureSuccessStatusCode();
        var activeUsers = await activeResponse.Content.ReadFromJsonAsync<List<UserListItemDto>>();
        Assert.NotNull(activeUsers);
        Assert.All(activeUsers, u => Assert.True(u.IsActive));
    }

    [Fact]
    public async Task Admin_can_get_user_by_id()
    {
        await AuthenticateAsync("phase10-admin");

        var response = await client.GetAsync($"/api/v1/users/{factory.AdminUserId}");
        response.EnsureSuccessStatusCode();

        var user = await response.Content.ReadFromJsonAsync<UserListItemDto>();
        Assert.NotNull(user);
        Assert.Equal(factory.AdminUserId, user.Id);
        Assert.Equal("phase10-admin", user.UserName);
        Assert.Equal("Admin", user.RoleName);
    }

    [Fact]
    public async Task Admin_can_get_roles()
    {
        await AuthenticateAsync("phase10-admin");

        var response = await client.GetAsync("/api/v1/users/roles");
        response.EnsureSuccessStatusCode();

        var roles = await response.Content.ReadFromJsonAsync<List<RoleDto>>();
        Assert.NotNull(roles);
        Assert.Contains(roles, r => r.Name == "Admin");
        Assert.Contains(roles, r => r.Name == "Supervisor");
        Assert.Contains(roles, r => r.Name == "Agent");
    }

    [Fact]
    public async Task Admin_can_create_user_with_role_assignment()
    {
        await AuthenticateAsync("phase10-admin");

        var request = new CreateUserRequestDto
        {
            UserName = "new-supervisor",
            Password = "SecurePassword123!",
            RoleName = "Supervisor"
        };

        var response = await client.PostAsJsonAsync("/api/v1/users", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<UserListItemDto>();
        Assert.NotNull(created);
        Assert.Equal("new-supervisor", created.UserName);
        Assert.Equal("Supervisor", created.RoleName);
        Assert.True(created.IsActive);
        Assert.Null(created.AgentId);

        // Verify database audit log entry was created
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
        var audit = await db.AuditLogs.FirstOrDefaultAsync(x => x.Action == "CreateUser" && x.EntityId == created.Id.ToString());
        Assert.NotNull(audit);
    }

    [Fact]
    public async Task Creating_user_with_role_agent_provisions_linked_agent()
    {
        await AuthenticateAsync("phase10-admin");

        var request = new CreateUserRequestDto
        {
            UserName = "new-agent-user",
            Password = "SecurePassword123!",
            RoleName = "Agent",
            DisplayName = "New Agent Display",
            EmployeeCode = "AGT-NEW99",
            Team = "Tier 1 Support"
        };

        var response = await client.PostAsJsonAsync("/api/v1/users", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<UserListItemDto>();
        Assert.NotNull(created);
        Assert.Equal("new-agent-user", created.UserName);
        Assert.Equal("Agent", created.RoleName);
        Assert.NotNull(created.AgentId);
        Assert.Equal("New Agent Display", created.AgentName);
        Assert.Equal("AGT-NEW99", created.EmployeeCode);
        Assert.Equal("Tier 1 Support", created.Team);
        Assert.Equal(AgentStatus.Offline.ToString(), created.AgentStatus);

        // Verify in database
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
        var agent = await db.Agents.SingleOrDefaultAsync(x => x.UserId == created.Id);
        Assert.NotNull(agent);
        Assert.Equal(created.AgentId, agent.Id);
        Assert.Equal("New Agent Display", agent.DisplayName);
    }

    [Fact]
    public async Task Admin_cannot_create_user_with_duplicate_username()
    {
        await AuthenticateAsync("phase10-admin");

        var request = new CreateUserRequestDto
        {
            UserName = "phase10-admin",
            Password = "SecurePassword123!",
            RoleName = "Supervisor"
        };

        var response = await client.PostAsJsonAsync("/api/v1/users", request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Admin_can_update_user_username_and_role()
    {
        await AuthenticateAsync("phase10-admin");

        var updateRequest = new UpdateUserRequestDto
        {
            UserName = "phase10-supervisor-updated",
            RoleName = "Supervisor",
            IsActive = true
        };

        var response = await client.PutAsJsonAsync($"/api/v1/users/{factory.SupervisorUserId}", updateRequest);
        response.EnsureSuccessStatusCode();

        var updated = await response.Content.ReadFromJsonAsync<UserListItemDto>();
        Assert.NotNull(updated);
        Assert.Equal("phase10-supervisor-updated", updated.UserName);
        Assert.Equal("Supervisor", updated.RoleName);
    }

    [Fact]
    public async Task Admin_can_deactivate_and_reactivate_user()
    {
        await AuthenticateAsync("phase10-admin");

        // Deactivate supervisor
        var deactivateResponse = await client.PutAsync($"/api/v1/users/{factory.SupervisorUserId}/deactivate", null);
        deactivateResponse.EnsureSuccessStatusCode();

        var deactivated = await deactivateResponse.Content.ReadFromJsonAsync<UserListItemDto>();
        Assert.NotNull(deactivated);
        Assert.False(deactivated.IsActive);

        // Reactivate supervisor
        var reactivateResponse = await client.PutAsync($"/api/v1/users/{factory.SupervisorUserId}/reactivate", null);
        reactivateResponse.EnsureSuccessStatusCode();

        var reactivated = await reactivateResponse.Content.ReadFromJsonAsync<UserListItemDto>();
        Assert.NotNull(reactivated);
        Assert.True(reactivated.IsActive);
    }

    [Fact]
    public async Task Deactivating_user_also_deactivates_linked_agent_and_forces_offline()
    {
        await AuthenticateAsync("phase10-admin");

        // First set agent to Available status in DB
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var agent = await db.Agents.SingleAsync(x => x.Id == factory.AgentId);
            agent.Status = AgentStatus.Available;
            agent.IsActive = true;
            await db.SaveChangesAsync();
        }

        // Deactivate agent's user
        var response = await client.PutAsync($"/api/v1/users/{factory.AgentUserId}/deactivate", null);
        response.EnsureSuccessStatusCode();

        var user = await response.Content.ReadFromJsonAsync<UserListItemDto>();
        Assert.NotNull(user);
        Assert.False(user.IsActive);

        // Verify linked agent in DB was deactivated and forced Offline
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var agent = await db.Agents.SingleAsync(x => x.Id == factory.AgentId);
            Assert.False(agent.IsActive);
            Assert.Equal(AgentStatus.Offline, agent.Status);
        }
    }

    [Fact]
    public async Task Admin_cannot_deactivate_self()
    {
        await AuthenticateAsync("phase10-admin");

        var response = await client.PutAsync($"/api/v1/users/{factory.AdminUserId}/deactivate", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Admin_cannot_remove_own_admin_role()
    {
        await AuthenticateAsync("phase10-admin");

        var request = new UpdateUserRequestDto
        {
            UserName = "phase10-admin",
            RoleName = "Supervisor",
            IsActive = true
        };

        var response = await client.PutAsJsonAsync($"/api/v1/users/{factory.AdminUserId}", request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Admin_cannot_deactivate_last_admin()
    {
        await AuthenticateAsync("phase10-admin");

        // Create a second admin first
        var createResponse = await client.PostAsJsonAsync("/api/v1/users", new CreateUserRequestDto
        {
            UserName = "second-admin",
            Password = "SecurePassword123!",
            RoleName = "Admin"
        });
        createResponse.EnsureSuccessStatusCode();
        var secondAdmin = await createResponse.Content.ReadFromJsonAsync<UserListItemDto>();
        Assert.NotNull(secondAdmin);

        // Deactivate the second admin (now phase10-admin is the last active admin)
        var deactSecond = await client.PutAsync($"/api/v1/users/{secondAdmin.Id}/deactivate", null);
        deactSecond.EnsureSuccessStatusCode();

        // Switch login to second admin (even if deactivated, test attempt to deactivate phase10-admin)
        // Trying to deactivate phase10-admin through an update request when it's the last admin
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var user = await db.Users.SingleAsync(x => x.Id == secondAdmin.Id);
            user.IsActive = true;
            await db.SaveChangesAsync();
        }

        // Now login as second-admin and deactivate phase10-admin
        await AuthenticateAsync("second-admin", "SecurePassword123!");
        var deactPhase10 = await client.PutAsync($"/api/v1/users/{factory.AdminUserId}/deactivate", null);
        deactPhase10.EnsureSuccessStatusCode();

        // Now second-admin is the only active admin left. Trying to deactivate second-admin must fail.
        var failDeact = await client.PutAsync($"/api/v1/users/{secondAdmin.Id}/deactivate", null);
        Assert.Equal(HttpStatusCode.BadRequest, failDeact.StatusCode);
    }

    [Fact]
    public async Task Admin_can_reset_user_password()
    {
        await AuthenticateAsync("phase10-admin");

        var resetRequest = new ResetPasswordRequestDto
        {
            NewPassword = "BrandNewPassword123!"
        };

        var response = await client.PostAsJsonAsync($"/api/v1/users/{factory.SupervisorUserId}/reset-password", resetRequest);
        response.EnsureSuccessStatusCode();

        // Verify supervisor can login with the new password
        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto
        {
            UserName = "phase10-supervisor",
            Password = "BrandNewPassword123!"
        });
        loginResponse.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task User_can_change_password_with_valid_current_password()
    {
        await AuthenticateAsync("phase10-supervisor");

        var changeRequest = new ChangePasswordRequestDto
        {
            CurrentPassword = IntegrationTestFactory.TestPassword,
            NewPassword = "ChangedSuperSecret123!"
        };

        var response = await client.PostAsJsonAsync($"/api/v1/users/{factory.SupervisorUserId}/change-password", changeRequest);
        response.EnsureSuccessStatusCode();

        // Verify new password works
        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto
        {
            UserName = "phase10-supervisor",
            Password = "ChangedSuperSecret123!"
        });
        loginResponse.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task User_cannot_change_password_with_invalid_current_password()
    {
        await AuthenticateAsync("phase10-supervisor");

        var changeRequest = new ChangePasswordRequestDto
        {
            CurrentPassword = "WrongPassword!",
            NewPassword = "ChangedSuperSecret123!"
        };

        var response = await client.PostAsJsonAsync($"/api/v1/users/{factory.SupervisorUserId}/change-password", changeRequest);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Non_admin_cannot_access_user_management_endpoints()
    {
        // Agent access
        await AuthenticateAsync("phase10-agent");

        var agentGet = await client.GetAsync("/api/v1/users");
        Assert.Equal(HttpStatusCode.Forbidden, agentGet.StatusCode);

        var agentPost = await client.PostAsJsonAsync("/api/v1/users", new CreateUserRequestDto
        {
            UserName = "unauthorized-user",
            Password = "Password123!",
            RoleName = "Agent"
        });
        Assert.Equal(HttpStatusCode.Forbidden, agentPost.StatusCode);

        // Supervisor access
        await AuthenticateAsync("phase10-supervisor");

        var supervisorGet = await client.GetAsync("/api/v1/users");
        Assert.Equal(HttpStatusCode.Forbidden, supervisorGet.StatusCode);
    }

    [Fact]
    public async Task Deactivated_user_cannot_log_in()
    {
        await AuthenticateAsync("phase10-admin");

        // Deactivate agent
        var deact = await client.PutAsync($"/api/v1/users/{factory.AgentUserId}/deactivate", null);
        deact.EnsureSuccessStatusCode();

        // Clear authorization header
        client.DefaultRequestHeaders.Authorization = null;

        // Attempt login as deactivated agent
        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto
        {
            UserName = "phase10-agent",
            Password = IntegrationTestFactory.TestPassword
        });

        Assert.Equal(HttpStatusCode.Unauthorized, loginResponse.StatusCode);
    }

    [Fact]
    public async Task LastLoginAt_is_updated_on_successful_login()
    {
        // Initial login
        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto
        {
            UserName = "phase10-supervisor",
            Password = IntegrationTestFactory.TestPassword
        });
        loginResponse.EnsureSuccessStatusCode();

        // Check user record
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
        var user = await db.Users.SingleAsync(x => x.Id == factory.SupervisorUserId);
        Assert.NotNull(user.LastLoginAt);
        Assert.True(user.LastLoginAt > DateTime.UtcNow.AddMinutes(-2));
    }

    [Fact]
    public async Task Password_hash_is_never_exposed_in_any_response()
    {
        await AuthenticateAsync("phase10-admin");

        // List endpoint
        var listResponse = await client.GetAsync("/api/v1/users");
        var listJson = await listResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("passwordHash", listJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AQAAAA", listJson); // Typical ASP.NET identity password hash prefix

        // Get single endpoint
        var singleResponse = await client.GetAsync($"/api/v1/users/{factory.AdminUserId}");
        var singleJson = await singleResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("passwordHash", singleJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AQAAAA", singleJson);

        // Create endpoint
        var createResponse = await client.PostAsJsonAsync("/api/v1/users", new CreateUserRequestDto
        {
            UserName = "hash-check-user",
            Password = "SuperPassword123!",
            RoleName = "Supervisor"
        });
        var createJson = await createResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("passwordHash", createJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AQAAAA", createJson);
    }
}
