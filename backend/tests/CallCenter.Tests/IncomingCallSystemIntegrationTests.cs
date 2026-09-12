using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CallCenter.Application.Authentication.DTOs;
using CallCenter.Application.Telephony.DTOs;
using CallCenter.Domain.Entities;
using CallCenter.Domain.Enums;
using CallCenter.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CallCenter.Tests;

public sealed class IncomingCallSystemIntegrationTests : IAsyncLifetime
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

    [Fact]
    public async Task Incoming_call_with_existing_customer_resolves_and_routes_correctly()
    {
        await AuthenticateAsync("phase10-admin");

        // Seed an available agent
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var agent = await db.Agents.SingleAsync(a => a.Id == factory.AgentId);
            agent.Status = AgentStatus.Available;
            await db.SaveChangesAsync();
        }

        var request = new SimulateIncomingCallRequestDto
        {
            PhoneNumber = "8801712345678", // Matches factory.CustomerId
            CorrelationId = $"INCOMING-EXISTING-{Guid.NewGuid():N}"
        };

        var response = await client.PostAsJsonAsync("/api/v1/telephony/incoming/simulate", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var call = await response.Content.ReadFromJsonAsync<TelephonyCallResponseDto>();

        Assert.NotNull(call);
        Assert.Equal(factory.CustomerId, call.CustomerId);
        Assert.Equal("8801712345678", call.PhoneNumber);
        Assert.Equal(CallStatus.Ringing, call.Status);
        Assert.Equal(factory.AgentId, call.AssignedAgentId);

        // Verify in DB
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var dbCall = await db.Calls.Include(c => c.Customer).SingleAsync(c => c.Id == call.CallId);
            Assert.Equal(factory.CustomerId, dbCall.CustomerId);
            Assert.Equal("Phase 10 Customer", dbCall.Customer.DisplayName);
            Assert.Equal(CallStatus.Ringing, dbCall.Status);
            Assert.Equal(factory.AgentId, dbCall.AssignedAgentId);
        }
    }

    [Fact]
    public async Task Incoming_call_with_invalid_customer_returns_bad_request()
    {
        await AuthenticateAsync("phase10-admin");

        var nonExistentCustomerId = Guid.NewGuid();
        var request = new SimulateIncomingCallRequestDto
        {
            CustomerId = nonExistentCustomerId,
            PhoneNumber = "8801712345678",
            CorrelationId = $"INCOMING-INVALID-{Guid.NewGuid():N}"
        };

        var response = await client.PostAsJsonAsync("/api/v1/telephony/incoming/simulate", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Verify no call was created
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var callCount = await db.Calls.CountAsync(c => c.CorrelationId == request.CorrelationId);
            Assert.Equal(0, callCount);
        }
    }

    [Fact]
    public async Task Incoming_call_with_duplicate_correlation_returns_409_conflict()
    {
        await AuthenticateAsync("phase10-admin");

        var duplicateCorrId = $"DUP-CORR-{Guid.NewGuid():N}";
        var request = new SimulateIncomingCallRequestDto
        {
            PhoneNumber = "8801712345678",
            CorrelationId = duplicateCorrId
        };

        // First call succeeds
        var firstResponse = await client.PostAsJsonAsync("/api/v1/telephony/incoming/simulate", request);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        // Second call with duplicate correlation ID returns 409 Conflict
        var secondResponse = await client.PostAsJsonAsync("/api/v1/telephony/incoming/simulate", request);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    [Fact]
    public async Task Incoming_call_with_invalid_parameters_returns_400_bad_request()
    {
        await AuthenticateAsync("phase10-admin");

        // Invalid empty phone number
        var invalidPhoneRequest = new SimulateIncomingCallRequestDto
        {
            PhoneNumber = "123", // less than minimum 7 digits
            CorrelationId = $"INV-PHONE-{Guid.NewGuid():N}"
        };

        var response = await client.PostAsJsonAsync("/api/v1/telephony/incoming/simulate", invalidPhoneRequest);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Incoming_call_with_no_available_agent_remains_queued()
    {
        await AuthenticateAsync("phase10-admin");

        // Ensure all agents are Offline
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var agents = await db.Agents.ToListAsync();
            foreach (var a in agents) a.Status = AgentStatus.Offline;
            await db.SaveChangesAsync();
        }

        var request = new SimulateIncomingCallRequestDto
        {
            PhoneNumber = "8801712345678",
            CorrelationId = $"NO-AGENT-QUEUED-{Guid.NewGuid():N}"
        };

        var response = await client.PostAsJsonAsync("/api/v1/telephony/incoming/simulate", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var call = await response.Content.ReadFromJsonAsync<TelephonyCallResponseDto>();

        Assert.NotNull(call);
        Assert.Equal(CallStatus.Queued, call.Status);
        Assert.Null(call.AssignedAgentId);

        // Verify call queue entry is active in DB
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var entry = await db.CallQueueEntries.SingleOrDefaultAsync(e => e.CallId == call.CallId);
            Assert.NotNull(entry);
            Assert.Null(entry.DequeuedAt);
            Assert.True(entry.Position >= 1);
        }
    }

    [Fact]
    public async Task Incoming_call_with_available_agent_routes_to_ringing_and_can_be_accepted_and_ended()
    {
        // Seed agent as Available
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var agent = await db.Agents.SingleAsync(a => a.Id == factory.AgentId);
            agent.Status = AgentStatus.Available;
            await db.SaveChangesAsync();
        }

        await AuthenticateAsync("phase10-agent");

        var request = new SimulateIncomingCallRequestDto
        {
            PhoneNumber = "8801712345678",
            CorrelationId = $"ACCEPT-FLOW-{Guid.NewGuid():N}"
        };

        // Incoming call triggers routing to agent -> Ringing
        var incomingResponse = await client.PostAsJsonAsync("/api/v1/telephony/incoming/simulate", request);
        Assert.Equal(HttpStatusCode.Created, incomingResponse.StatusCode);
        var incomingCall = await incomingResponse.Content.ReadFromJsonAsync<TelephonyCallResponseDto>();

        Assert.NotNull(incomingCall);
        Assert.Equal(CallStatus.Ringing, incomingCall.Status);
        Assert.Equal(factory.AgentId, incomingCall.AssignedAgentId);

        // Agent accepts the call
        var acceptResponse = await client.PostAsJsonAsync($"/api/v1/telephony/calls/{incomingCall.CallId}/accept", new { });
        Assert.Equal(HttpStatusCode.OK, acceptResponse.StatusCode);
        var acceptedCall = await acceptResponse.Content.ReadFromJsonAsync<TelephonyCallResponseDto>();

        Assert.NotNull(acceptedCall);
        Assert.Equal(CallStatus.Connected, acceptedCall.Status);

        // Verify in DB: call is Connected and agent became Busy
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var dbCall = await db.Calls.SingleAsync(c => c.Id == incomingCall.CallId);
            Assert.Equal(CallStatus.Connected, dbCall.Status);
            Assert.NotNull(dbCall.AnsweredAt);

            var dbAgent = await db.Agents.SingleAsync(a => a.Id == factory.AgentId);
            Assert.Equal(AgentStatus.Busy, dbAgent.Status);
        }

        // Agent ends the call
        var endResponse = await client.PostAsJsonAsync($"/api/v1/telephony/calls/{incomingCall.CallId}/end", new { });
        Assert.Equal(HttpStatusCode.OK, endResponse.StatusCode);
        var endedCall = await endResponse.Content.ReadFromJsonAsync<TelephonyCallResponseDto>();

        Assert.NotNull(endedCall);
        Assert.Equal(CallStatus.Completed, endedCall.Status);

        // Verify in DB: call is Completed and agent reverted to Available
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var dbCall = await db.Calls.SingleAsync(c => c.Id == incomingCall.CallId);
            Assert.Equal(CallStatus.Completed, dbCall.Status);
            Assert.NotNull(dbCall.EndedAt);

            var dbAgent = await db.Agents.SingleAsync(a => a.Id == factory.AgentId);
            Assert.Equal(AgentStatus.Available, dbAgent.Status);
        }
    }

    [Fact]
    public async Task Incoming_call_can_be_rejected_by_agent()
    {
        // Seed agent as Available
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var agent = await db.Agents.SingleAsync(a => a.Id == factory.AgentId);
            agent.Status = AgentStatus.Available;
            await db.SaveChangesAsync();
        }

        await AuthenticateAsync("phase10-agent");

        var request = new SimulateIncomingCallRequestDto
        {
            PhoneNumber = "8801712345678",
            CorrelationId = $"REJECT-FLOW-{Guid.NewGuid():N}"
        };

        var incomingResponse = await client.PostAsJsonAsync("/api/v1/telephony/incoming/simulate", request);
        Assert.Equal(HttpStatusCode.Created, incomingResponse.StatusCode);
        var incomingCall = await incomingResponse.Content.ReadFromJsonAsync<TelephonyCallResponseDto>();
        Assert.NotNull(incomingCall);

        // Agent rejects the call
        var rejectResponse = await client.PostAsJsonAsync($"/api/v1/telephony/calls/{incomingCall.CallId}/reject", new { });
        Assert.Equal(HttpStatusCode.OK, rejectResponse.StatusCode);
        var rejectedCall = await rejectResponse.Content.ReadFromJsonAsync<TelephonyCallResponseDto>();

        Assert.NotNull(rejectedCall);
        Assert.Equal(CallStatus.Rejected, rejectedCall.Status);

        // Verify in DB: call is Rejected and EndedAt is set
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var dbCall = await db.Calls.SingleAsync(c => c.Id == incomingCall.CallId);
            Assert.Equal(CallStatus.Rejected, dbCall.Status);
            Assert.NotNull(dbCall.EndedAt);
        }
    }

    [Fact]
    public async Task Incoming_call_with_unknown_customer_auto_provisions_customer_gracefully()
    {
        await AuthenticateAsync("phase10-admin");

        var unknownPhone = "8801999888777";
        var request = new SimulateIncomingCallRequestDto
        {
            PhoneNumber = unknownPhone,
            CorrelationId = $"UNKNOWN-CALLER-{Guid.NewGuid():N}"
        };

        var response = await client.PostAsJsonAsync("/api/v1/telephony/incoming/simulate", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var call = await response.Content.ReadFromJsonAsync<TelephonyCallResponseDto>();

        Assert.NotNull(call);
        Assert.NotNull(call.CustomerId);
        Assert.NotEqual(Guid.Empty, call.CustomerId.Value);

        // Verify in DB: new Customer record exists
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var newCust = await db.Customers.SingleOrDefaultAsync(c => c.Id == call.CustomerId.Value);
            Assert.NotNull(newCust);
            Assert.Equal(unknownPhone, newCust.PhoneNumber);
            Assert.Contains("Unknown Caller", newCust.DisplayName);
        }
    }
}
