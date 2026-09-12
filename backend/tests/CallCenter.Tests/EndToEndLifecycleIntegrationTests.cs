using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CallCenter.Application.Authentication.DTOs;
using CallCenter.Application.Calls.DTOs;
using CallCenter.Application.Customers.DTOs;
using CallCenter.Application.Reports.DTOs;
using CallCenter.Application.Routing.DTOs;
using CallCenter.Application.Telephony.DTOs;
using CallCenter.Domain.Entities;
using CallCenter.Domain.Enums;
using CallCenter.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CallCenter.Tests;

public sealed class EndToEndLifecycleIntegrationTests : IAsyncLifetime
{
    private readonly IntegrationTestFactory factory = new();
    private HttpClient client = null!;
    private static readonly PasswordHasher<User> Hasher = new();

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

    private async Task<string> LoginAsync(string userName, string password = IntegrationTestFactory.TestPassword)
    {
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto
        {
            UserName = userName,
            Password = password
        });

        login.EnsureSuccessStatusCode();
        var body = await login.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(body?.AccessToken);
        return body.AccessToken;
    }

    private void SetToken(string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    [Fact]
    public async Task FullCallCenterLifecycle_FromLoginToReport_SucceedsEndToEnd()
    {
        // -------------------------------------------------------------
        // STEP 1: LOGIN (Admin & Agents)
        // -------------------------------------------------------------
        var adminToken = await LoginAsync("phase10-admin");
        Assert.False(string.IsNullOrWhiteSpace(adminToken));
        SetToken(adminToken);

        // Seed 2 Agents (Agent1: primary handler, Agent2: transfer target)
        Guid agent1Id = Guid.NewGuid();
        Guid agent2Id = Guid.NewGuid();
        Guid agent1UserId = Guid.NewGuid();
        Guid agent2UserId = Guid.NewGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var agentRole = await db.Roles.SingleAsync(r => r.Name == "Agent");

            var user1 = new User { Id = agent1UserId, RoleId = agentRole.Id, UserName = "e2e-agent1", IsActive = true, CreatedAt = DateTime.UtcNow };
            user1.PasswordHash = Hasher.HashPassword(user1, IntegrationTestFactory.TestPassword);

            var user2 = new User { Id = agent2UserId, RoleId = agentRole.Id, UserName = "e2e-agent2", IsActive = true, CreatedAt = DateTime.UtcNow };
            user2.PasswordHash = Hasher.HashPassword(user2, IntegrationTestFactory.TestPassword);

            db.Users.AddRange(user1, user2);

            var agent1 = new Agent
            {
                Id = agent1Id,
                UserId = agent1UserId,
                EmployeeCode = "E2E01",
                DisplayName = "E2E Agent 1",
                Status = AgentStatus.Available,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            var agent2 = new Agent
            {
                Id = agent2Id,
                UserId = agent2UserId,
                EmployeeCode = "E2E02",
                DisplayName = "E2E Agent 2",
                Status = AgentStatus.Available,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            db.Agents.AddRange(agent1, agent2);
            await db.SaveChangesAsync();
        }

        var agent1Token = await LoginAsync("e2e-agent1");
        var agent2Token = await LoginAsync("e2e-agent2");

        // -------------------------------------------------------------
        // STEP 2: CUSTOMER LOOKUP / CREATION
        // -------------------------------------------------------------
        SetToken(adminToken);
        var customerPhone = "8801812345678";
        var createCustomerResponse = await client.PostAsJsonAsync("/api/v1/customers", new CreateCustomerRequestDto
        {
            FullName = "John Doe Customer",
            Phone = customerPhone,
            Email = "john.doe@example.com",
            Address = "Dhanmondi, Dhaka",
            Notes = "VIP Customer"
        });

        createCustomerResponse.EnsureSuccessStatusCode();
        var customer = await createCustomerResponse.Content.ReadFromJsonAsync<CustomerResponseDto>();
        Assert.NotNull(customer);
        Assert.Equal("John Doe Customer", customer.FullName);
        Guid customerId = customer.Id;

        // -------------------------------------------------------------
        // STEP 3: INCOMING CALL SIMULATION
        // -------------------------------------------------------------
        var inboundCallResponse = await client.PostAsJsonAsync("/api/v1/telephony/incoming/simulate", new SimulateIncomingCallRequestDto
        {
            PhoneNumber = customerPhone,
            CorrelationId = $"E2E-INCOMING-{Guid.NewGuid():N}"
        });

        inboundCallResponse.EnsureSuccessStatusCode();
        var inboundResult = await inboundCallResponse.Content.ReadFromJsonAsync<TelephonyCallResponseDto>();
        Assert.NotNull(inboundResult);
        Guid callId = inboundResult.CallId;

        // -------------------------------------------------------------
        // STEP 4: ASSIGN / ROUTE TO AGENT 1
        // -------------------------------------------------------------
        var assignResponse = await client.PostAsJsonAsync($"/api/v1/routing/calls/{callId}/assign", new AssignCallRequestDto
        {
            AgentId = agent1Id
        });
        assignResponse.EnsureSuccessStatusCode();

        // -------------------------------------------------------------
        // STEP 5: AGENT 1 ACCEPTS CALL (Connected state)
        // -------------------------------------------------------------
        SetToken(agent1Token);
        var acceptResponse = await client.PostAsync($"/api/v1/telephony/calls/{callId}/accept", null);
        acceptResponse.EnsureSuccessStatusCode();

        var callAfterAccept = await client.GetFromJsonAsync<CallResponseDto>($"/api/v1/calls/{callId}");
        Assert.NotNull(callAfterAccept);
        Assert.Equal(CallStatus.Connected, callAfterAccept.Status);
        Assert.Equal(agent1Id, callAfterAccept.AssignedAgentId);

        // -------------------------------------------------------------
        // STEP 6: ACTIVE CALL WORKSPACE - HOLD
        // -------------------------------------------------------------
        var holdResponse = await client.PostAsync($"/api/v1/telephony/calls/{callId}/hold", null);
        holdResponse.EnsureSuccessStatusCode();

        var callOnHold = await client.GetFromJsonAsync<CallResponseDto>($"/api/v1/calls/{callId}");
        Assert.NotNull(callOnHold);
        Assert.Equal(CallStatus.OnHold, callOnHold.Status);

        // -------------------------------------------------------------
        // STEP 7: RESUME CALL
        // -------------------------------------------------------------
        var resumeResponse = await client.PostAsync($"/api/v1/telephony/calls/{callId}/resume", null);
        resumeResponse.EnsureSuccessStatusCode();

        var callResumed = await client.GetFromJsonAsync<CallResponseDto>($"/api/v1/calls/{callId}");
        Assert.NotNull(callResumed);
        Assert.Equal(CallStatus.Connected, callResumed.Status);

        // -------------------------------------------------------------
        // STEP 8: TRANSFER CALL TO AGENT 2
        // -------------------------------------------------------------
        var transferRequest = new TransferCallRequestDto
        {
            TargetAgentId = agent2Id,
            TransferType = TransferType.Blind,
            Reason = "Transferring to second agent for delivery inquiry"
        };

        var transferResponse = await client.PostAsJsonAsync($"/api/v1/telephony/calls/{callId}/transfer", transferRequest);
        transferResponse.EnsureSuccessStatusCode();

        // Target Agent 2 accepts the transferred call
        SetToken(agent2Token);
        var acceptTransferResponse = await client.PostAsync($"/api/v1/telephony/calls/{callId}/accept", null);
        acceptTransferResponse.EnsureSuccessStatusCode();

        var callAfterTransfer = await client.GetFromJsonAsync<CallResponseDto>($"/api/v1/calls/{callId}");
        Assert.NotNull(callAfterTransfer);
        Assert.Equal(CallStatus.Connected, callAfterTransfer.Status);
        Assert.Equal(agent2Id, callAfterTransfer.AssignedAgentId);

        // -------------------------------------------------------------
        // STEP 9: APPLY DISPOSITION & COMPLETE CALL
        // -------------------------------------------------------------
        var completeResponse = await client.PostAsJsonAsync($"/api/v1/telephony/calls/{callId}/complete", new CompleteCallRequestDto
        {
            DispositionId = factory.DispositionId,
            Notes = "Successfully resolved delivery inquiry and placed order"
        });
        completeResponse.EnsureSuccessStatusCode();

        var finalCall = await client.GetFromJsonAsync<CallResponseDto>($"/api/v1/calls/{callId}");
        Assert.NotNull(finalCall);
        Assert.Equal(CallStatus.Completed, finalCall.Status);
        Assert.Equal(factory.DispositionId, finalCall.CallDispositionId);
        Assert.NotNull(finalCall.EndedAt);

        // -------------------------------------------------------------
        // STEP 10: VERIFY CALL HISTORY
        // -------------------------------------------------------------
        SetToken(adminToken);
        var historyResponse = await client.GetAsync($"/api/v1/calls/history?customerId={customerId}");
        historyResponse.EnsureSuccessStatusCode();
        var history = await historyResponse.Content.ReadFromJsonAsync<CallPagedResultDto>();
        Assert.NotNull(history);
        Assert.Contains(history.Items, c => c.Id == callId);

        // -------------------------------------------------------------
        // STEP 11: VERIFY REPORTS SUMMARY / METRICS
        // -------------------------------------------------------------
        var reportsResponse = await client.GetAsync("/api/v1/reports/metrics");
        reportsResponse.EnsureSuccessStatusCode();
        var reportMetrics = await reportsResponse.Content.ReadFromJsonAsync<ReportMetricsDto>();
        Assert.NotNull(reportMetrics);
        Assert.True(reportMetrics.TotalCalls >= 1);
    }
}
