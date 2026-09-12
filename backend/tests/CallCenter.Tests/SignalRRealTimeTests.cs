using System.Net.Http.Json;
using CallCenter.Application.Authentication.DTOs;
using CallCenter.Application.RealTime;
using CallCenter.Application.RealTime.Contracts;
using CallCenter.Domain.Entities;
using CallCenter.Domain.Enums;
using CallCenter.Domain.Security;
using CallCenter.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CallCenter.Tests;

public sealed class SignalRRealTimeTests : IAsyncLifetime
{
    private readonly IntegrationTestFactory factory = new();
    private HttpClient client = null!;

    private Guid agent2UserId;
    private Guid agent2Id;

    public async Task InitializeAsync()
    {
        client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

        await factory.SeedAsync();

        // Seed a second agent for multi-agent isolation verification
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
        var agentRole = await db.Roles.SingleAsync(r => r.Name == RolePermissionMatrix.RoleAgent);

        agent2UserId = Guid.NewGuid();
        agent2Id = Guid.NewGuid();

        var hasher = new PasswordHasher<User>();
        var user2 = new User
        {
            Id = agent2UserId,
            RoleId = agentRole.Id,
            UserName = "agent-two",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        user2.PasswordHash = hasher.HashPassword(user2, IntegrationTestFactory.TestPassword);

        var agent2 = new Agent
        {
            Id = agent2Id,
            UserId = agent2UserId,
            EmployeeCode = "AGT-TWO",
            DisplayName = "Agent Two",
            Status = AgentStatus.Available,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        db.Users.Add(user2);
        db.Agents.Add(agent2);
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync()
    {
        client.Dispose();
        factory.Dispose();
        return Task.CompletedTask;
    }

    private async Task<string> GetTokenAsync(string userName, string password = IntegrationTestFactory.TestPassword)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto
        {
            UserName = userName,
            Password = password
        });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(body?.AccessToken);
        return body.AccessToken;
    }

    private HubConnection CreateHubConnection(string? token = null)
    {
        var hubUrl = new Uri(factory.Server.BaseAddress, "/hubs/call-center");
        var builder = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                if (!string.IsNullOrEmpty(token))
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(token);
                }
            });

        return builder.Build();
    }

    [Fact]
    public async Task All_workforce_roles_can_connect_to_SignalR_hub()
    {
        var adminToken = await GetTokenAsync("phase10-admin");
        var supToken = await GetTokenAsync("phase10-supervisor");
        var agentToken = await GetTokenAsync("phase10-agent");

        await using var adminConn = CreateHubConnection(adminToken);
        await using var supConn = CreateHubConnection(supToken);
        await using var agentConn = CreateHubConnection(agentToken);

        await adminConn.StartAsync();
        await supConn.StartAsync();
        await agentConn.StartAsync();

        Assert.Equal(HubConnectionState.Connected, adminConn.State);
        Assert.Equal(HubConnectionState.Connected, supConn.State);
        Assert.Equal(HubConnectionState.Connected, agentConn.State);
    }

    [Fact]
    public async Task Unauthenticated_client_cannot_connect_to_SignalR_hub()
    {
        await using var anonConn = CreateHubConnection(null);

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await anonConn.StartAsync();
        });
    }

    [Fact]
    public async Task CallAssigned_is_received_by_assigned_agent_supervisor_and_admin_but_not_other_agents()
    {
        var adminToken = await GetTokenAsync("phase10-admin");
        var supToken = await GetTokenAsync("phase10-supervisor");
        var agent1Token = await GetTokenAsync("phase10-agent");
        var agent2Token = await GetTokenAsync("agent-two");

        await using var adminConn = CreateHubConnection(adminToken);
        await using var supConn = CreateHubConnection(supToken);
        await using var agent1Conn = CreateHubConnection(agent1Token);
        await using var agent2Conn = CreateHubConnection(agent2Token);

        var adminReceived = new TaskCompletionSource<CallAssignedEvent>();
        var supReceived = new TaskCompletionSource<CallAssignedEvent>();
        var agent1Received = new TaskCompletionSource<CallAssignedEvent>();
        var agent2ReceivedCount = 0;

        adminConn.On<CallAssignedEvent>("CallAssigned", ev => adminReceived.TrySetResult(ev));
        supConn.On<CallAssignedEvent>("CallAssigned", ev => supReceived.TrySetResult(ev));
        agent1Conn.On<CallAssignedEvent>("CallAssigned", ev => agent1Received.TrySetResult(ev));
        agent2Conn.On<CallAssignedEvent>("CallAssigned", _ => Interlocked.Increment(ref agent2ReceivedCount));

        await adminConn.StartAsync();
        await supConn.StartAsync();
        await agent1Conn.StartAsync();
        await agent2Conn.StartAsync();

        var callId = Guid.NewGuid();
        var callAssignedEvent = new CallAssignedEvent(
            callId,
            factory.AgentId, // Assigned to Agent 1
            "Phase 10 Agent",
            factory.CustomerId,
            "Phase 10 Customer",
            "8801712345678",
            "Inbound",
            "Ringing",
            DateTime.UtcNow);

        // Emit through IRealTimeNotifier
        using (var scope = factory.Services.CreateScope())
        {
            var notifier = scope.ServiceProvider.GetRequiredService<IRealTimeNotifier>();
            await notifier.NotifyCallAssignedAsync(callAssignedEvent);
        }

        // Wait for expected recipients
        var adminEvent = await Task.WhenAny(adminReceived.Task, Task.Delay(4000));
        var supEvent = await Task.WhenAny(supReceived.Task, Task.Delay(4000));
        var agent1Event = await Task.WhenAny(agent1Received.Task, Task.Delay(4000));

        Assert.True(adminReceived.Task.IsCompletedSuccessfully, "Admin should have received CallAssigned");
        Assert.True(supReceived.Task.IsCompletedSuccessfully, "Supervisor should have received CallAssigned");
        Assert.True(agent1Received.Task.IsCompletedSuccessfully, "Assigned Agent 1 should have received CallAssigned");

        // Small delay to ensure no unexpected event reached Agent 2
        await Task.Delay(200);
        Assert.Equal(0, agent2ReceivedCount); // Agent 2 must NOT receive Agent 1's CallAssigned event!
    }

    [Fact]
    public async Task IncomingCall_is_received_by_workforce_groups()
    {
        var adminToken = await GetTokenAsync("phase10-admin");
        var supToken = await GetTokenAsync("phase10-supervisor");
        var agent1Token = await GetTokenAsync("phase10-agent");
        var agent2Token = await GetTokenAsync("agent-two");

        await using var adminConn = CreateHubConnection(adminToken);
        await using var supConn = CreateHubConnection(supToken);
        await using var agent1Conn = CreateHubConnection(agent1Token);
        await using var agent2Conn = CreateHubConnection(agent2Token);

        var adminReceived = new TaskCompletionSource<IncomingCallEvent>();
        var supReceived = new TaskCompletionSource<IncomingCallEvent>();
        var agent1Received = new TaskCompletionSource<IncomingCallEvent>();
        var agent2Received = new TaskCompletionSource<IncomingCallEvent>();

        adminConn.On<IncomingCallEvent>("IncomingCall", ev => adminReceived.TrySetResult(ev));
        supConn.On<IncomingCallEvent>("IncomingCall", ev => supReceived.TrySetResult(ev));
        agent1Conn.On<IncomingCallEvent>("IncomingCall", ev => agent1Received.TrySetResult(ev));
        agent2Conn.On<IncomingCallEvent>("IncomingCall", ev => agent2Received.TrySetResult(ev));

        await adminConn.StartAsync();
        await supConn.StartAsync();
        await agent1Conn.StartAsync();
        await agent2Conn.StartAsync();

        var incomingEvent = new IncomingCallEvent(
            Guid.NewGuid(),
            factory.CustomerId,
            "8801712345678",
            "Inbound",
            "Queued",
            DateTime.UtcNow);

        using (var scope = factory.Services.CreateScope())
        {
            var notifier = scope.ServiceProvider.GetRequiredService<IRealTimeNotifier>();
            await notifier.NotifyIncomingCallAsync(incomingEvent);
        }

        await Task.WhenAll(
            adminReceived.Task.WaitAsync(TimeSpan.FromSeconds(4)),
            supReceived.Task.WaitAsync(TimeSpan.FromSeconds(4)),
            agent1Received.Task.WaitAsync(TimeSpan.FromSeconds(4)),
            agent2Received.Task.WaitAsync(TimeSpan.FromSeconds(4)));

        Assert.True(adminReceived.Task.IsCompletedSuccessfully);
        Assert.True(supReceived.Task.IsCompletedSuccessfully);
        Assert.True(agent1Received.Task.IsCompletedSuccessfully);
        Assert.True(agent2Received.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task CallStatusChanged_with_assigned_agent_is_isolated_from_unrelated_agents()
    {
        var adminToken = await GetTokenAsync("phase10-admin");
        var supToken = await GetTokenAsync("phase10-supervisor");
        var agent1Token = await GetTokenAsync("phase10-agent");
        var agent2Token = await GetTokenAsync("agent-two");

        await using var adminConn = CreateHubConnection(adminToken);
        await using var supConn = CreateHubConnection(supToken);
        await using var agent1Conn = CreateHubConnection(agent1Token);
        await using var agent2Conn = CreateHubConnection(agent2Token);

        var adminReceived = new TaskCompletionSource<CallStatusChangedEvent>();
        var supReceived = new TaskCompletionSource<CallStatusChangedEvent>();
        var agent1Received = new TaskCompletionSource<CallStatusChangedEvent>();
        var agent2ReceivedCount = 0;

        adminConn.On<CallStatusChangedEvent>("CallStatusChanged", ev => adminReceived.TrySetResult(ev));
        supConn.On<CallStatusChangedEvent>("CallStatusChanged", ev => supReceived.TrySetResult(ev));
        agent1Conn.On<CallStatusChangedEvent>("CallStatusChanged", ev => agent1Received.TrySetResult(ev));
        agent2Conn.On<CallStatusChangedEvent>("CallStatusChanged", _ => Interlocked.Increment(ref agent2ReceivedCount));

        await adminConn.StartAsync();
        await supConn.StartAsync();
        await agent1Conn.StartAsync();
        await agent2Conn.StartAsync();

        var callStatusEvent = new CallStatusChangedEvent(
            Guid.NewGuid(),
            factory.AgentId, // Agent 1
            factory.CustomerId,
            "Connected",
            DateTime.UtcNow);

        using (var scope = factory.Services.CreateScope())
        {
            var notifier = scope.ServiceProvider.GetRequiredService<IRealTimeNotifier>();
            await notifier.NotifyCallStatusChangedAsync(callStatusEvent);
        }

        await Task.WhenAll(
            adminReceived.Task.WaitAsync(TimeSpan.FromSeconds(4)),
            supReceived.Task.WaitAsync(TimeSpan.FromSeconds(4)),
            agent1Received.Task.WaitAsync(TimeSpan.FromSeconds(4)));

        Assert.True(adminReceived.Task.IsCompletedSuccessfully);
        Assert.True(supReceived.Task.IsCompletedSuccessfully);
        Assert.True(agent1Received.Task.IsCompletedSuccessfully);

        await Task.Delay(200);
        Assert.Equal(0, agent2ReceivedCount); // Unrelated agent must not receive active call status
    }

    [Fact]
    public async Task CallTransferred_is_delivered_to_source_target_supervisor_and_admin()
    {
        var adminToken = await GetTokenAsync("phase10-admin");
        var supToken = await GetTokenAsync("phase10-supervisor");
        var agent1Token = await GetTokenAsync("phase10-agent");
        var agent2Token = await GetTokenAsync("agent-two");

        await using var adminConn = CreateHubConnection(adminToken);
        await using var supConn = CreateHubConnection(supToken);
        await using var agent1Conn = CreateHubConnection(agent1Token);
        await using var agent2Conn = CreateHubConnection(agent2Token);

        var adminReceived = new TaskCompletionSource<CallTransferredEvent>();
        var supReceived = new TaskCompletionSource<CallTransferredEvent>();
        var agent1Received = new TaskCompletionSource<CallTransferredEvent>();
        var agent2Received = new TaskCompletionSource<CallTransferredEvent>();

        adminConn.On<CallTransferredEvent>("CallTransferred", ev => adminReceived.TrySetResult(ev));
        supConn.On<CallTransferredEvent>("CallTransferred", ev => supReceived.TrySetResult(ev));
        agent1Conn.On<CallTransferredEvent>("CallTransferred", ev => agent1Received.TrySetResult(ev));
        agent2Conn.On<CallTransferredEvent>("CallTransferred", ev => agent2Received.TrySetResult(ev));

        await adminConn.StartAsync();
        await supConn.StartAsync();
        await agent1Conn.StartAsync();
        await agent2Conn.StartAsync();

        var transferredEvent = new CallTransferredEvent(
            Guid.NewGuid(),
            factory.AgentId, // Source: Agent 1
            agent2Id,        // Target: Agent 2
            null,
            "Warm",
            "Escalation to senior agent",
            DateTime.UtcNow);

        using (var scope = factory.Services.CreateScope())
        {
            var notifier = scope.ServiceProvider.GetRequiredService<IRealTimeNotifier>();
            await notifier.NotifyCallTransferredAsync(transferredEvent);
        }

        await Task.WhenAll(
            adminReceived.Task.WaitAsync(TimeSpan.FromSeconds(4)),
            supReceived.Task.WaitAsync(TimeSpan.FromSeconds(4)),
            agent1Received.Task.WaitAsync(TimeSpan.FromSeconds(4)),
            agent2Received.Task.WaitAsync(TimeSpan.FromSeconds(4)));

        Assert.True(adminReceived.Task.IsCompletedSuccessfully);
        Assert.True(supReceived.Task.IsCompletedSuccessfully);
        Assert.True(agent1Received.Task.IsCompletedSuccessfully);
        Assert.True(agent2Received.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task AgentStatusChanged_and_QueueUpdated_are_delivered_to_workforce()
    {
        var adminToken = await GetTokenAsync("phase10-admin");
        var agentToken = await GetTokenAsync("phase10-agent");

        await using var adminConn = CreateHubConnection(adminToken);
        await using var agentConn = CreateHubConnection(agentToken);

        var statusReceived = new TaskCompletionSource<AgentStatusChangedEvent>();
        var queueReceived = new TaskCompletionSource<QueueUpdatedEvent>();

        agentConn.On<AgentStatusChangedEvent>("AgentStatusChanged", ev => statusReceived.TrySetResult(ev));
        adminConn.On<QueueUpdatedEvent>("QueueUpdated", ev => queueReceived.TrySetResult(ev));

        await adminConn.StartAsync();
        await agentConn.StartAsync();

        var statusEvent = new AgentStatusChangedEvent(
            factory.AgentId,
            factory.AgentUserId,
            "Available",
            DateTime.UtcNow);

        var queueEvent = new QueueUpdatedEvent(
            Guid.NewGuid(),
            5,
            DateTime.UtcNow);

        using (var scope = factory.Services.CreateScope())
        {
            var notifier = scope.ServiceProvider.GetRequiredService<IRealTimeNotifier>();
            await notifier.NotifyAgentStatusChangedAsync(statusEvent);
            await notifier.NotifyQueueUpdatedAsync(queueEvent);
        }

        await Task.WhenAll(
            statusReceived.Task.WaitAsync(TimeSpan.FromSeconds(4)),
            queueReceived.Task.WaitAsync(TimeSpan.FromSeconds(4)));

        Assert.True(statusReceived.Task.IsCompletedSuccessfully);
        Assert.True(queueReceived.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Targeted_notification_delivers_only_to_intended_recipient()
    {
        var adminToken = await GetTokenAsync("phase10-admin");
        var supToken = await GetTokenAsync("phase10-supervisor");
        var agentToken = await GetTokenAsync("phase10-agent");

        await using var adminConn = CreateHubConnection(adminToken);
        await using var supConn = CreateHubConnection(supToken);
        await using var agentConn = CreateHubConnection(agentToken);

        var adminReceived = new TaskCompletionSource<NotificationEvent>();
        var supReceivedCount = 0;
        var agentReceivedCount = 0;

        adminConn.On<NotificationEvent>("Notification", ev => adminReceived.TrySetResult(ev));
        supConn.On<NotificationEvent>("Notification", _ => Interlocked.Increment(ref supReceivedCount));
        agentConn.On<NotificationEvent>("Notification", _ => Interlocked.Increment(ref agentReceivedCount));

        await adminConn.StartAsync();
        await supConn.StartAsync();
        await agentConn.StartAsync();

        var notification = new NotificationEvent(
            Guid.NewGuid(),
            "Security Alert",
            "New admin configuration applied.",
            "Info",
            "User",
            factory.AdminUserId.ToString(),
            DateTime.UtcNow);

        using (var scope = factory.Services.CreateScope())
        {
            var notifier = scope.ServiceProvider.GetRequiredService<IRealTimeNotifier>();
            await notifier.NotifyNotificationAsync(notification);
        }

        await adminReceived.Task.WaitAsync(TimeSpan.FromSeconds(4));
        Assert.True(adminReceived.Task.IsCompletedSuccessfully);

        await Task.Delay(200);
        Assert.Equal(0, supReceivedCount);
        Assert.Equal(0, agentReceivedCount);
    }
}
