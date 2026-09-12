using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CallCenter.Application.Authentication.DTOs;
using CallCenter.Application.Calls.DTOs;
using CallCenter.Application.Telephony.DTOs;
using CallCenter.Application.Telephony.Providers;
using CallCenter.Domain.Entities;
using CallCenter.Domain.Enums;
using CallCenter.Infrastructure.Persistence;
using CallCenter.Infrastructure.Telephony;
using CallCenter.Infrastructure.Telephony.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CallCenter.Tests;

public sealed class TelephonyAbstractionTests : IAsyncLifetime
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
        var payload = await login.Content.ReadFromJsonAsync<LoginResponseDto>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", payload!.AccessToken);
    }

    [Fact]
    public async Task SimulatedTelephonyProvider_Capabilities_And_Full_Call_Lifecycle()
    {
        // Arrange
        var provider = new SimulatedTelephonyProvider();
        var callId = Guid.NewGuid();
        var agentId = Guid.NewGuid();

        // Act & Assert: Capabilities
        Assert.Equal("Simulated", provider.ProviderName);
        Assert.True(provider.Capabilities.SupportsOutbound);
        Assert.True(provider.Capabilities.SupportsHoldResume);
        Assert.True(provider.Capabilities.SupportsTransfer);
        Assert.True(provider.Capabilities.SupportsCallRecording);
        Assert.True(provider.Capabilities.SupportsSimulatedInbound);

        // 1. Dial
        var dial = await provider.DialAsync(new TelephonyOutboundCommand(
            callId, "8801700000001", null, agentId, "corr-001"));
        Assert.True(dial.Success);
        Assert.StartsWith("SIM-", dial.ProviderCallId);
        Assert.Equal(CallStatus.Ringing, dial.Status);

        // 2. Accept
        var accept = await provider.AcceptCallAsync(new TelephonyAcceptCommand(
            callId, dial.ProviderCallId, agentId));
        Assert.True(accept.Success);

        // 3. Hold
        var hold = await provider.HoldCallAsync(new TelephonyHoldCommand(
            callId, dial.ProviderCallId, agentId));
        Assert.True(hold.Success);

        // 4. Resume
        var resume = await provider.ResumeCallAsync(new TelephonyResumeCommand(
            callId, dial.ProviderCallId, agentId));
        Assert.True(resume.Success);

        // 5. Transfer
        var targetAgentId = Guid.NewGuid();
        var transfer = await provider.TransferCallAsync(new TelephonyTransferCommand(
            callId, dial.ProviderCallId, TransferType.Warm, TargetAgentId: targetAgentId));
        Assert.True(transfer.Success);
        Assert.NotNull(transfer.TargetSessionId);

        // 6. Recording Start / Stop
        var recStart = await provider.StartRecordingAsync(new TelephonyStartRecordingCommand(
            callId, dial.ProviderCallId, agentId));
        Assert.True(recStart.Success);

        var session = provider.GetSession(callId);
        Assert.NotNull(session);
        Assert.True(session.IsRecording);
        Assert.NotNull(session.ProviderRecordingId);

        var recStop = await provider.StopRecordingAsync(new TelephonyStopRecordingCommand(
            callId, dial.ProviderCallId, session.ProviderRecordingId, agentId));
        Assert.True(recStop.Success);
        Assert.False(session.IsRecording);

        // 7. End
        var end = await provider.EndCallAsync(new TelephonyEndCommand(
            callId, dial.ProviderCallId, agentId, "Customer finished"));
        Assert.True(end.Success);
        Assert.Equal(CallStatus.Completed, session.Status);
    }

    [Fact]
    public async Task SimulatedTelephonyProvider_ProcessRecordingWebhook_Parses_Json()
    {
        // Arrange
        var provider = new SimulatedTelephonyProvider();
        var rawJson = JsonSerializer.Serialize(new
        {
            providerCallId = "SIM-9999",
            providerRecordingId = "REC-12345",
            recordingUrl = "https://recordings.example.com/rec-12345.mp3",
            durationSeconds = 65.5,
            fileSizeBytes = 2048000L,
            contentType = "audio/mpeg",
            status = "completed"
        });

        var payload = new TelephonyWebhookPayload(
            "Recording",
            rawJson,
            new Dictionary<string, string>());

        // Act
        var result = await provider.ProcessRecordingWebhookAsync(payload);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("SIM-9999", result.ProviderCallId);
        Assert.Equal("REC-12345", result.ProviderRecordingId);
        Assert.Equal("https://recordings.example.com/rec-12345.mp3", result.RecordingUrl);
        Assert.Equal(65.5, result.Duration.TotalSeconds);
        Assert.Equal(2048000L, result.FileSizeBytes);
        Assert.Equal("completed", result.Status);
    }

    [Fact]
    public async Task RealTelephonyProvider_Unconfigured_Returns_Failure_And_Reports_Capabilities()
    {
        // Arrange: Unconfigured options
        var options = Options.Create(new TelephonyOptions
        {
            Provider = "Real",
            ApiEndpoint = null,
            ApiKey = null
        });

        var provider = new RealTelephonyProvider(options);

        // Act & Assert
        Assert.Equal("Real", provider.ProviderName);
        Assert.False(provider.Capabilities.SupportsSimulatedInbound);
        Assert.True(provider.Capabilities.SupportsCallRecording);

        var callId = Guid.NewGuid();
        var dial = await provider.DialAsync(new TelephonyOutboundCommand(
            callId, "8801700000002", null, Guid.NewGuid(), "corr-002"));

        Assert.False(dial.Success);
        Assert.Equal("PROVIDER_NOT_CONFIGURED", dial.ErrorCode);

        var hold = await provider.HoldCallAsync(new TelephonyHoldCommand(callId, "REAL-123", null));
        Assert.False(hold.Success);
        Assert.Equal("PROVIDER_NOT_CONFIGURED", hold.ErrorCode);
    }

    [Fact]
    public async Task RealTelephonyProvider_WhenConfigured_Executes_Operations()
    {
        // Arrange: Configured options
        var options = Options.Create(new TelephonyOptions
        {
            Provider = "Real",
            ApiEndpoint = "https://telephony-gateway.falaqfood.local/v1",
            ApiKey = "prod-secret-key-12345",
            WebhookSecret = "webhook-sig-secret"
        });

        var provider = new RealTelephonyProvider(options);
        var callId = Guid.NewGuid();

        // Act
        var dial = await provider.DialAsync(new TelephonyOutboundCommand(
            callId, "8801700000003", null, Guid.NewGuid(), "corr-003"));
        var transfer = await provider.TransferCallAsync(new TelephonyTransferCommand(
            callId, dial.ProviderCallId, TransferType.Blind, TargetAgentId: Guid.NewGuid()));
        var recording = await provider.StartRecordingAsync(new TelephonyStartRecordingCommand(
            callId, dial.ProviderCallId, null));

        // Assert
        Assert.True(dial.Success);
        Assert.StartsWith("REAL-", dial.ProviderCallId);
        Assert.True(transfer.Success);
        Assert.NotNull(transfer.TargetSessionId);
        Assert.True(recording.Success);
    }

    [Fact]
    public void TelephonyProviderFactory_Resolves_Simulated_And_Real_Providers()
    {
        // Arrange
        var options = Options.Create(new TelephonyOptions { Provider = "Simulated" });
        var simulated = new SimulatedTelephonyProvider();
        var real = new RealTelephonyProvider(options);

        var factory = new TelephonyProviderFactory([simulated, real], options);

        // Act & Assert
        var active = factory.GetActiveProvider();
        Assert.Equal("Simulated", active.ProviderName);

        var realResolved = factory.GetProvider("Real");
        Assert.Equal("Real", realResolved.ProviderName);

        var simResolved = factory.GetProvider("Simulated");
        Assert.Equal("Simulated", simResolved.ProviderName);

        var available = factory.GetAvailableProviders();
        Assert.True(available.ContainsKey("Simulated"));
        Assert.True(available.ContainsKey("Real"));
    }

    [Fact]
    public async Task TelephonyApi_OutboundCall_DelegatesToProvider_And_SetsProviderCallId()
    {
        // Arrange
        await AuthenticateAsync("phase10-agent");

        var request = new InitiateOutboundCallRequestDto
        {
            PhoneNumber = "8801711122233",
            CorrelationId = $"corr-{Guid.NewGuid():N}",
            CustomerId = factory.CustomerId,
            AgentId = factory.AgentId
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/telephony/outgoing", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var call = await response.Content.ReadFromJsonAsync<TelephonyCallResponseDto>();
        Assert.NotNull(call);
        Assert.NotEmpty(call.ProviderCallId);
        Assert.StartsWith("SIM-", call.ProviderCallId);
        Assert.Equal(CallStatus.Ringing, call.Status);
    }

    [Fact]
    public async Task TelephonyApi_CallRecording_Start_And_Stop_Flow()
    {
        // Arrange
        await AuthenticateAsync("phase10-agent");

        var outbound = await client.PostAsJsonAsync("/api/v1/telephony/outgoing", new InitiateOutboundCallRequestDto
        {
            PhoneNumber = "8801711122244",
            CorrelationId = $"corr-{Guid.NewGuid():N}",
            CustomerId = factory.CustomerId,
            AgentId = factory.AgentId
        });
        outbound.EnsureSuccessStatusCode();
        var call = (await outbound.Content.ReadFromJsonAsync<TelephonyCallResponseDto>())!;

        // Accept call to connect
        var accept = await client.PostAsync($"/api/v1/telephony/calls/{call.CallId}/accept", null);
        accept.EnsureSuccessStatusCode();

        // Act: Start Recording
        var startRec = await client.PostAsync($"/api/v1/telephony/calls/{call.CallId}/recording/start", null);
        Assert.Equal(HttpStatusCode.OK, startRec.StatusCode);
        var startResult = await startRec.Content.ReadFromJsonAsync<TelephonyProviderResult>();
        Assert.NotNull(startResult);
        Assert.True(startResult.Success);

        // Act: Stop Recording
        var stopRec = await client.PostAsync($"/api/v1/telephony/calls/{call.CallId}/recording/stop", null);
        Assert.Equal(HttpStatusCode.OK, stopRec.StatusCode);
        var stopResult = await stopRec.Content.ReadFromJsonAsync<TelephonyProviderResult>();
        Assert.NotNull(stopResult);
        Assert.True(stopResult.Success);

        // Verify database events
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
        var events = await db.CallEvents
            .Where(e => e.CallId == call.CallId)
            .Select(e => e.EventType)
            .ToListAsync();

        Assert.Contains("RecordingStarted", events);
        Assert.Contains("RecordingStopped", events);
    }

    [Fact]
    public async Task TelephonyApi_GetProvider_Returns_Active_And_Capabilities()
    {
        // Arrange
        await AuthenticateAsync("phase10-agent");

        // Act
        var response = await client.GetAsync("/api/v1/telephony/provider");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var info = await response.Content.ReadFromJsonAsync<TelephonyProviderInfoResponseDto>();
        Assert.NotNull(info);
        Assert.Equal("Simulated", info.ActiveProvider);
        Assert.NotNull(info.Capabilities);
        Assert.True(info.Capabilities.SupportsOutbound);
        Assert.True(info.Capabilities.SupportsCallRecording);
        Assert.True(info.AvailableProviders.ContainsKey("Simulated"));
        Assert.True(info.AvailableProviders.ContainsKey("Real"));
    }

    [Fact]
    public async Task TelephonyApi_RecordingWebhook_Persists_CallRecording()
    {
        // Arrange
        await AuthenticateAsync("phase10-agent");

        var outbound = await client.PostAsJsonAsync("/api/v1/telephony/outgoing", new InitiateOutboundCallRequestDto
        {
            PhoneNumber = "8801711122255",
            CorrelationId = $"corr-{Guid.NewGuid():N}",
            CustomerId = factory.CustomerId,
            AgentId = factory.AgentId
        });
        outbound.EnsureSuccessStatusCode();
        var call = (await outbound.Content.ReadFromJsonAsync<TelephonyCallResponseDto>())!;

        // Webhook payload referencing the call's ProviderCallId
        var webhookPayload = new
        {
            providerCallId = call.ProviderCallId,
            providerRecordingId = $"REC-{Guid.NewGuid():N}",
            recordingUrl = $"https://s3.amazonaws.com/recordings/{call.CallId}.mp3",
            durationSeconds = 120.5,
            fileSizeBytes = 4194304L,
            contentType = "audio/mpeg",
            status = "completed"
        };

        // Act: Anonymous webhook ingestion
        var clientNoAuth = factory.CreateClient();
        var webhookResponse = await clientNoAuth.PostAsJsonAsync(
            "/api/v1/telephony/webhooks/recording",
            webhookPayload);

        // Assert
        Assert.Equal(HttpStatusCode.OK, webhookResponse.StatusCode);

        // Verify recording is persisted in database
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
        var recording = await db.CallRecordings
            .FirstOrDefaultAsync(r => r.CallId == call.CallId);

        Assert.NotNull(recording);
        Assert.Equal(webhookPayload.recordingUrl, recording.StorageUrl);
        Assert.Equal(webhookPayload.providerRecordingId, recording.ProviderRecordingId);
        Assert.Equal(4194304L, recording.FileSizeBytes);
    }
}
