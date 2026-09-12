using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
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
using CallCenter.Infrastructure.Telephony.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CallCenter.Tests;

public sealed class RealTelephonyIntegrationTests : IAsyncLifetime
{
    private const string TestWebhookSecret = "Phase22-Enterprise-Webhook-Secret-9999!";
    private const string TestTwilioAuthToken = "test-mock-twilio-auth-token-9999";
    private const string TestTwilioAccountSid = "test-mock-twilio-account-sid-9999";

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

        // Configure options in the DI container
        using var scope = factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<TelephonyOptions>>().Value;
        options.WebhookSecret = TestWebhookSecret;
        options.AccountSid = TestTwilioAccountSid;
        options.AuthToken = TestTwilioAuthToken;
        options.RequireWebhookSignature = true;
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

    // =========================================================================
    // 1. Webhook Security: HMAC-SHA256, Twilio HMAC-SHA1, Replay, Idempotency
    // =========================================================================

    [Fact]
    public void ValidateStandardSignature_ValidHmacSha256_Passes()
    {
        // Arrange
        var body = "{\"event\":\"test\",\"call_id\":\"REAL-1234\"}";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var payloadToSign = $"{timestamp}.{body}";
        var signature = TelephonyWebhookSecurity.ComputeSignatureHex(TestWebhookSecret, payloadToSign);

        var headers = new Dictionary<string, string>
        {
            ["X-Telephony-Signature"] = signature,
            ["X-Telephony-Timestamp"] = timestamp
        };

        // Act
        var result = TelephonyWebhookSecurity.ValidateStandardSignature(
            body, headers, TestWebhookSecret, toleranceSeconds: 300, requireSignature: true);

        // Assert
        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void ValidateStandardSignature_TamperedBody_Fails()
    {
        // Arrange
        var body = "{\"event\":\"test\",\"amount\":100}";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signature = TelephonyWebhookSecurity.ComputeSignatureHex(TestWebhookSecret, $"{timestamp}.{body}");

        var tamperedBody = "{\"event\":\"test\",\"amount\":999}";
        var headers = new Dictionary<string, string>
        {
            ["X-Telephony-Signature"] = signature,
            ["X-Telephony-Timestamp"] = timestamp
        };

        // Act
        var result = TelephonyWebhookSecurity.ValidateStandardSignature(
            tamperedBody, headers, TestWebhookSecret, toleranceSeconds: 300, requireSignature: true);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("INVALID_SIGNATURE", result.ErrorCode);
    }

    [Fact]
    public void ValidateStandardSignature_ExpiredTimestamp_TriggersReplayProtection()
    {
        // Arrange: timestamp 10 minutes (600s) in the past with 300s tolerance
        var body = "{\"event\":\"test\"}";
        var expiredTimestamp = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds().ToString();
        var signature = TelephonyWebhookSecurity.ComputeSignatureHex(TestWebhookSecret, $"{expiredTimestamp}.{body}");

        var headers = new Dictionary<string, string>
        {
            ["X-Telephony-Signature"] = signature,
            ["X-Telephony-Timestamp"] = expiredTimestamp
        };

        // Act
        var result = TelephonyWebhookSecurity.ValidateStandardSignature(
            body, headers, TestWebhookSecret, toleranceSeconds: 300, requireSignature: true);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("REPLAY_EXPIRED", result.ErrorCode);
    }

    [Fact]
    public void ValidateStandardSignature_DuplicateDelivery_RejectedByReplayFilter()
    {
        // Arrange
        var body = "{\"event\":\"single_use\",\"nonce\":\"abc-xyz\"}";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signature = TelephonyWebhookSecurity.ComputeSignatureHex(TestWebhookSecret, $"{timestamp}.{body}");

        var headers = new Dictionary<string, string>
        {
            ["X-Telephony-Signature"] = signature,
            ["X-Telephony-Timestamp"] = timestamp
        };

        // Act: First delivery succeeds
        var first = TelephonyWebhookSecurity.ValidateStandardSignature(
            body, headers, TestWebhookSecret, toleranceSeconds: 300, requireSignature: true);
        Assert.True(first.IsValid);

        // Act: Immediate duplicate delivery fails replay filter
        var second = TelephonyWebhookSecurity.ValidateStandardSignature(
            body, headers, TestWebhookSecret, toleranceSeconds: 300, requireSignature: true);
        Assert.False(second.IsValid);
        Assert.Equal("REPLAY_DUPLICATE", second.ErrorCode);
    }

    [Fact]
    public void ValidateTwilioSignature_ValidSignature_Passes()
    {
        // Arrange
        var url = "https://callcenter.falaqfood.com/api/v1/telephony/webhooks/inbound";
        var formParams = new Dictionary<string, string>
        {
            ["CallSid"] = "CA1234567890abcdef",
            ["From"] = "+8801700000000",
            ["To"] = "+8801800000000"
        };

        // Compute expected Twilio signature: URL + sorted params
        var sb = new StringBuilder(url);
        foreach (var kvp in formParams.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            sb.Append(kvp.Key).Append(kvp.Value);
        }
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(TestTwilioAuthToken));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
        var validTwilioSig = Convert.ToBase64String(hash);

        var headers = new Dictionary<string, string>
        {
            ["X-Twilio-Signature"] = validTwilioSig
        };

        // Act
        var result = TelephonyWebhookSecurity.ValidateTwilioSignature(
            url, formParams, headers, TestTwilioAuthToken, toleranceSeconds: 300, requireSignature: true);

        // Assert
        Assert.True(result.IsValid);
    }

