using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CallCenter.Application.Authentication.DTOs;
using CallCenter.Application.Telephony.DTOs;
using CallCenter.Domain.Entities;
using CallCenter.Domain.Enums;
using CallCenter.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CallCenter.Tests;

public sealed class CallTransferTests : IAsyncLifetime
{
    private static readonly PasswordHasher<User> Hasher = new();
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

    private async Task AuthenticateAsync(string userName)
    {
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto
        {
            UserName = userName,
            Password = IntegrationTestFactory.TestPassword
        });
        login.EnsureSuccessStatusCode();
        var body = await login.Content.ReadFromJsonAsync<LoginResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.AccessToken);
    }

    private static User CreateUser(Guid roleId, string userName)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            RoleId = roleId,
            UserName = userName,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        user.PasswordHash = Hasher.HashPassword(user, IntegrationTestFactory.TestPassword);
        return user;
    }

    private static Call CreateCall(Guid customerId, CallStatus status, Guid? assignedAgentId = null, Guid? callId = null) => new()
    {
        Id = callId ?? Guid.NewGuid(),
        CustomerId = customerId,
        AssignedAgentId = assignedAgentId,
        Direction = CallDirection.Inbound,
        Status = status,
        PhoneNumber = "8801712345678",
        CorrelationId = $"XFER-{Guid.NewGuid():N}",
        StartedAt = DateTime.UtcNow.AddMinutes(-3),
        AnsweredAt = status is CallStatus.Connected or CallStatus.OnHold ? DateTime.UtcNow.AddMinutes(-2) : null,
        CreatedAt = DateTime.UtcNow.AddMinutes(-3)
    };

    [Fact]
    public async Task Get_eligible_agents_returns_available_agents_excluding_caller()
    {
        Guid callId = Guid.NewGuid();
        Guid agent1Id;
        Guid agent2Id;
        Guid agent3Id;
        string agent1UserName = "xfer-eligible-agent1";

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var agentRole = await db.Roles.SingleAsync(r => r.Name == "Agent");

            var user1 = CreateUser(agentRole.Id, agent1UserName);
            agent1Id = Guid.NewGuid();
            var agent1 = new Agent
            {
                Id = agent1Id,
                UserId = user1.Id,
                EmployeeCode = "XF01",
                DisplayName = "Transfer Agent 1",
                Status = AgentStatus.Busy,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            var user2 = CreateUser(agentRole.Id, "xfer-eligible-agent2");
            agent2Id = Guid.NewGuid();
            var agent2 = new Agent
            {
                Id = agent2Id,
                UserId = user2.Id,
                EmployeeCode = "XF02",
                DisplayName = "Transfer Agent 2 Available",
                Status = AgentStatus.Available,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            var user3 = CreateUser(agentRole.Id, "xfer-eligible-agent3");
            agent3Id = Guid.NewGuid();
            var agent3 = new Agent
            {
                Id = agent3Id,
                UserId = user3.Id,
                EmployeeCode = "XF03",
                DisplayName = "Transfer Agent 3 Busy",
                Status = AgentStatus.Busy,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            var customer = await db.Customers.FirstAsync();
            var call = CreateCall(customer.Id, CallStatus.Connected, agent1Id, callId);

            db.Users.AddRange(user1, user2, user3);
            db.Agents.AddRange(agent1, agent2, agent3);
            db.Calls.Add(call);
            await db.SaveChangesAsync();
        }

        await AuthenticateAsync(agent1UserName);

        var response = await client.GetAsync($"/api/v1/telephony/calls/{callId}/transfer/eligible-agents?transferType=Blind");
        response.EnsureSuccessStatusCode();

        var agents = await response.Content.ReadFromJsonAsync<List<EligibleAgentDto>>();
        Assert.NotNull(agents);

        // Caller agent1 should not be in the list
        Assert.DoesNotContain(agents, a => a.AgentId == agent1Id);

        // Agent 2 should be in the list and eligible
        var target2 = agents.SingleOrDefault(a => a.AgentId == agent2Id);
        Assert.NotNull(target2);
        Assert.True(target2.IsEligible);

        // Agent 3 should be in the list but not eligible (busy)
        var target3 = agents.SingleOrDefault(a => a.AgentId == agent3Id);
        Assert.NotNull(target3);
        Assert.False(target3.IsEligible);
    }

    [Fact]
    public async Task Get_eligible_agents_for_supervisor_transfer_filters_to_supervisors()
    {
        Guid callId = Guid.NewGuid();
        Guid agent1Id;
        Guid agent2Id;
        Guid supervisorId;
        string agent1UserName = "xfer-sup-agent1";

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var agentRole = await db.Roles.SingleAsync(r => r.Name == "Agent");
            var supervisorRole = await db.Roles.SingleAsync(r => r.Name == "Supervisor");

            var user1 = CreateUser(agentRole.Id, agent1UserName);
            agent1Id = Guid.NewGuid();
            var agent1 = new Agent
            {
                Id = agent1Id,
                UserId = user1.Id,
                EmployeeCode = "XFS01",
                DisplayName = "Transfer Regular Agent",
                Status = AgentStatus.Busy,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            var user2 = CreateUser(agentRole.Id, "xfer-sup-agent2");
            agent2Id = Guid.NewGuid();
            var agent2 = new Agent
            {
                Id = agent2Id,
                UserId = user2.Id,
                EmployeeCode = "XFS02",
                DisplayName = "Transfer Peer Agent",
                Status = AgentStatus.Available,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            var supUser = CreateUser(supervisorRole.Id, "xfer-supervisor-user");
            supervisorId = Guid.NewGuid();
            var supervisor = new Agent
            {
                Id = supervisorId,
                UserId = supUser.Id,
                EmployeeCode = "SU01",
                DisplayName = "Shift Supervisor",
                Status = AgentStatus.Available,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            var customer = await db.Customers.FirstAsync();
            var call = CreateCall(customer.Id, CallStatus.Connected, agent1Id, callId);

            db.Users.AddRange(user1, user2, supUser);
            db.Agents.AddRange(agent1, agent2, supervisor);
            db.Calls.Add(call);
            await db.SaveChangesAsync();
        }

        await AuthenticateAsync(agent1UserName);

        var response = await client.GetAsync($"/api/v1/telephony/calls/{callId}/transfer/eligible-agents?transferType=Supervisor");
        response.EnsureSuccessStatusCode();

        var agents = await response.Content.ReadFromJsonAsync<List<EligibleAgentDto>>();
        Assert.NotNull(agents);

        // Should contain supervisor
        Assert.Contains(agents, a => a.AgentId == supervisorId && a.RoleName == "Supervisor");

        // Should NOT contain regular peer agent
        Assert.DoesNotContain(agents, a => a.AgentId == agent2Id);
    }

    [Fact]
    public async Task Blind_transfer_reassigns_to_target_agent_frees_source_agent_and_records_events()
    {
        Guid callId = Guid.NewGuid();
        Guid agent1Id;
        Guid agent2Id;
        string agent1UserName = "xfer-blind-agent1";

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var agentRole = await db.Roles.SingleAsync(r => r.Name == "Agent");

            var user1 = CreateUser(agentRole.Id, agent1UserName);
            agent1Id = Guid.NewGuid();
            var agent1 = new Agent
            {
                Id = agent1Id,
                UserId = user1.Id,
                EmployeeCode = "BL01",
                DisplayName = "Blind Transfer Source Agent",
                Status = AgentStatus.Busy,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            var user2 = CreateUser(agentRole.Id, "xfer-blind-target");
            agent2Id = Guid.NewGuid();
            var agent2 = new Agent
            {
                Id = agent2Id,
                UserId = user2.Id,
                EmployeeCode = "BL02",
                DisplayName = "Blind Transfer Target Agent",
                Status = AgentStatus.Available,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            var customer = await db.Customers.FirstAsync();
            var call = CreateCall(customer.Id, CallStatus.Connected, agent1Id, callId);

            db.Users.AddRange(user1, user2);
            db.Agents.AddRange(agent1, agent2);
            db.Calls.Add(call);
            await db.SaveChangesAsync();
        }

        await AuthenticateAsync(agent1UserName);

        var request = new TransferCallRequestDto
        {
            TargetAgentId = agent2Id,
            TransferType = TransferType.Blind,
            Reason = "Cold transfer to specialist"
        };

        var response = await client.PostAsJsonAsync($"/api/v1/telephony/calls/{callId}/transfer", request);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<TelephonyCallResponseDto>();
        Assert.NotNull(body);
        Assert.Equal(callId, body.CallId);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var updatedCall = await db.Calls.SingleAsync(c => c.Id == callId);
            Assert.Equal(agent2Id, updatedCall.AssignedAgentId);
            Assert.Equal(CallStatus.Ringing, updatedCall.Status);

            var prevAgent = await db.Agents.SingleAsync(a => a.Id == agent1Id);
            Assert.Equal(AgentStatus.Available, prevAgent.Status);

            var transferEvent = await db.CallEvents
                .Where(e => e.CallId == callId && e.EventType == "Transferred")
                .SingleOrDefaultAsync();
            Assert.NotNull(transferEvent);
            Assert.Contains("Blind", transferEvent.MetadataJson);
            Assert.Contains("Cold transfer to specialist", transferEvent.MetadataJson);
        }

        // Target agent accepts the transferred incoming call
        await AuthenticateAsync("xfer-blind-target");
        var acceptResponse = await client.PostAsync($"/api/v1/telephony/calls/{callId}/accept", null);
        acceptResponse.EnsureSuccessStatusCode();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var connectedCall = await db.Calls.SingleAsync(c => c.Id == callId);
            Assert.Equal(CallStatus.Connected, connectedCall.Status);

            var targetAgent = await db.Agents.SingleAsync(a => a.Id == agent2Id);
            Assert.Equal(AgentStatus.Busy, targetAgent.Status);
        }
    }

    [Fact]
    public async Task Warm_transfer_logs_transfer_initiated_and_reassigns_agent()
    {
        Guid callId = Guid.NewGuid();
        Guid agent1Id;
        Guid agent2Id;
        string agent1UserName = "xfer-warm-agent1";

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var agentRole = await db.Roles.SingleAsync(r => r.Name == "Agent");

            var user1 = CreateUser(agentRole.Id, agent1UserName);
            agent1Id = Guid.NewGuid();
            var agent1 = new Agent
            {
                Id = agent1Id,
                UserId = user1.Id,
                EmployeeCode = "WM01",
                DisplayName = "Warm Transfer Source",
                Status = AgentStatus.Busy,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            var user2 = CreateUser(agentRole.Id, "xfer-warm-target");
            agent2Id = Guid.NewGuid();
            var agent2 = new Agent
            {
                Id = agent2Id,
                UserId = user2.Id,
                EmployeeCode = "WM02",
                DisplayName = "Warm Transfer Target",
                Status = AgentStatus.Available,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            var customer = await db.Customers.FirstAsync();
            var call = CreateCall(customer.Id, CallStatus.Connected, agent1Id, callId);

            db.Users.AddRange(user1, user2);
            db.Agents.AddRange(agent1, agent2);
            db.Calls.Add(call);
            await db.SaveChangesAsync();
        }

        await AuthenticateAsync(agent1UserName);

        var request = new TransferCallRequestDto
        {
            TargetAgentId = agent2Id,
            TransferType = TransferType.Warm,
            Reason = "Consultative transfer with customer on hold"
        };

        var response = await client.PostAsJsonAsync($"/api/v1/telephony/calls/{callId}/transfer", request);
        response.EnsureSuccessStatusCode();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var updatedCall = await db.Calls.SingleAsync(c => c.Id == callId);
            Assert.Equal(agent2Id, updatedCall.AssignedAgentId);

            var initiatedEvent = await db.CallEvents
                .Where(e => e.CallId == callId && e.EventType == "TransferInitiated")
                .SingleOrDefaultAsync();
            Assert.NotNull(initiatedEvent);
            Assert.Contains("Warm", initiatedEvent.MetadataJson);

            var transferredEvent = await db.CallEvents
                .Where(e => e.CallId == callId && e.EventType == "Transferred")
                .SingleOrDefaultAsync();
            Assert.NotNull(transferredEvent);
            Assert.Contains("Warm", transferredEvent.MetadataJson);
        }
    }

    [Fact]
    public async Task Supervisor_transfer_to_non_supervisor_fails_with_conflict()
    {
        Guid callId = Guid.NewGuid();
        Guid agent1Id;
        Guid agent2Id;
        string agent1UserName = "xfer-supfail-agent1";

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var agentRole = await db.Roles.SingleAsync(r => r.Name == "Agent");

            var user1 = CreateUser(agentRole.Id, agent1UserName);
            agent1Id = Guid.NewGuid();
            var agent1 = new Agent
            {
                Id = agent1Id,
                UserId = user1.Id,
                EmployeeCode = "SF01",
                DisplayName = "Regular Agent 1",
                Status = AgentStatus.Busy,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            var user2 = CreateUser(agentRole.Id, "xfer-supfail-agent2");
            agent2Id = Guid.NewGuid();
            var agent2 = new Agent
            {
                Id = agent2Id,
                UserId = user2.Id,
                EmployeeCode = "SF02",
                DisplayName = "Regular Agent 2 Not Supervisor",
                Status = AgentStatus.Available,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            var customer = await db.Customers.FirstAsync();
            var call = CreateCall(customer.Id, CallStatus.Connected, agent1Id, callId);

            db.Users.AddRange(user1, user2);
            db.Agents.AddRange(agent1, agent2);
            db.Calls.Add(call);
            await db.SaveChangesAsync();
        }

        await AuthenticateAsync(agent1UserName);

        var request = new TransferCallRequestDto
        {
            TargetAgentId = agent2Id,
            TransferType = TransferType.Supervisor,
            Reason = "Attempted supervisor escalation to non-supervisor"
        };

        var response = await client.PostAsJsonAsync($"/api/v1/telephony/calls/{callId}/transfer", request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Transfer_to_busy_agent_returns_conflict()
    {
        Guid callId = Guid.NewGuid();
        Guid agent1Id;
        Guid agent2Id;
        string agent1UserName = "xfer-busy-agent1";

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var agentRole = await db.Roles.SingleAsync(r => r.Name == "Agent");

            var user1 = CreateUser(agentRole.Id, agent1UserName);
            agent1Id = Guid.NewGuid();
            var agent1 = new Agent
            {
                Id = agent1Id,
                UserId = user1.Id,
                EmployeeCode = "BY01",
                DisplayName = "Busy Source Agent",
                Status = AgentStatus.Busy,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            var user2 = CreateUser(agentRole.Id, "xfer-busy-target");
            agent2Id = Guid.NewGuid();
            var agent2 = new Agent
            {
                Id = agent2Id,
                UserId = user2.Id,
                EmployeeCode = "BY02",
                DisplayName = "Target Agent Currently Busy",
                Status = AgentStatus.Busy,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            var customer = await db.Customers.FirstAsync();
            var call = CreateCall(customer.Id, CallStatus.Connected, agent1Id, callId);

            db.Users.AddRange(user1, user2);
            db.Agents.AddRange(agent1, agent2);
            db.Calls.Add(call);
            await db.SaveChangesAsync();
        }

        await AuthenticateAsync(agent1UserName);

        var request = new TransferCallRequestDto
        {
            TargetAgentId = agent2Id,
            TransferType = TransferType.Blind,
            Reason = "Transfer to busy agent"
        };

        var response = await client.PostAsJsonAsync($"/api/v1/telephony/calls/{callId}/transfer", request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Transfer_to_self_returns_conflict()
    {
        Guid callId = Guid.NewGuid();
        Guid agent1Id;
        string agent1UserName = "xfer-self-agent";

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var agentRole = await db.Roles.SingleAsync(r => r.Name == "Agent");

            var user1 = CreateUser(agentRole.Id, agent1UserName);
            agent1Id = Guid.NewGuid();
            var agent1 = new Agent
            {
                Id = agent1Id,
                UserId = user1.Id,
                EmployeeCode = "SF99",
                DisplayName = "Self Transfer Agent",
                Status = AgentStatus.Busy,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            var customer = await db.Customers.FirstAsync();
            var call = CreateCall(customer.Id, CallStatus.Connected, agent1Id, callId);

            db.Users.Add(user1);
            db.Agents.Add(agent1);
            db.Calls.Add(call);
            await db.SaveChangesAsync();
        }

        await AuthenticateAsync(agent1UserName);

        var request = new TransferCallRequestDto
        {
            TargetAgentId = agent1Id,
            TransferType = TransferType.Blind,
            Reason = "Transferring to myself"
        };

        var response = await client.PostAsJsonAsync($"/api/v1/telephony/calls/{callId}/transfer", request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Unauthorized_agent_cannot_transfer_call()
    {
        Guid callId = Guid.NewGuid();
        Guid agent1Id;
        Guid agent2Id;
        Guid agent3Id;
        string agent3UserName = "xfer-unauth-agent3";

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var agentRole = await db.Roles.SingleAsync(r => r.Name == "Agent");

            var user1 = CreateUser(agentRole.Id, "xfer-owner-agent1");
            agent1Id = Guid.NewGuid();
            var agent1 = new Agent
            {
                Id = agent1Id,
                UserId = user1.Id,
                EmployeeCode = "UA01",
                DisplayName = "Call Owner Agent",
                Status = AgentStatus.Busy,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            var user2 = CreateUser(agentRole.Id, "xfer-target-agent2");
            agent2Id = Guid.NewGuid();
            var agent2 = new Agent
            {
                Id = agent2Id,
                UserId = user2.Id,
                EmployeeCode = "UA02",
                DisplayName = "Target Agent",
                Status = AgentStatus.Available,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            var user3 = CreateUser(agentRole.Id, agent3UserName);
            agent3Id = Guid.NewGuid();
            var agent3 = new Agent
            {
                Id = agent3Id,
                UserId = user3.Id,
                EmployeeCode = "UA03",
                DisplayName = "Intruding Agent",
                Status = AgentStatus.Available,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            var customer = await db.Customers.FirstAsync();
            var call = CreateCall(customer.Id, CallStatus.Connected, agent1Id, callId);

            db.Users.AddRange(user1, user2, user3);
            db.Agents.AddRange(agent1, agent2, agent3);
            db.Calls.Add(call);
            await db.SaveChangesAsync();
        }

        // Intruding agent 3 tries to transfer agent 1's call
        await AuthenticateAsync(agent3UserName);

        var request = new TransferCallRequestDto
        {
            TargetAgentId = agent2Id,
            TransferType = TransferType.Blind,
            Reason = "Unauthorized transfer attempt"
        };

        var response = await client.PostAsJsonAsync($"/api/v1/telephony/calls/{callId}/transfer", request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Transfer_to_queue_reassigns_to_queue_and_frees_source_agent()
    {
        Guid callId = Guid.NewGuid();
        Guid agent1Id;
        Guid queueId;
        string agent1UserName = "xfer-q-agent1";

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var agentRole = await db.Roles.SingleAsync(r => r.Name == "Agent");

            var user1 = CreateUser(agentRole.Id, agent1UserName);
            agent1Id = Guid.NewGuid();
            var agent1 = new Agent
            {
                Id = agent1Id,
                UserId = user1.Id,
                EmployeeCode = "Q01",
                DisplayName = "Queue Transfer Agent",
                Status = AgentStatus.Busy,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            queueId = Guid.NewGuid();
            var queue = new CallQueue
            {
                Id = queueId,
                Name = "Support Escalation Queue",
                Priority = 10,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            var customer = await db.Customers.FirstAsync();
            var call = CreateCall(customer.Id, CallStatus.Connected, agent1Id, callId);

            db.Users.Add(user1);
            db.Agents.Add(agent1);
            db.CallQueues.Add(queue);
            db.Calls.Add(call);
            await db.SaveChangesAsync();
        }

        await AuthenticateAsync(agent1UserName);

        var request = new TransferCallRequestDto
        {
            TargetQueueId = queueId,
            Reason = "Re-queueing call for specialty queue"
        };

        var response = await client.PostAsJsonAsync($"/api/v1/telephony/calls/{callId}/transfer", request);
        response.EnsureSuccessStatusCode();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var updatedCall = await db.Calls.SingleAsync(c => c.Id == callId);
            Assert.Null(updatedCall.AssignedAgentId);
            Assert.Equal(queueId, updatedCall.CallQueueId);
            Assert.Equal(CallStatus.Queued, updatedCall.Status);

            var prevAgent = await db.Agents.SingleAsync(a => a.Id == agent1Id);
            Assert.Equal(AgentStatus.Available, prevAgent.Status);

            var queueEvent = await db.CallEvents
                .Where(e => e.CallId == callId && e.EventType == "TransferredToQueue")
                .SingleOrDefaultAsync();
            Assert.NotNull(queueEvent);

            var queueEntry = await db.CallQueueEntries
                .Where(e => e.CallId == callId && e.CallQueueId == queueId && e.DequeuedAt == null)
                .SingleOrDefaultAsync();
            Assert.NotNull(queueEntry);
        }
    }
}
