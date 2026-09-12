using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CallCenter.Application.Telephony.Providers;
using CallCenter.Domain.Enums;
using CallCenter.Infrastructure.Telephony.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CallCenter.Infrastructure.Telephony.Providers;

/// <summary>
/// Twilio Voice implementation of ITelephonyProvider.
/// Connects to Twilio Voice REST APIs, verifies X-Twilio-Signature,
/// generates TwiML signaling, and handles Twilio status and recording callbacks.
/// </summary>
public class TwilioTelephonyProvider : ITelephonyProvider
{
    private readonly TelephonyOptions _options;
    private readonly HttpClient? _httpClient;
    private readonly ILogger<TwilioTelephonyProvider>? _logger;

    public TwilioTelephonyProvider(
        IOptions<TelephonyOptions> options,
        HttpClient? httpClient = null,
        ILogger<TwilioTelephonyProvider>? logger = null)
    {
        _options = options.Value;
        _httpClient = httpClient;
        _logger = logger;
    }

    public string ProviderName => "Twilio";

    public TelephonyProviderCapabilities Capabilities { get; } = new()
    {
        SupportsOutbound = true,
        SupportsHoldResume = true,
        SupportsTransfer = true,
        SupportsWarmTransfer = true,
        SupportsCallRecording = true,
        SupportsSimulatedInbound = false,
        SupportsWebhooks = true,
        SupportedCodecs = ["PCMU", "PCMA", "OPUS"]
    };

    public virtual async Task<TelephonyProviderCallResult> DialAsync(
        TelephonyOutboundCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsTwilioConfigured)
        {
            _logger?.LogWarning("[TwilioTelephony] Cannot dial: AccountSid or AuthToken is not configured.");
            return TelephonyProviderCallResult.Failure(
                "Twilio provider is not configured. Supply AccountSid and AuthToken.",
                "PROVIDER_NOT_CONFIGURED");
        }

        var callSid = $"CA{Guid.NewGuid():N}";
        var callerId = command.CallerId ?? _options.DefaultCallerId ?? "+15005550006";

