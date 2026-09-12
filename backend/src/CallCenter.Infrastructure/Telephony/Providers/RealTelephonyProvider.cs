using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CallCenter.Application.Telephony.Providers;
using CallCenter.Domain.Enums;
using CallCenter.Infrastructure.Telephony.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CallCenter.Infrastructure.Telephony.Providers;

/// <summary>
/// Vendor-neutral real telephony provider adapter.
/// Connects to real telephony backends (e.g. SIP PBX, WebRTC gateway, or cloud communications API)
/// over HTTP/REST with cryptographic signature validation and replay protection,
/// without coupling domain models to vendor-specific SDKs.
/// </summary>
public class RealTelephonyProvider : ITelephonyProvider
{
    private readonly TelephonyOptions _options;
    private readonly HttpClient? _httpClient;
    private readonly ILogger<RealTelephonyProvider>? _logger;

    public RealTelephonyProvider(
        IOptions<TelephonyOptions> options,
        HttpClient? httpClient = null,
        ILogger<RealTelephonyProvider>? logger = null)
    {
        _options = options.Value;
        _httpClient = httpClient;
        _logger = logger;
    }

    public string ProviderName => "Real";

    public TelephonyProviderCapabilities Capabilities { get; } = new()
    {
        SupportsOutbound = true,
        SupportsHoldResume = true,
        SupportsTransfer = true,
        SupportsWarmTransfer = true,
        SupportsCallRecording = true,
        SupportsSimulatedInbound = false,
        SupportsWebhooks = true,
        SupportedCodecs = ["G.711u", "G.711a", "OPUS"]
    };

    public virtual async Task<TelephonyProviderCallResult> DialAsync(
        TelephonyOutboundCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsRealProviderConfigured)
        {
            _logger?.LogWarning("[RealTelephony] Cannot dial: provider is not configured.");
            return TelephonyProviderCallResult.Failure(
                "Real telephony provider is not configured. Please supply ApiEndpoint and ApiKey.",
                "PROVIDER_NOT_CONFIGURED");
        }

        var externalCallId = $"REAL-{Guid.NewGuid():N}";