    // =========================================================================
    // 2. Provider Unit Tests: TwilioTelephonyProvider & Capabilities
    // =========================================================================

    [Fact]
    public async Task TwilioTelephonyProvider_Capabilities_And_Outbound_Dial()
    {
        // Arrange
        var options = Options.Create(new TelephonyOptions
        {
            Provider = "Twilio",
            AccountSid = TestTwilioAccountSid,
            AuthToken = TestTwilioAuthToken,
            DefaultCallerId = "+8801799999999"
        });

        var provider = new TwilioTelephonyProvider(options);

        // Act & Assert
        Assert.Equal("Twilio", provider.ProviderName);
        Assert.True(provider.Capabilities.SupportsOutbound);
        Assert.True(provider.Capabilities.SupportsHoldResume);
        Assert.True(provider.Capabilities.SupportsTransfer);
        Assert.True(provider.Capabilities.SupportsCallRecording);

        var callId = Guid.NewGuid();
        var dial = await provider.DialAsync(new TelephonyOutboundCommand(
            callId, "8801711223344", null, Guid.NewGuid(), "corr-tw-01"));

        Assert.True(dial.Success);
        Assert.StartsWith("CA", dial.ProviderCallId);
        Assert.Equal(CallStatus.Ringing, dial.Status);

        var hold = await provider.HoldCallAsync(new TelephonyHoldCommand(callId, dial.ProviderCallId, null));
        Assert.True(hold.Success);

        var resume = await provider.ResumeCallAsync(new TelephonyResumeCommand(callId, dial.ProviderCallId, null));
        Assert.True(resume.Success);

        var transfer = await provider.TransferCallAsync(new TelephonyTransferCommand(
            callId, dial.ProviderCallId, TransferType.Blind, TargetPhoneNumber: "8801822334455"));
        Assert.True(transfer.Success);
        Assert.NotNull(transfer.TargetSessionId);

        var recStart = await provider.StartRecordingAsync(new TelephonyStartRecordingCommand(callId, dial.ProviderCallId, null));
        Assert.True(recStart.Success);

        var recStop = await provider.StopRecordingAsync(new TelephonyStopRecordingCommand(callId, dial.ProviderCallId, "REC-1", null));
        Assert.True(recStop.Success);

        var end = await provider.EndCallAsync(new TelephonyEndCommand(callId, dial.ProviderCallId, null));
        Assert.True(end.Success);
    }

