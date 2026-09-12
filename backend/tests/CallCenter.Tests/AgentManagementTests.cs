using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CallCenter.Application.Agents.DTOs;
using CallCenter.Application.Authentication.DTOs;
using CallCenter.Application.Calls.DTOs;
using CallCenter.Application.Routing.DTOs;
using CallCenter.Domain.Entities;
using CallCenter.Domain.Enums;
using CallCenter.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace CallCenter.Tests;

public sealed class AgentManagementTests : IAsyncLifetime
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

    private async Task AuthenticateAsync(string userName, string password)
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
    public async Task Admin_can_list_and_filter_agents()
    {
        await AuthenticateAsync("phase10-admin", IntegrationTestFactory.TestPassword);

        // Add additional test agent
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var agentRole = db.Roles.Single(x => x.Name == "Agent");
            var user = new User
            {
                Id = Guid.NewGuid(),
                RoleId = agentRole.Id,
                UserName = "sales-agent-1",
                PasswordHash = "hash",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            db.Users.Add(user);
            db.Agents.Add(new Agent
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                EmployeeCode = "SALES-01",
                DisplayName = "Sales Rep One",
                Team = "Sales",
                Status = AgentStatus.Available,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // 1. Get all agents
        var allResponse = await client.GetAsync("/api/v1/agents");
        Assert.Equal(HttpStatusCode.OK, allResponse.StatusCode);
        var allAgents = await allResponse.Content.ReadFromJsonAsync<List<AgentResponseDto>>();
        Assert.NotNull(allAgents);
        Assert.True(allAgents.Count >= 2);

        // 2. Filter by search term
        var searchResponse = await client.GetAsync("/api/v1/agents?search=SALES-01");
        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);
        var searchAgents = await searchResponse.Content.ReadFromJsonAsync<List<AgentResponseDto>>();
        Assert.NotNull(searchAgents);
        Assert.Single(searchAgents);
        Assert.Equal("SALES-01", searchAgents[0].EmployeeCode);

        // 3. Filter by team
        var teamResponse = await client.GetAsync("/api/v1/agents?team=Sales");
        Assert.Equal(HttpStatusCode.OK, teamResponse.StatusCode);
        var teamAgents = await teamResponse.Content.ReadFromJsonAsync<List<AgentResponseDto>>();
        Assert.NotNull(teamAgents);
        Assert.Contains(teamAgents, a => a.EmployeeCode == "SALES-01");

        // 4. Filter by status
        var statusResponse = await client.GetAsync("/api/v1/agents?status=Available");
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        var statusAgents = await statusResponse.Content.ReadFromJsonAsync<List<AgentResponseDto>>();
        Assert.NotNull(statusAgents);
        Assert.All(statusAgents, a => Assert.Equal(AgentStatus.Available, a.Status));
    }

    [Fact]
    public async Task Admin_can_create_agent_with_new_user_credentials()
    {
        await AuthenticateAsync("phase10-admin", IntegrationTestFactory.TestPassword);

        var createReq = new CreateAgentRequestDto
        {
            EmployeeCode = "CS-101",
            DisplayName = "Customer Support Agent 1",
            Team = "Customer Support",
            UserName = "cs.agent1",
            Password = "Password123!",
            RoleName = "Agent"
        };

        var response = await client.PostAsJsonAsync("/api/v1/agents", createReq);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var createdAgent = await response.Content.ReadFromJsonAsync<AgentResponseDto>();
        Assert.NotNull(createdAgent);
        Assert.Equal("CS-101", createdAgent.EmployeeCode);
        Assert.Equal("Customer Support Agent 1", createdAgent.DisplayName);
        Assert.Equal("Customer Support", createdAgent.Team);
        Assert.Equal("cs.agent1", createdAgent.UserName);
        Assert.Equal("Agent", createdAgent.RoleName);
        Assert.True(createdAgent.IsActive);
        Assert.Equal(AgentStatus.Offline, createdAgent.Status);

        // Verify the newly created agent can log in
        client.DefaultRequestHeaders.Authorization = null;
        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto
        {
            UserName = "cs.agent1",
            Password = "Password123!"
        });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
    }

    [Fact]
    public async Task Admin_can_update_agent_profile_and_team()
    {
        await AuthenticateAsync("phase10-admin", IntegrationTestFactory.TestPassword);

        var updateReq = new UpdateAgentRequestDto
        {
            EmployeeCode = "P10AG-UPDATED",
            DisplayName = "Phase 10 Agent Updated",
            Team = "VIP Support",
            IsActive = true
        };

        var response = await client.PutAsJsonAsync($"/api/v1/agents/{factory.AgentId}", updateReq);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<AgentResponseDto>();
        Assert.NotNull(updated);
        Assert.Equal("P10AG-UPDATED", updated.EmployeeCode);
        Assert.Equal("Phase 10 Agent Updated", updated.DisplayName);
        Assert.Equal("VIP Support", updated.Team);
    }

    [Fact]
    public async Task Admin_can_deactivate_and_reactivate_agent()
    {
        await AuthenticateAsync("phase10-admin", IntegrationTestFactory.TestPassword);

        // First set agent to Available
        await client.PutAsJsonAsync($"/api/v1/agents/{factory.AgentId}/status", new UpdateAgentStatusRequestDto
        {
            Status = AgentStatus.Available
        });

        // Deactivate agent
        var deactivateResp = await client.PutAsync($"/api/v1/agents/{factory.AgentId}/deactivate", null);
        Assert.Equal(HttpStatusCode.OK, deactivateResp.StatusCode);
        var deactivated = await deactivateResp.Content.ReadFromJsonAsync<AgentResponseDto>();
        Assert.NotNull(deactivated);
        Assert.False(deactivated.IsActive);
        Assert.Equal(AgentStatus.Offline, deactivated.Status);

        // Reactivate agent
        var reactivateResp = await client.PutAsync($"/api/v1/agents/{factory.AgentId}/reactivate", null);
        Assert.Equal(HttpStatusCode.OK, reactivateResp.StatusCode);
        var reactivated = await reactivateResp.Content.ReadFromJsonAsync<AgentResponseDto>();
        Assert.NotNull(reactivated);
        Assert.True(reactivated.IsActive);
    }

    [Fact]
    public async Task Deactivated_agent_cannot_be_set_to_available_or_away()
    {
        await AuthenticateAsync("phase10-admin", IntegrationTestFactory.TestPassword);

        // Deactivate agent
        await client.PutAsync($"/api/v1/agents/{factory.AgentId}/deactivate", null);

        // Try setting status to Available
        var response = await client.PutAsJsonAsync($"/api/v1/agents/{factory.AgentId}/status", new UpdateAgentStatusRequestDto
        {
            Status = AgentStatus.Available
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        // Try setting status to Away
        var awayResponse = await client.PutAsJsonAsync($"/api/v1/agents/{factory.AgentId}/status", new UpdateAgentStatusRequestDto
        {
            Status = AgentStatus.Away
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Routing_service_does_not_route_calls_to_deactivated_agents()
    {
        await AuthenticateAsync("phase10-admin", IntegrationTestFactory.TestPassword);

        // Create an incoming call
        var callResp = await client.PostAsJsonAsync("/api/v1/calls/incoming", new CreateIncomingCallRequestDto
        {
            CustomerId = factory.CustomerId,
            PhoneNumber = "8801712345678",
            CorrelationId = $"ROUTING-TEST-{Guid.NewGuid():N}"
        });
        callResp.EnsureSuccessStatusCode();
        var call = await callResp.Content.ReadFromJsonAsync<CallResponseDto>();
        Assert.NotNull(call);

        // Agent is currently Offline and Deactivated
        await client.PutAsync($"/api/v1/agents/{factory.AgentId}/deactivate", null);

        // Attempt assignment to deactivated agent
        var assignResp = await client.HttpPostAsync($"/api/v1/routing/calls/{call.Id}/assign", new AssignCallRequestDto
        {
            AgentId = factory.AgentId
        });

        // Should return Conflict or Bad Request because agent is not active Available
        Assert.True(assignResp.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Admin_can_delete_agent_without_calls()
    {
        await AuthenticateAsync("phase10-admin", IntegrationTestFactory.TestPassword);

        // Create an agent with no calls
        var createResp = await client.PostAsJsonAsync("/api/v1/agents", new CreateAgentRequestDto
        {
            EmployeeCode = "DEL-001",
            DisplayName = "Delete Me Agent",
            UserName = "del.agent",
            Password = "Password123!"
        });
        createResp.EnsureSuccessStatusCode();
        var agent = await createResp.Content.ReadFromJsonAsync<AgentResponseDto>();
        Assert.NotNull(agent);

        // Delete agent
        var deleteResp = await client.DeleteAsync($"/api/v1/agents/{agent.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        // Verify gone
        var getResp = await client.GetAsync($"/api/v1/agents/{agent.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResp.StatusCode);
    }

    [Fact]
    public async Task Deleting_agent_with_call_history_returns_409_conflict()
    {
        await AuthenticateAsync("phase10-admin", IntegrationTestFactory.TestPassword);

        // Attach a call to factory.AgentId
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            db.Calls.Add(new Call
            {
                Id = Guid.NewGuid(),
                CustomerId = factory.CustomerId,
                AssignedAgentId = factory.AgentId,
                PhoneNumber = "8801712345678",
                CorrelationId = $"CALL-REF-{Guid.NewGuid():N}",
                Direction = CallDirection.Inbound,
                Status = CallStatus.Completed,
                StartedAt = DateTime.UtcNow.AddMinutes(-5),
                EndedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // Attempt delete
        var response = await client.DeleteAsync($"/api/v1/agents/{factory.AgentId}");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Duplicate_employee_code_returns_409_conflict()
    {
        await AuthenticateAsync("phase10-admin", IntegrationTestFactory.TestPassword);

        // Attempt creating agent with already existing employee code "P10AG"
        var createReq = new CreateAgentRequestDto
        {
            EmployeeCode = "P10AG",
            DisplayName = "Duplicate Code Agent",
            UserName = "duplicate.user",
            Password = "Password123!"
        };

        var response = await client.PostAsJsonAsync("/api/v1/agents", createReq);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Supervisor_can_create_and_update_agent()
    {
        await AuthenticateAsync("phase10-supervisor", IntegrationTestFactory.TestPassword);

        // Create
        var createResp = await client.PostAsJsonAsync("/api/v1/agents", new CreateAgentRequestDto
        {
            EmployeeCode = "SUP-CREATE-1",
            DisplayName = "Supervisor Created Agent",
            UserName = "sup.created1",
            Password = "Password123!",
            Team = "Escalations"
        });
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var created = await createResp.Content.ReadFromJsonAsync<AgentResponseDto>();
        Assert.NotNull(created);

        // Update
        var updateResp = await client.PutAsJsonAsync($"/api/v1/agents/{created.Id}", new UpdateAgentRequestDto
        {
            EmployeeCode = "SUP-CREATE-1",
            DisplayName = "Supervisor Created Agent Renamed",
            Team = "Tier 2 Support"
        });
        Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);
    }

    [Fact]
    public async Task Supervisor_cannot_delete_agent_returns_403_forbidden()
    {
        await AuthenticateAsync("phase10-supervisor", IntegrationTestFactory.TestPassword);

        var response = await client.DeleteAsync($"/api/v1/agents/{factory.AgentId}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Agent_cannot_list_or_manage_agents_returns_403_forbidden()
    {
        await AuthenticateAsync("phase10-agent", IntegrationTestFactory.TestPassword);

        // Agent cannot list all agents
        var listResp = await client.GetAsync("/api/v1/agents");
        Assert.Equal(HttpStatusCode.Forbidden, listResp.StatusCode);

        // Agent cannot create agent
        var createResp = await client.PostAsJsonAsync("/api/v1/agents", new CreateAgentRequestDto
        {
            EmployeeCode = "AGT-HACK",
            DisplayName = "Hacked Agent",
            UserName = "hacked.user",
            Password = "Password123!"
        });
        Assert.Equal(HttpStatusCode.Forbidden, createResp.StatusCode);

        // Agent cannot delete agent
        var deleteResp = await client.DeleteAsync($"/api/v1/agents/{factory.AgentId}");
        Assert.Equal(HttpStatusCode.Forbidden, deleteResp.StatusCode);
    }

    [Fact]
    public async Task Agent_can_update_own_status_to_away_and_available()
    {
        await AuthenticateAsync("phase10-agent", IntegrationTestFactory.TestPassword);

        // Update own status to Away
        var awayResp = await client.PutAsJsonAsync($"/api/v1/agents/{factory.AgentId}/status", new UpdateAgentStatusRequestDto
        {
            Status = AgentStatus.Away
        });
        Assert.Equal(HttpStatusCode.OK, awayResp.StatusCode);
        var awayAgent = await awayResp.Content.ReadFromJsonAsync<AgentResponseDto>();
        Assert.Equal(AgentStatus.Away, awayAgent!.Status);

        // Update own status to Available
        var availResp = await client.PutAsJsonAsync($"/api/v1/agents/{factory.AgentId}/status", new UpdateAgentStatusRequestDto
        {
            Status = AgentStatus.Available
        });
        Assert.Equal(HttpStatusCode.OK, availResp.StatusCode);
        var availAgent = await availResp.Content.ReadFromJsonAsync<AgentResponseDto>();
        Assert.Equal(AgentStatus.Available, availAgent!.Status);
    }

    [Fact]
    public async Task Agent_cannot_update_other_agent_status_returns_403_forbidden()
    {
        await AuthenticateAsync("phase10-agent", IntegrationTestFactory.TestPassword);

        var otherAgentId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var agentRole = db.Roles.Single(x => x.Name == "Agent");
            var otherUser = new User
            {
                Id = Guid.NewGuid(),
                RoleId = agentRole.Id,
                UserName = "other-agent",
                PasswordHash = "hash",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            db.Users.Add(otherUser);
            db.Agents.Add(new Agent
            {
                Id = otherAgentId,
                UserId = otherUser.Id,
                EmployeeCode = "OTHER-01",
                DisplayName = "Other Agent",
                Status = AgentStatus.Offline,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var response = await client.PutAsJsonAsync($"/api/v1/agents/{otherAgentId}/status", new UpdateAgentStatusRequestDto
        {
            Status = AgentStatus.Available
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_request_returns_401_unauthorized()
    {
        client.DefaultRequestHeaders.Authorization = null;

        var response = await client.GetAsync("/api/v1/agents");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_can_get_agent_details_and_calls()
    {
        await AuthenticateAsync("phase10-admin", IntegrationTestFactory.TestPassword);

        // Add a call for this agent
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            db.Calls.Add(new Call
            {
                Id = Guid.NewGuid(),
                CustomerId = factory.CustomerId,
                AssignedAgentId = factory.AgentId,
                PhoneNumber = "8801712345678",
                CorrelationId = $"DETAILS-TEST-{Guid.NewGuid():N}",
                Direction = CallDirection.Inbound,
                Status = CallStatus.Completed,
                StartedAt = DateTime.UtcNow.AddMinutes(-10),
                EndedAt = DateTime.UtcNow.AddMinutes(-5),
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // 1. Get Agent Details
        var detailsResp = await client.GetAsync($"/api/v1/agents/{factory.AgentId}/details");
        Assert.Equal(HttpStatusCode.OK, detailsResp.StatusCode);
        var details = await detailsResp.Content.ReadFromJsonAsync<AgentDetailsResponseDto>();
        Assert.NotNull(details);
        Assert.Equal(factory.AgentId, details.Id);
        Assert.True(details.TotalAssignedCalls >= 1);
        Assert.True(details.CompletedCalls >= 1);
        Assert.NotEmpty(details.RecentCalls);

        // 2. Get Agent Calls
        var callsResp = await client.GetAsync($"/api/v1/agents/{factory.AgentId}/calls?page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, callsResp.StatusCode);
        var calls = await callsResp.Content.ReadFromJsonAsync<List<CallResponseDto>>();
        Assert.NotNull(calls);
        Assert.NotEmpty(calls);
    }
}

internal static class HttpClientExtensions
{
    public static Task<HttpResponseMessage> HttpPostAsync(this HttpClient client, string uri, object body) =>
        client.PostAsJsonAsync(uri, body);
}