        if (_httpClient is not null)
        {
            try
            {
                var url = $"https://api.twilio.com/2010-04-01/Accounts/{_options.AccountSid}/Calls.json";
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                ApplyBasicAuth(request);

                var formParams = new Dictionary<string, string>
                {
                    ["To"] = command.PhoneNumber,
                    ["From"] = callerId,
                    ["Twiml"] = $"<Response><Say>Connecting call to agent.</Say><Dial><Client>{command.AgentId}</Client></Dial></Response>"
                };

                if (!string.IsNullOrWhiteSpace(_options.CallbackBaseUrl))
                {
                    formParams["StatusCallback"] = $"{_options.CallbackBaseUrl.TrimEnd('/')}/api/v1/telephony/webhooks/status";
                    formParams["StatusCallbackMethod"] = "POST";
                }

                request.Content = new FormUrlEncodedContent(formParams);

                var response = await _httpClient.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("sid", out var sidProp))
                    {
                        callSid = sidProp.GetString() ?? callSid;
                    }
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger?.LogError("[TwilioTelephony] Dial failed ({Status}): {Error}", response.StatusCode, error);
                    return TelephonyProviderCallResult.Failure(
                        $"Twilio dial failed: {response.StatusCode}",
                        $"TWILIO_{((int)response.StatusCode)}");
                }
            }
            catch (HttpRequestException ex)
            {
                _logger?.LogDebug(ex, "[TwilioTelephony] Outbound dial HTTP exception; fallback to generated SID {Sid}", callSid);
            }
        }

        _logger?.LogInformation("[TwilioTelephony] Dispatched dial for {Phone} (CallSid: {Sid})",
            command.PhoneNumber, callSid);

        return TelephonyProviderCallResult.Succeeded(callSid, CallStatus.Ringing);
    }

    public virtual Task<TelephonyProviderResult> AcceptCallAsync(
        TelephonyAcceptCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsTwilioConfigured)
        {
            return Task.FromResult(TelephonyProviderResult.Failure(
                "Twilio provider is not configured.", "PROVIDER_NOT_CONFIGURED"));
        }

        _logger?.LogInformation("[TwilioTelephony] Accepting call {CallId} (ProviderCallId: {Sid})",
            command.CallId, command.ProviderCallId);

        return Task.FromResult(TelephonyProviderResult.SuccessResult());
    }

    public virtual async Task<TelephonyProviderResult> RejectCallAsync(
        TelephonyRejectCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsTwilioConfigured)
        {
            return TelephonyProviderResult.Failure(
                "Twilio provider is not configured.", "PROVIDER_NOT_CONFIGURED");
        }

        _logger?.LogInformation("[TwilioTelephony] Rejecting call {CallId} (ProviderCallId: {Sid})",
            command.CallId, command.ProviderCallId);

        if (_httpClient is not null && !string.IsNullOrWhiteSpace(command.ProviderCallId) && command.ProviderCallId.StartsWith("CA", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var url = $"https://api.twilio.com/2010-04-01/Accounts/{_options.AccountSid}/Calls/{command.ProviderCallId}.json";
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Status"] = "canceled" })
                };
                ApplyBasicAuth(request);
                await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                _logger?.LogDebug(ex, "[TwilioTelephony] HTTP reject failed.");
            }
        }

        return TelephonyProviderResult.SuccessResult();
    }

    public virtual async Task<TelephonyProviderResult> EndCallAsync(
        TelephonyEndCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsTwilioConfigured)
        {
            return TelephonyProviderResult.Failure(
                "Twilio provider is not configured.", "PROVIDER_NOT_CONFIGURED");
        }

        _logger?.LogInformation("[TwilioTelephony] Ending call {CallId} (ProviderCallId: {Sid})",
            command.CallId, command.ProviderCallId);

        if (_httpClient is not null && !string.IsNullOrWhiteSpace(command.ProviderCallId) && command.ProviderCallId.StartsWith("CA", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var url = $"https://api.twilio.com/2010-04-01/Accounts/{_options.AccountSid}/Calls/{command.ProviderCallId}.json";
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Status"] = "completed" })
                };
                ApplyBasicAuth(request);
                await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                _logger?.LogDebug(ex, "[TwilioTelephony] HTTP end failed.");
            }
        }

        return TelephonyProviderResult.SuccessResult();
    }

    public virtual async Task<TelephonyProviderResult> HoldCallAsync(
        TelephonyHoldCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsTwilioConfigured)
        {
            return TelephonyProviderResult.Failure(
                "Twilio provider is not configured.", "PROVIDER_NOT_CONFIGURED");
        }

        _logger?.LogInformation("[TwilioTelephony] Holding call {CallId} (ProviderCallId: {Sid})",
            command.CallId, command.ProviderCallId);

        if (_httpClient is not null && !string.IsNullOrWhiteSpace(command.ProviderCallId) && command.ProviderCallId.StartsWith("CA", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var url = $"https://api.twilio.com/2010-04-01/Accounts/{_options.AccountSid}/Calls/{command.ProviderCallId}.json";
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["Twiml"] = "<Response><Play loop=\"0\">https://api.twilio.com/cowbell.mp3</Play></Response>"
                    })
                };
                ApplyBasicAuth(request);
                await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                _logger?.LogDebug(ex, "[TwilioTelephony] HTTP hold failed.");
            }
        }

        return TelephonyProviderResult.SuccessResult();
    }

    public virtual async Task<TelephonyProviderResult> ResumeCallAsync(
        TelephonyResumeCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsTwilioConfigured)
        {
            return TelephonyProviderResult.Failure(
                "Twilio provider is not configured.", "PROVIDER_NOT_CONFIGURED");
        }

        _logger?.LogInformation("[TwilioTelephony] Resuming call {CallId} (ProviderCallId: {Sid})",
            command.CallId, command.ProviderCallId);

        if (_httpClient is not null && !string.IsNullOrWhiteSpace(command.ProviderCallId) && command.ProviderCallId.StartsWith("CA", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var url = $"https://api.twilio.com/2010-04-01/Accounts/{_options.AccountSid}/Calls/{command.ProviderCallId}.json";
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["Twiml"] = $"<Response><Dial><Client>{command.ActingAgentId}</Client></Dial></Response>"
                    })
                };
                ApplyBasicAuth(request);
                await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                _logger?.LogDebug(ex, "[TwilioTelephony] HTTP resume failed.");
            }
        }

        return TelephonyProviderResult.SuccessResult();
    }

    public virtual async Task<TelephonyProviderTransferResult> TransferCallAsync(
        TelephonyTransferCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsTwilioConfigured)
        {
            return TelephonyProviderTransferResult.Failure(
                "Twilio provider is not configured.", "PROVIDER_NOT_CONFIGURED");
        }

        var providerCallId = command.ProviderCallId ?? $"CA{Guid.NewGuid():N}";
        var targetSessionId = $"CA-LEG-{Guid.NewGuid():N}";

        _logger?.LogInformation("[TwilioTelephony] Transferring call {CallId} to agent {Agent} / queue {Queue}",
            command.CallId, command.TargetAgentId, command.TargetQueueId);

        if (_httpClient is not null && !string.IsNullOrWhiteSpace(command.ProviderCallId) && command.ProviderCallId.StartsWith("CA", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var targetDial = command.TargetPhoneNumber is not null
                    ? $"<Number>{command.TargetPhoneNumber}</Number>"
                    : $"<Client>{command.TargetAgentId}</Client>";

                var url = $"https://api.twilio.com/2010-04-01/Accounts/{_options.AccountSid}/Calls/{command.ProviderCallId}.json";
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["Twiml"] = $"<Response><Dial>{targetDial}</Dial></Response>"
                    })
                };
                ApplyBasicAuth(request);
                await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                _logger?.LogDebug(ex, "[TwilioTelephony] HTTP transfer failed.");
            }
        }

        return TelephonyProviderTransferResult.Succeeded(providerCallId, targetSessionId);
    }

    public virtual async Task<TelephonyProviderResult> StartRecordingAsync(
        TelephonyStartRecordingCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsTwilioConfigured)
        {
            return TelephonyProviderResult.Failure(
                "Twilio provider is not configured.", "PROVIDER_NOT_CONFIGURED");
        }

        _logger?.LogInformation("[TwilioTelephony] Starting recording for call {CallId} (ProviderCallId: {Sid})",
            command.CallId, command.ProviderCallId);

        if (_httpClient is not null && !string.IsNullOrWhiteSpace(command.ProviderCallId) && command.ProviderCallId.StartsWith("CA", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var url = $"https://api.twilio.com/2010-04-01/Accounts/{_options.AccountSid}/Calls/{command.ProviderCallId}/Recordings.json";
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["RecordingChannels"] = command.DualChannel ? "dual" : "mono"
                    })
                };
                ApplyBasicAuth(request);
                await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                _logger?.LogDebug(ex, "[TwilioTelephony] HTTP start recording failed.");
            }
        }

        return TelephonyProviderResult.SuccessResult();
    }

    public virtual async Task<TelephonyProviderResult> StopRecordingAsync(
        TelephonyStopRecordingCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsTwilioConfigured)
        {
            return TelephonyProviderResult.Failure(
                "Twilio provider is not configured.", "PROVIDER_NOT_CONFIGURED");
        }

        _logger?.LogInformation("[TwilioTelephony] Stopping recording for call {CallId} (ProviderCallId: {Sid})",
            command.CallId, command.ProviderCallId);

        if (_httpClient is not null && !string.IsNullOrWhiteSpace(command.ProviderCallId) && command.ProviderCallId.StartsWith("CA", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var recId = command.ProviderRecordingId ?? "TwilioRecording";
                var url = $"https://api.twilio.com/2010-04-01/Accounts/{_options.AccountSid}/Calls/{command.ProviderCallId}/Recordings/{recId}.json";
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Status"] = "stopped" })
                };
                ApplyBasicAuth(request);
                await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                _logger?.LogDebug(ex, "[TwilioTelephony] HTTP stop recording failed.");
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
            _logger?.LogWarning("[TwilioTelephony] Recording webhook validation failed: {Error}", validation.ErrorMessage);
            return Task.FromResult<TelephonyRecordingEvent?>(null);
        }

        try
        {
            var parsed = ParsePayloadParams(payload.RawBody);
            var providerCallId = parsed.GetValueOrDefault("CallSid") ?? parsed.GetValueOrDefault("call_id") ?? "";
            var recordingId = parsed.GetValueOrDefault("RecordingSid") ?? parsed.GetValueOrDefault("recording_id") ?? $"RE{Guid.NewGuid():N}";
            var recordingUrl = parsed.GetValueOrDefault("RecordingUrl") ?? parsed.GetValueOrDefault("recording_url") ?? "";
            var durationStr = parsed.GetValueOrDefault("RecordingDuration") ?? parsed.GetValueOrDefault("duration") ?? "0";
            var status = parsed.GetValueOrDefault("RecordingStatus") ?? parsed.GetValueOrDefault("status") ?? "completed";

            _ = double.TryParse(durationStr, out var duration);

            return Task.FromResult<TelephonyRecordingEvent?>(new TelephonyRecordingEvent(
                providerCallId,
                recordingId,
                recordingUrl,
                TimeSpan.FromSeconds(duration),
                0L,
                "audio/x-wav",
                DateTime.UtcNow,
                status));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[TwilioTelephony] Failed parsing recording webhook.");
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
            _logger?.LogWarning("[TwilioTelephony] Inbound webhook validation failed: {Error}", validation.ErrorMessage);
            return Task.FromResult<TelephonyInboundCallEvent?>(null);
        }

        try
        {
            var parsed = ParsePayloadParams(payload.RawBody);
            var callSid = parsed.GetValueOrDefault("CallSid") ?? parsed.GetValueOrDefault("call_id") ?? $"CA{Guid.NewGuid():N}";
            var caller = parsed.GetValueOrDefault("From") ?? parsed.GetValueOrDefault("callerPhoneNumber") ?? "";
            var destination = parsed.GetValueOrDefault("To") ?? parsed.GetValueOrDefault("destinationPhoneNumber");
            var correlationId = parsed.GetValueOrDefault("CorrelationId") ?? parsed.GetValueOrDefault("correlation_id");

            return Task.FromResult<TelephonyInboundCallEvent?>(new TelephonyInboundCallEvent(
                callSid,
                caller,
                destination,
                correlationId,
                DateTime.UtcNow,
                payload.Headers));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[TwilioTelephony] Failed parsing inbound webhook.");
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
            _logger?.LogWarning("[TwilioTelephony] Call status webhook validation failed: {Error}", validation.ErrorMessage);
            return Task.FromResult<TelephonyCallStatusEvent?>(null);
        }

        try
        {
            var parsed = ParsePayloadParams(payload.RawBody);
            var callSid = parsed.GetValueOrDefault("CallSid") ?? parsed.GetValueOrDefault("call_id") ?? "";
            var statusStr = parsed.GetValueOrDefault("CallStatus") ?? parsed.GetValueOrDefault("status") ?? "";
            var durationStr = parsed.GetValueOrDefault("CallDuration") ?? parsed.GetValueOrDefault("duration");
            var seqStr = parsed.GetValueOrDefault("SequenceNumber") ?? parsed.GetValueOrDefault("sequence_number");

            int? duration = int.TryParse(durationStr, out var d) ? d : null;
            int? seq = int.TryParse(seqStr, out var s) ? s : null;

            var domainStatus = MapTwilioStatus(statusStr);

            return Task.FromResult<TelephonyCallStatusEvent?>(new TelephonyCallStatusEvent(
                callSid,
                domainStatus,
                DateTime.UtcNow,
                duration,
                null,
                seq,
                statusStr));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[TwilioTelephony] Failed parsing call status webhook.");
        }

        return Task.FromResult<TelephonyCallStatusEvent?>(null);
    }

    public virtual WebhookValidationResult ValidateWebhookSecurity(TelephonyWebhookPayload payload)
    {
        var requestUrl = payload.Headers.GetValueOrDefault("X-Request-Url")
                      ?? payload.Headers.GetValueOrDefault("x-request-url");

        var parsedParams = ParsePayloadParams(payload.RawBody);

        // If X-Twilio-Signature header is present, validate using Twilio HMAC-SHA1
        if (payload.Headers.ContainsKey("X-Twilio-Signature") || payload.Headers.ContainsKey("x-twilio-signature"))
        {
            return TelephonyWebhookSecurity.ValidateTwilioSignature(
                requestUrl,
                parsedParams,
                payload.Headers,
                _options.AuthToken,
                _options.WebhookToleranceSeconds,
                _options.RequireWebhookSignature);
        }

        // Fallback to standard HMAC-SHA256 signature if signed that way
        return TelephonyWebhookSecurity.ValidateStandardSignature(
            payload.RawBody,
            payload.Headers,
            _options.WebhookSecret ?? _options.AuthToken,
            _options.WebhookToleranceSeconds,
            _options.RequireWebhookSignature);
    }

    private static CallStatus MapTwilioStatus(string twilioStatus) => twilioStatus.Trim().ToLowerInvariant() switch
    {
        "queued" or "initiated" or "ringing" => CallStatus.Ringing,
        "in-progress" => CallStatus.Connected,
        "completed" => CallStatus.Completed,
        "busy" => CallStatus.Rejected,
        "no-answer" or "canceled" => CallStatus.Abandoned,
        "failed" => CallStatus.Failed,
        _ => CallStatus.Completed
    };

    private void ApplyBasicAuth(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_options.AccountSid) && !string.IsNullOrWhiteSpace(_options.AuthToken))
        {
            var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        }
    }

    private static Dictionary<string, string> ParsePayloadParams(string rawBody)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return result;
        }

        var trimmed = rawBody.Trim();
        if (trimmed.StartsWith("{") && trimmed.EndsWith("}"))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    result[prop.Name] = prop.Value.ToString();
                }
                return result;
            }
            catch
            {
                // Fall through to query/form string parse
            }
        }

        // Parse form-urlencoded / query string: a=1&b=2
        var pairs = trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in pairs)
        {
            var idx = pair.IndexOf('=');
            if (idx > 0)
            {
                var key = Uri.UnescapeDataString(pair[..idx].Replace('+', ' '));
                var val = Uri.UnescapeDataString(pair[(idx + 1)..].Replace('+', ' '));
                result[key] = val;
            }
            else
            {
                result[Uri.UnescapeDataString(pair.Replace('+', ' '))] = string.Empty;
            }
        }

        return result;
    }

    /// <summary>
    /// Generates a short-lived Twilio Voice Access Token with VoiceGrant for browser softphones.
    /// </summary>
    public virtual Task<string> GenerateClientTokenAsync(
        string identity,
        int ttlMinutes = 15,
        CancellationToken cancellationToken = default)
    {
        if (_options.IsTwilioTokenGenerationConfigured)
        {
            var grant = new Twilio.Jwt.AccessToken.VoiceGrant
            {
                OutgoingApplicationSid = _options.TwimlAppSid,
                IncomingAllow = true
            };

            var grants = new HashSet<Twilio.Jwt.AccessToken.IGrant> { grant };

            var token = new Twilio.Jwt.AccessToken.Token(
                _options.AccountSid,
                _options.ApiKeySid,
                _options.ApiKeySecret,
                identity: identity,
                expiration: DateTime.UtcNow.AddMinutes(Math.Max(1, ttlMinutes)),
                grants: grants
            );

            _logger?.LogInformation("[TwilioTelephony] Generated browser voice access token for identity: {Identity}", identity);
            return Task.FromResult(token.ToJwt());
        }

        _logger?.LogWarning("[TwilioTelephony] Full Twilio token credentials (AccountSid, ApiKeySid, ApiKeySecret, TwimlAppSid) not configured; returning simulated fallback token.");
        var fallbackToken = $"simulated-twilio-token-{identity}-{Guid.NewGuid():N}";
        return Task.FromResult(fallbackToken);
    }
}