    [Fact]
    public async Task TwilioTelephonyProvider_ProcessWebhooks_MapsPayloadsCorrectly()
    {
        // Arrange
        var options = Options.Create(new TelephonyOptions
        {
            Provider = "Twilio",
            AccountSid = TestTwilioAccountSid,
            AuthToken = TestTwilioAuthToken,
            RequireWebhookSignature = false
        });

        var provider = new TwilioTelephonyProvider(options);

        // 1. Inbound Webhook
        var inboundPayload = new TelephonyWebhookPayload(
            "Inbound",
            "CallSid=CA99998888&From=%2B8801712345678&To=%2B8801899999999",
            new Dictionary<string, string>());

        var inbEvent = await provider.ProcessInboundWebhookAsync(inboundPayload);
        Assert.NotNull(inbEvent);
        Assert.Equal("CA99998888", inbEvent.ProviderCallId);
        Assert.Equal("+8801712345678", inbEvent.CallerPhoneNumber);
        Assert.Equal("+8801899999999", inbEvent.DestinationPhoneNumber);

        // 2. Status Callback Webhook
        var statusPayload = new TelephonyWebhookPayload(
            "Status",
            "CallSid=CA99998888&CallStatus=in-progress&CallDuration=45&SequenceNumber=2",
            new Dictionary<string, string>());

        var statusEvent = await provider.ProcessCallStatusWebhookAsync(statusPayload);
        Assert.NotNull(statusEvent);
        Assert.Equal("CA99998888", statusEvent.ProviderCallId);
        Assert.Equal(CallStatus.Connected, statusEvent.Status);
        Assert.Equal(45, statusEvent.DurationSeconds);
        Assert.Equal(2, statusEvent.SequenceNumber);

        // 3. Recording Webhook
        var recPayload = new TelephonyWebhookPayload(
            "Recording",
            "CallSid=CA99998888&RecordingSid=RE12345678&RecordingUrl=https%3A%2F%2Fapi.twilio.com%2Frec.wav&RecordingDuration=120&RecordingStatus=completed",
            new Dictionary<string, string>());

        var recEvent = await provider.ProcessRecordingWebhookAsync(recPayload);
        Assert.NotNull(recEvent);
        Assert.Equal("CA99998888", recEvent.ProviderCallId);
        Assert.Equal("RE12345678", recEvent.ProviderRecordingId);
        Assert.Equal(120, recEvent.Duration.TotalSeconds);
    }

    [Fact]
    public async Task RealTelephonyProvider_WireMock_Dispatches_HttpRequests()
    {
        // Arrange: Custom HttpMessageHandler to verify wire-level HTTP requests
        var testHandler = new TestHttpMessageHandler();
        var httpClient = new HttpClient(testHandler);

        var options = Options.Create(new TelephonyOptions
        {
            Provider = "Real",
            ApiEndpoint = "https://telephony-gateway.falaqfood.local/api/v1",
            ApiKey = "secret-token-abc-123",
            WebhookSecret = "sig-secret"
        });

        var provider = new RealTelephonyProvider(options, httpClient);
        var callId = Guid.NewGuid();

        // Act: Dial
        var dial = await provider.DialAsync(new TelephonyOutboundCommand(
            callId, "8801700112233", null, Guid.NewGuid(), "corr-http-01"));

        // Assert
        Assert.True(dial.Success);
        Assert.NotNull(testHandler.LastRequest);
        Assert.Equal(HttpMethod.Post, testHandler.LastRequest.Method);
        Assert.Equal("https://telephony-gateway.falaqfood.local/api/v1/calls", testHandler.LastRequest.RequestUri?.ToString());
        Assert.Equal("Bearer", testHandler.LastRequest.Headers.Authorization?.Scheme);
        Assert.Equal("secret-token-abc-123", testHandler.LastRequest.Headers.Authorization?.Parameter);
    }

    // =========================================================================
    // 3. API Webhook Integration Tests (Inbound, Status, Outbound Idempotency)
    // =========================================================================