        if (_httpClient is not null && !string.IsNullOrWhiteSpace(_options.ApiEndpoint))
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, CombineUrl(_options.ApiEndpoint, "calls"))
                {
                    Content = JsonContent.Create(new
                    {
                        callId = command.CallId,
                        phoneNumber = command.PhoneNumber,
                        callerId = command.CallerId ?? _options.DefaultCallerId,
                        agentId = command.AgentId,
                        correlationId = command.CorrelationId,
                        metadata = command.Metadata
                    })
                };

                ApplyAuthentication(request);

                var response = await _httpClient.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(responseJson);
                    if (doc.RootElement.TryGetProperty("providerCallId", out var idProp) ||
                        doc.RootElement.TryGetProperty("callId", out idProp) ||
                        doc.RootElement.TryGetProperty("sid", out idProp))
                    {
                        externalCallId = idProp.GetString() ?? externalCallId;
                    }
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger?.LogError("[RealTelephony] Dial failed with status {Code}: {Error}", response.StatusCode, error);
                    return TelephonyProviderCallResult.Failure(
                        $"Telephony gateway rejected dial: {response.StatusCode}",
                        $"HTTP_{((int)response.StatusCode)}");
                }
            }
            catch (HttpRequestException ex) when (_options.ApiEndpoint.Contains(".local") || _options.ApiEndpoint.Contains("localhost"))
            {
                // In local/mock environments without a live gateway listening, log and use the generated external ID
                _logger?.LogDebug(ex, "[RealTelephony] Local gateway unavailable; using simulated real ID {Id}", externalCallId);
            }
        }

        _logger?.LogInformation("[RealTelephony] Dispatched dial command for {Phone} via endpoint {Endpoint}",
            command.PhoneNumber, _options.ApiEndpoint);

        return TelephonyProviderCallResult.Succeeded(externalCallId, CallStatus.Ringing);
    }

    public virtual async Task<TelephonyProviderResult> AcceptCallAsync(
        TelephonyAcceptCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsRealProviderConfigured)
        {
            return TelephonyProviderResult.Failure(
                "Real telephony provider is not configured.", "PROVIDER_NOT_CONFIGURED");
        }

        _logger?.LogInformation("[RealTelephony] Accepting call {CallId} (ProviderCallId: {ProviderCallId})",
            command.CallId, command.ProviderCallId);

        if (_httpClient is not null && !string.IsNullOrWhiteSpace(_options.ApiEndpoint))
        {
            try
            {
                var url = CombineUrl(_options.ApiEndpoint, $"calls/{command.ProviderCallId}/answer");
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = JsonContent.Create(new { agentId = command.ActingAgentId })
                };
                ApplyAuthentication(request);
                await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex) when (_options.ApiEndpoint.Contains(".local"))
            {
                _logger?.LogDebug(ex, "[RealTelephony] Local gateway offline for accept.");
            }
        }

        return TelephonyProviderResult.SuccessResult();
    }

    public virtual async Task<TelephonyProviderResult> RejectCallAsync(
        TelephonyRejectCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsRealProviderConfigured)
        {
            return TelephonyProviderResult.Failure(
                "Real telephony provider is not configured.", "PROVIDER_NOT_CONFIGURED");
        }

        _logger?.LogInformation("[RealTelephony] Rejecting call {CallId}. Reason: {Reason}",
            command.CallId, command.Reason);

        if (_httpClient is not null && !string.IsNullOrWhiteSpace(_options.ApiEndpoint))
        {
            try
            {
                var url = CombineUrl(_options.ApiEndpoint, $"calls/{command.ProviderCallId}/reject");
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = JsonContent.Create(new { reason = command.Reason })
                };
                ApplyAuthentication(request);
                await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex) when (_options.ApiEndpoint.Contains(".local"))
            {
                _logger?.LogDebug(ex, "[RealTelephony] Local gateway offline for reject.");
            }
        }

        return TelephonyProviderResult.SuccessResult();
    }

    public virtual async Task<TelephonyProviderResult> EndCallAsync(
        TelephonyEndCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsRealProviderConfigured)
        {
            return TelephonyProviderResult.Failure(
                "Real telephony provider is not configured.", "PROVIDER_NOT_CONFIGURED");
        }

        _logger?.LogInformation("[RealTelephony] Ending call {CallId} (ProviderCallId: {ProviderCallId})",
            command.CallId, command.ProviderCallId);

        if (_httpClient is not null && !string.IsNullOrWhiteSpace(_options.ApiEndpoint))
        {
            try
            {
                var url = CombineUrl(_options.ApiEndpoint, $"calls/{command.ProviderCallId}/end");
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = JsonContent.Create(new { reason = command.Reason })
                };
                ApplyAuthentication(request);
                await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex) when (_options.ApiEndpoint.Contains(".local"))
            {
                _logger?.LogDebug(ex, "[RealTelephony] Local gateway offline for end.");
            }
        }

        return TelephonyProviderResult.SuccessResult();
    }

    public virtual async Task<TelephonyProviderResult> HoldCallAsync(
        TelephonyHoldCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsRealProviderConfigured)
        {
            return TelephonyProviderResult.Failure(
                "Real telephony provider is not configured.", "PROVIDER_NOT_CONFIGURED");
        }

        _logger?.LogInformation("[RealTelephony] Holding call {CallId}", command.CallId);

        if (_httpClient is not null && !string.IsNullOrWhiteSpace(_options.ApiEndpoint))
        {
            try
            {
                var url = CombineUrl(_options.ApiEndpoint, $"calls/{command.ProviderCallId}/hold");
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                ApplyAuthentication(request);
                await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex) when (_options.ApiEndpoint.Contains(".local"))
            {
                _logger?.LogDebug(ex, "[RealTelephony] Local gateway offline for hold.");
            }
        }

        return TelephonyProviderResult.SuccessResult();
    }

    public virtual async Task<TelephonyProviderResult> ResumeCallAsync(
        TelephonyResumeCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsRealProviderConfigured)
        {
            return TelephonyProviderResult.Failure(
                "Real telephony provider is not configured.", "PROVIDER_NOT_CONFIGURED");
        }

        _logger?.LogInformation("[RealTelephony] Resuming call {CallId}", command.CallId);

        if (_httpClient is not null && !string.IsNullOrWhiteSpace(_options.ApiEndpoint))
        {
            try
            {
                var url = CombineUrl(_options.ApiEndpoint, $"calls/{command.ProviderCallId}/resume");
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                ApplyAuthentication(request);
                await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex) when (_options.ApiEndpoint.Contains(".local"))
            {
                _logger?.LogDebug(ex, "[RealTelephony] Local gateway offline for resume.");
            }
        }

        return TelephonyProviderResult.SuccessResult();
    }

    public virtual async Task<TelephonyProviderTransferResult> TransferCallAsync(
        TelephonyTransferCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsRealProviderConfigured)
        {
            return TelephonyProviderTransferResult.Failure(
                "Real telephony provider is not configured.", "PROVIDER_NOT_CONFIGURED");
        }

        var providerCallId = command.ProviderCallId ?? $"REAL-{command.CallId:N}";
        var targetSessionId = $"REAL-LEG-{Guid.NewGuid():N}";

        _logger?.LogInformation("[RealTelephony] Transferring call {CallId} to agent {Agent} / queue {Queue}",
            command.CallId, command.TargetAgentId, command.TargetQueueId);

        if (_httpClient is not null && !string.IsNullOrWhiteSpace(_options.ApiEndpoint))
        {
            try
            {
                var url = CombineUrl(_options.ApiEndpoint, $"calls/{providerCallId}/transfer");
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = JsonContent.Create(new
                    {
                        transferType = command.TransferType.ToString(),
                        targetAgentId = command.TargetAgentId,
                        targetQueueId = command.TargetQueueId,
                        targetPhoneNumber = command.TargetPhoneNumber
                    })
                };
                ApplyAuthentication(request);
                await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex) when (_options.ApiEndpoint.Contains(".local"))
            {
                _logger?.LogDebug(ex, "[RealTelephony] Local gateway offline for transfer.");
            }
        }

        return TelephonyProviderTransferResult.Succeeded(providerCallId, targetSessionId);
    }

    public virtual async Task<TelephonyProviderResult> StartRecordingAsync(
        TelephonyStartRecordingCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsRealProviderConfigured)
        {
            return TelephonyProviderResult.Failure(
                "Real telephony provider is not configured.", "PROVIDER_NOT_CONFIGURED");
        }

        _logger?.LogInformation("[RealTelephony] Starting recording for call {CallId}", command.CallId);

        if (_httpClient is not null && !string.IsNullOrWhiteSpace(_options.ApiEndpoint))
        {
            try
            {
                var url = CombineUrl(_options.ApiEndpoint, $"calls/{command.ProviderCallId}/recordings/start");
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = JsonContent.Create(new { dualChannel = command.DualChannel })
                };
                ApplyAuthentication(request);
                await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex) when (_options.ApiEndpoint.Contains(".local"))
            {
                _logger?.LogDebug(ex, "[RealTelephony] Local gateway offline for start recording.");
            }
        }

        return TelephonyProviderResult.SuccessResult();
    }

    public virtual async Task<TelephonyProviderResult> StopRecordingAsync(
        TelephonyStopRecordingCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsRealProviderConfigured)
        {
            return TelephonyProviderResult.Failure(
                "Real telephony provider is not configured.", "PROVIDER_NOT_CONFIGURED");
        }

        _logger?.LogInformation("[RealTelephony] Stopping recording for call {CallId}", command.CallId);

        if (_httpClient is not null && !string.IsNullOrWhiteSpace(_options.ApiEndpoint))
        {
            try
            {
                var url = CombineUrl(_options.ApiEndpoint, $"calls/{command.ProviderCallId}/recordings/stop");
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = JsonContent.Create(new { recordingId = command.ProviderRecordingId })
                };
                ApplyAuthentication(request);
                await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex) when (_options.ApiEndpoint.Contains(".local"))
            {
                _logger?.LogDebug(ex, "[RealTelephony] Local gateway offline for stop recording.");
            }
        }

        return TelephonyProviderResult.SuccessResult();
    }

    public virtual Task<TelephonyRecordingEvent?> ProcessRecordingWebhookAsync(
        TelephonyWebhookPayload payload,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateWebhookSecurity(payload);
        if (!validation.IsValid)
        {
            _logger?.LogWarning("[RealTelephony] Webhook security check failed: {Error}", validation.ErrorMessage);
            return Task.FromResult<TelephonyRecordingEvent?>(null);
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(payload.RawBody))
            {
                using var doc = JsonDocument.Parse(payload.RawBody);
                var root = doc.RootElement;

                var providerCallId = root.TryGetProperty("call_id", out var cId) ? cId.GetString() ?? "" :
                                     root.TryGetProperty("providerCallId", out var pcId) ? pcId.GetString() ?? "" : "";
                var recordingId = root.TryGetProperty("recording_id", out var rId) ? rId.GetString() ?? "" :
                                  root.TryGetProperty("providerRecordingId", out var prId) ? prId.GetString() ?? "" : $"REC-{Guid.NewGuid():N}";
                var recordingUrl = root.TryGetProperty("recording_url", out var u) ? u.GetString() ?? "" :
                                   root.TryGetProperty("recordingUrl", out var ru) ? ru.GetString() ?? "" : "";
                var duration = root.TryGetProperty("duration", out var d) ? d.GetDouble() :
                               root.TryGetProperty("durationSeconds", out var ds) ? ds.GetDouble() : 0.0;
                var size = root.TryGetProperty("file_size", out var s) ? s.GetInt64() :
                           root.TryGetProperty("fileSizeBytes", out var fsb) ? fsb.GetInt64() : 0L;
                var contentType = root.TryGetProperty("content_type", out var ct) ? ct.GetString() ?? "audio/mpeg" :
                                  root.TryGetProperty("contentType", out var ct2) ? ct2.GetString() ?? "audio/mpeg" : "audio/mpeg";
                var status = root.TryGetProperty("status", out var st) ? st.GetString() ?? "completed" : "completed";

                return Task.FromResult<TelephonyRecordingEvent?>(new TelephonyRecordingEvent(
                    providerCallId,
                    recordingId,
                    recordingUrl,
                    TimeSpan.FromSeconds(duration),
                    size,
                    contentType,
                    DateTime.UtcNow,
                    status));
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[RealTelephony] Failed parsing recording webhook.");
        }

        return Task.FromResult<TelephonyRecordingEvent?>(null);
    }

    public virtual Task<TelephonyInboundCallEvent?> ProcessInboundWebhookAsync(
        TelephonyWebhookPayload payload,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateWebhookSecurity(payload);
        if (!validation.IsValid)
        {
            _logger?.LogWarning("[RealTelephony] Inbound webhook security check failed: {Error}", validation.ErrorMessage);
            return Task.FromResult<TelephonyInboundCallEvent?>(null);
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(payload.RawBody))
            {
                using var doc = JsonDocument.Parse(payload.RawBody);
                var root = doc.RootElement;

                var providerCallId = root.TryGetProperty("call_id", out var cId) ? cId.GetString() ?? "" :
                                     root.TryGetProperty("providerCallId", out var pcId) ? pcId.GetString() ?? "" :
                                     root.TryGetProperty("callSid", out var cs) ? cs.GetString() ?? "" : $"REAL-{Guid.NewGuid():N}";
                var callerPhone = root.TryGetProperty("from", out var from) ? from.GetString() ?? "" :
                                  root.TryGetProperty("callerPhoneNumber", out var cPhone) ? cPhone.GetString() ?? "" :
                                  root.TryGetProperty("phoneNumber", out var pn) ? pn.GetString() ?? "" : "";
                var destPhone = root.TryGetProperty("to", out var to) ? to.GetString() :
                                root.TryGetProperty("destinationPhoneNumber", out var dPhone) ? dPhone.GetString() : null;
                var correlationId = root.TryGetProperty("correlation_id", out var corr) ? corr.GetString() :
                                    root.TryGetProperty("correlationId", out var corr2) ? corr2.GetString() : null;

                return Task.FromResult<TelephonyInboundCallEvent?>(new TelephonyInboundCallEvent(
                    providerCallId,
                    callerPhone,
                    destPhone,
                    correlationId,
                    DateTime.UtcNow,
                    payload.Headers));
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[RealTelephony] Failed parsing inbound webhook.");
        }

        return Task.FromResult<TelephonyInboundCallEvent?>(null);
    }

    public virtual Task<TelephonyCallStatusEvent?> ProcessCallStatusWebhookAsync(
        TelephonyWebhookPayload payload,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateWebhookSecurity(payload);
        if (!validation.IsValid)
        {
            _logger?.LogWarning("[RealTelephony] Call status webhook security check failed: {Error}", validation.ErrorMessage);
            return Task.FromResult<TelephonyCallStatusEvent?>(null);
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(payload.RawBody))
            {
                using var doc = JsonDocument.Parse(payload.RawBody);
                var root = doc.RootElement;

                var providerCallId = root.TryGetProperty("call_id", out var cId) ? cId.GetString() ?? "" :
                                     root.TryGetProperty("providerCallId", out var pcId) ? pcId.GetString() ?? "" :
                                     root.TryGetProperty("callSid", out var cs) ? cs.GetString() ?? "" : "";
                var statusStr = root.TryGetProperty("status", out var st) ? st.GetString() ?? "" :
                                root.TryGetProperty("callStatus", out var cSt) ? cSt.GetString() ?? "" : "";
                var duration = root.TryGetProperty("duration", out var d) ? (int?)d.GetInt32() :
                               root.TryGetProperty("durationSeconds", out var ds) ? (int?)ds.GetInt32() : null;
                var reason = root.TryGetProperty("reason", out var r) ? r.GetString() : null;
                var seq = root.TryGetProperty("sequence_number", out var s) ? (int?)s.GetInt32() :
                          root.TryGetProperty("sequenceNumber", out var sn) ? (int?)sn.GetInt32() : null;

                var domainStatus = MapProviderStatus(statusStr);

                return Task.FromResult<TelephonyCallStatusEvent?>(new TelephonyCallStatusEvent(
                    providerCallId,
                    domainStatus,
                    DateTime.UtcNow,
                    duration,
                    reason,
                    seq,
                    statusStr));
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[RealTelephony] Failed parsing call status webhook.");
        }

        return Task.FromResult<TelephonyCallStatusEvent?>(null);
    }

    public virtual WebhookValidationResult ValidateWebhookSecurity(TelephonyWebhookPayload payload)
    {
        return TelephonyWebhookSecurity.ValidateStandardSignature(
            payload.RawBody,
            payload.Headers,
            _options.WebhookSecret,
            _options.WebhookToleranceSeconds,
            _options.RequireWebhookSignature);
    }

    private static CallStatus MapProviderStatus(string status) => status.Trim().ToLowerInvariant() switch
    {
        "ringing" or "initiated" or "queued" => CallStatus.Ringing,
        "in-progress" or "in_progress" or "connected" or "answered" => CallStatus.Connected,
        "on-hold" or "on_hold" or "hold" => CallStatus.OnHold,
        "completed" or "hangup" or "finished" => CallStatus.Completed,
        "busy" => CallStatus.Rejected,
        "no-answer" or "no_answer" or "canceled" or "cancelled" => CallStatus.Abandoned,
        "failed" or "error" => CallStatus.Failed,
        _ => CallStatus.Completed
    };

    private void ApplyAuthentication(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
            request.Headers.TryAddWithoutValidation("X-API-Key", _options.ApiKey);
        }
    }

    private static string CombineUrl(string baseUrl, string path) =>
        $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";

    public virtual Task<string> GenerateClientTokenAsync(
        string identity,
        int ttlMinutes = 15,
        CancellationToken cancellationToken = default)
    {
        var token = $"real-token-{identity}-{Guid.NewGuid():N}";
        return Task.FromResult(token);
    }
}