    [Fact]
    public async Task InboundWebhook_WithValidSignature_CreatesAndRoutesCall_Idempotently()
    {
        // Arrange
        var body = JsonSerializer.Serialize(new
        {
            call_id = $"REAL-{Guid.NewGuid():N}",
            from = "8801788776655",
            to = "8801900000001",
            correlation_id = $"corr-inb-{Guid.NewGuid():N}"
        });

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signature = TelephonyWebhookSecurity.ComputeSignatureHex(TestWebhookSecret, $"{timestamp}.{body}");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/telephony/webhooks/inbound")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Telephony-Signature", signature);
        request.Headers.Add("X-Telephony-Timestamp", timestamp);

        // Act: Send webhook
        var response = await client.SendAsync(request);

        // Assert: First arrival creates and routes the call
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var callDto = await response.Content.ReadFromJsonAsync<TelephonyCallResponseDto>();
        Assert.NotNull(callDto);
        Assert.Equal(CallDirection.Inbound, callDto.Direction);
        Assert.Equal("8801788776655", callDto.PhoneNumber);

        // Verify in database
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
        var dbCall = await db.Calls.FirstOrDefaultAsync(c => c.Id == callDto.CallId);
        Assert.NotNull(dbCall);
        Assert.Equal(CallDirection.Inbound, dbCall.Direction);

        // Act: Re-sending identical webhook is handled idempotently
        using var duplicateReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/telephony/webhooks/inbound")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        // Use fresh timestamp for second request to test application-level idempotency
        var freshTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var freshSig = TelephonyWebhookSecurity.ComputeSignatureHex(TestWebhookSecret, $"{freshTimestamp}.{body}");
        duplicateReq.Headers.Add("X-Telephony-Signature", freshSig);
        duplicateReq.Headers.Add("X-Telephony-Timestamp", freshTimestamp);

        var dupResponse = await client.SendAsync(duplicateReq);
        Assert.Equal(HttpStatusCode.OK, dupResponse.StatusCode);
        var dupCallDto = await dupResponse.Content.ReadFromJsonAsync<TelephonyCallResponseDto>();
        Assert.NotNull(dupCallDto);
        Assert.Equal(callDto.CallId, dupCallDto.CallId);
    }

    [Fact]
    public async Task InboundWebhook_WithInvalidSignature_Returns401Unauthorized()
    {
        // Arrange: Wrong secret producing invalid signature
        var body = "{\"call_id\":\"REAL-bad-sig\",\"from\":\"8801700000000\"}";
        var signature = TelephonyWebhookSecurity.ComputeSignatureHex("Wrong-Secret-12345", body);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/telephony/webhooks/inbound")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Telephony-Signature", signature);

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CallStatusWebhook_TransitionsCallStatus_AndUpdatesAgentAvailability()
    {
        // Arrange: Agent initiates call
        await AuthenticateAsync("phase10-agent");

        var outbound = await client.PostAsJsonAsync("/api/v1/telephony/outgoing", new InitiateOutboundCallRequestDto
        {
            PhoneNumber = "8801711229988",
            CorrelationId = $"corr-{Guid.NewGuid():N}",
            CustomerId = factory.CustomerId,
            AgentId = factory.AgentId
        });
        outbound.EnsureSuccessStatusCode();
        var call = (await outbound.Content.ReadFromJsonAsync<TelephonyCallResponseDto>())!;

        // 1. Send status webhook: in-progress (Connected)
        var connectedPayload = JsonSerializer.Serialize(new
        {
            providerCallId = call.ProviderCallId,
            status = "in-progress",
            durationSeconds = 0
        });
        var ts1 = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var sig1 = TelephonyWebhookSecurity.ComputeSignatureHex(TestWebhookSecret, $"{ts1}.{connectedPayload}");

        using var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/telephony/webhooks/status")
        {
            Content = new StringContent(connectedPayload, Encoding.UTF8, "application/json")
        };
        req1.Headers.Add("X-Telephony-Signature", sig1);
        req1.Headers.Add("X-Telephony-Timestamp", ts1);

        var res1 = await client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);

        // Verify call is connected and agent is Busy
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var updatedCall = await db.Calls.FirstAsync(c => c.Id == call.CallId);
            Assert.Equal(CallStatus.Connected, updatedCall.Status);
            Assert.NotNull(updatedCall.AnsweredAt);

            var agent = await db.Agents.FirstAsync(a => a.Id == factory.AgentId);
            Assert.Equal(AgentStatus.Busy, agent.Status);
        }

        // 2. Send status webhook: completed (Completed)
        var completedPayload = JsonSerializer.Serialize(new
        {
            providerCallId = call.ProviderCallId,
            status = "completed",
            durationSeconds = 125
        });
        var ts2 = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var sig2 = TelephonyWebhookSecurity.ComputeSignatureHex(TestWebhookSecret, $"{ts2}.{completedPayload}");

        using var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/telephony/webhooks/status")
        {
            Content = new StringContent(completedPayload, Encoding.UTF8, "application/json")
        };
        req2.Headers.Add("X-Telephony-Signature", sig2);
        req2.Headers.Add("X-Telephony-Timestamp", ts2);

        var res2 = await client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);

        // Verify call is completed and agent returned to Available
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var completedCall = await db.Calls.FirstAsync(c => c.Id == call.CallId);
            Assert.Equal(CallStatus.Completed, completedCall.Status);
            Assert.NotNull(completedCall.EndedAt);

            var agent = await db.Agents.FirstAsync(a => a.Id == factory.AgentId);
            Assert.Equal(AgentStatus.Available, agent.Status);
        }

        // 3. Send duplicate status callback: must be handled idempotently
        var ts3 = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var sig3 = TelephonyWebhookSecurity.ComputeSignatureHex(TestWebhookSecret, $"{ts3}.{completedPayload}");

        using var req3 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/telephony/webhooks/status")
        {
            Content = new StringContent(completedPayload, Encoding.UTF8, "application/json")
        };
        req3.Headers.Add("X-Telephony-Signature", sig3);
        req3.Headers.Add("X-Telephony-Timestamp", ts3);

        var res3 = await client.SendAsync(req3);
        Assert.Equal(HttpStatusCode.OK, res3.StatusCode);
    }

    [Fact]
    public async Task OutboundCall_WithIdempotencyKey_PreventsDuplicateCalls()
    {
        // Arrange
        await AuthenticateAsync("phase10-agent");

        var idempotencyKey = $"idemp-{Guid.NewGuid():N}";
        var requestDto = new InitiateOutboundCallRequestDto
        {
            PhoneNumber = "8801711223399",
            CorrelationId = $"corr-{Guid.NewGuid():N}",
            CustomerId = factory.CustomerId,
            AgentId = factory.AgentId,
            IdempotencyKey = idempotencyKey
        };

        // Act 1: First request creates call
        var firstResponse = await client.PostAsJsonAsync("/api/v1/telephony/outgoing", requestDto);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        var firstCall = await firstResponse.Content.ReadFromJsonAsync<TelephonyCallResponseDto>();
        Assert.NotNull(firstCall);

        // Act 2: Duplicate request with same IdempotencyKey returns identical call
        var secondRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/telephony/outgoing")
        {
            Content = JsonContent.Create(new InitiateOutboundCallRequestDto
            {
                PhoneNumber = "8801711223399",
                CorrelationId = $"corr-different-{Guid.NewGuid():N}",
                CustomerId = factory.CustomerId,
                AgentId = factory.AgentId
            })
        };
        secondRequest.Headers.Add("Idempotency-Key", idempotencyKey);

        var secondResponse = await client.SendAsync(secondRequest);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        var secondCall = await secondResponse.Content.ReadFromJsonAsync<TelephonyCallResponseDto>();
        Assert.NotNull(secondCall);

        // Assert: Identical CallId returned without duplicates
        Assert.Equal(firstCall.CallId, secondCall.CallId);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
        var totalCalls = await db.Calls.CountAsync(c => c.IdempotencyKey == idempotencyKey);
        Assert.Equal(1, totalCalls);
    }

    [Fact]
    public async Task CallHold_Resume_And_Transfer_FullLifecycle()
    {
        // Arrange: Initiate call
        await AuthenticateAsync("phase10-agent");

        var outbound = await client.PostAsJsonAsync("/api/v1/telephony/outgoing", new InitiateOutboundCallRequestDto
        {
            PhoneNumber = "8801711887766",
            CorrelationId = $"corr-{Guid.NewGuid():N}",
            CustomerId = factory.CustomerId,
            AgentId = factory.AgentId
        });
        outbound.EnsureSuccessStatusCode();
        var call = (await outbound.Content.ReadFromJsonAsync<TelephonyCallResponseDto>())!;

        // 1. Accept call
        var accept = await client.PostAsync($"/api/v1/telephony/calls/{call.CallId}/accept", null);
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);

        // 2. Hold call
        var hold = await client.PostAsync($"/api/v1/telephony/calls/{call.CallId}/hold", null);
        Assert.Equal(HttpStatusCode.OK, hold.StatusCode);
        var holdCall = await hold.Content.ReadFromJsonAsync<TelephonyCallResponseDto>();
        Assert.Equal(CallStatus.OnHold, holdCall!.Status);

        // 3. Resume call
        var resume = await client.PostAsync($"/api/v1/telephony/calls/{call.CallId}/resume", null);
        Assert.Equal(HttpStatusCode.OK, resume.StatusCode);
        var resumeCall = await resume.Content.ReadFromJsonAsync<TelephonyCallResponseDto>();
        Assert.Equal(CallStatus.Connected, resumeCall!.Status);

        // 4. End call
        var end = await client.PostAsync($"/api/v1/telephony/calls/{call.CallId}/end", null);
        Assert.Equal(HttpStatusCode.OK, end.StatusCode);
        var endCall = await end.Content.ReadFromJsonAsync<TelephonyCallResponseDto>();
        Assert.Equal(CallStatus.Completed, endCall!.Status);
    }

    [Fact]
    public async Task CallTransfer_ToQueue_Succeeds()
    {
        // Arrange
        await AuthenticateAsync("phase10-agent");

        var outbound = await client.PostAsJsonAsync("/api/v1/telephony/outgoing", new InitiateOutboundCallRequestDto
        {
            PhoneNumber = "8801711776655",
            CorrelationId = $"corr-{Guid.NewGuid():N}",
            CustomerId = factory.CustomerId,
            AgentId = factory.AgentId
        });
        outbound.EnsureSuccessStatusCode();
        var call = (await outbound.Content.ReadFromJsonAsync<TelephonyCallResponseDto>())!;

        var accept = await client.PostAsync($"/api/v1/telephony/calls/{call.CallId}/accept", null);
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);

        // Act: Transfer to Queue
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var queue = await db.CallQueues.FirstOrDefaultAsync();
            if (queue is null)
            {
                queue = new CallQueue { Id = Guid.NewGuid(), Name = "Support Queue", Priority = 1, IsActive = true, CreatedAt = DateTime.UtcNow };
                db.CallQueues.Add(queue);
                await db.SaveChangesAsync();
            }

            var transfer = await client.PostAsJsonAsync($"/api/v1/telephony/calls/{call.CallId}/transfer", new TransferCallRequestDto
            {
                TargetQueueId = queue.Id,
                TransferType = TransferType.Blind
            });
            Assert.Equal(HttpStatusCode.OK, transfer.StatusCode);

            // Assert: call is queued
            var transferred = await db.Calls.FirstAsync(c => c.Id == call.CallId);
            Assert.Equal(CallStatus.Queued, transferred.Status);
            Assert.Null(transferred.AssignedAgentId);
        }
    }
}

/// <summary>
/// Mock HttpMessageHandler for testing wire-level HTTP calls from RealTelephonyProvider.
/// </summary>
public sealed class TestHttpMessageHandler : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequest = request;
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"status\":\"success\",\"providerCallId\":\"REAL-MOCK-999\"}", Encoding.UTF8, "application/json")
        };
        return Task.FromResult(response);
    }
}
