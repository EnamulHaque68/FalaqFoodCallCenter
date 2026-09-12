using System.Collections.Concurrent;
using System.Text.Json;
using CallCenter.Application.Telephony.Providers;
using CallCenter.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CallCenter.Infrastructure.Telephony.Providers;

/// <summary>
/// Simulated in-memory telephony provider for local development and automated testing.
/// Provides deterministic execution, synthetic provider IDs, and realistic recording events.
/// </summary>
public sealed class SimulatedTelephonyProvider : ITelephonyProvider
{
    private readonly TelephonyOptions? _options;
    private readonly ILogger<SimulatedTelephonyProvider>? _logger;
    private readonly ConcurrentDictionary<Guid, SimulatedCallSession> _activeSessions = new();

    public SimulatedTelephonyProvider(
        IOptions<TelephonyOptions>? options = null,
        ILogger<SimulatedTelephonyProvider>? logger = null)
    {
        _options = options?.Value;
        _logger = logger;
    }

    public string ProviderName => "Simulated";

    public TelephonyProviderCapabilities Capabilities { get; } = new()
    {
        SupportsOutbound = true,
        SupportsHoldResume = true,
        SupportsTransfer = true,
        SupportsWarmTransfer = true,
        SupportsCallRecording = true,
        SupportsSimulatedInbound = true,
        SupportsWebhooks = true,
        SupportedCodecs = ["G.711u", "G.711a", "OPUS", "PCM"]
    };

    public Task<TelephonyProviderCallResult> DialAsync(
        TelephonyOutboundCommand command,
        CancellationToken cancellationToken = default)
    {
        var providerCallId = $"SIM-{command.CallId:N}";
        var session = new SimulatedCallSession
        {
            CallId = command.CallId,
            ProviderCallId = providerCallId,
            PhoneNumber = command.PhoneNumber,
            AgentId = command.AgentId,
            Status = CallStatus.Ringing,
            CreatedAt = DateTime.UtcNow
        };

        _activeSessions[command.CallId] = session;
        _logger?.LogInformation("[SimulatedTelephony] Dialed {Phone} for Call {CallId} (ProviderCallId: {ProviderCallId})",
            command.PhoneNumber, command.CallId, providerCallId);

        return Task.FromResult(TelephonyProviderCallResult.Succeeded(providerCallId, CallStatus.Ringing));
    }

    public Task<TelephonyProviderResult> AcceptCallAsync(
        TelephonyAcceptCommand command,
        CancellationToken cancellationToken = default)
    {
        if (_activeSessions.TryGetValue(command.CallId, out var session))
        {
            session.Status = CallStatus.Connected;
            session.AnsweredAt = DateTime.UtcNow;
        }

        _logger?.LogInformation("[SimulatedTelephony] Accepted Call {CallId}", command.CallId);
        return Task.FromResult(TelephonyProviderResult.SuccessResult());
    }

    public Task<TelephonyProviderResult> RejectCallAsync(
        TelephonyRejectCommand command,
        CancellationToken cancellationToken = default)
    {
        if (_activeSessions.TryGetValue(command.CallId, out var session))
        {
            session.Status = CallStatus.Rejected;
            session.EndedAt = DateTime.UtcNow;
        }

        _logger?.LogInformation("[SimulatedTelephony] Rejected Call {CallId}. Reason: {Reason}",
            command.CallId, command.Reason ?? "None");
        return Task.FromResult(TelephonyProviderResult.SuccessResult());
    }

    public Task<TelephonyProviderResult> EndCallAsync(
        TelephonyEndCommand command,
        CancellationToken cancellationToken = default)
    {
        if (_activeSessions.TryGetValue(command.CallId, out var session))
        {
            session.Status = CallStatus.Completed;
            session.EndedAt = DateTime.UtcNow;
        }

        _logger?.LogInformation("[SimulatedTelephony] Ended Call {CallId}. Reason: {Reason}",
            command.CallId, command.Reason ?? "Completed");
        return Task.FromResult(TelephonyProviderResult.SuccessResult());
    }

    public Task<TelephonyProviderResult> HoldCallAsync(
        TelephonyHoldCommand command,
        CancellationToken cancellationToken = default)
    {
        if (_activeSessions.TryGetValue(command.CallId, out var session))
        {
            session.Status = CallStatus.OnHold;
            session.LastHeldAt = DateTime.UtcNow;
        }

        _logger?.LogInformation("[SimulatedTelephony] Held Call {CallId}", command.CallId);
        return Task.FromResult(TelephonyProviderResult.SuccessResult());
    }

    public Task<TelephonyProviderResult> ResumeCallAsync(
        TelephonyResumeCommand command,
        CancellationToken cancellationToken = default)
    {
        if (_activeSessions.TryGetValue(command.CallId, out var session))
        {
            session.Status = CallStatus.Connected;
            session.LastResumedAt = DateTime.UtcNow;
        }

        _logger?.LogInformation("[SimulatedTelephony] Resumed Call {CallId}", command.CallId);
        return Task.FromResult(TelephonyProviderResult.SuccessResult());
    }

    public Task<TelephonyProviderTransferResult> TransferCallAsync(
        TelephonyTransferCommand command,
        CancellationToken cancellationToken = default)
    {
        var providerCallId = command.ProviderCallId ?? $"SIM-{command.CallId:N}";
        var targetSessionId = $"SIM-LEG-{(command.TargetAgentId ?? command.TargetQueueId ?? Guid.NewGuid()):N}";

        if (_activeSessions.TryGetValue(command.CallId, out var session))
        {
            session.Status = CallStatus.Ringing;
        }

        _logger?.LogInformation("[SimulatedTelephony] Transferred Call {CallId} ({Type}) to Agent {TargetAgent} / Queue {TargetQueue}",
            command.CallId, command.TransferType, command.TargetAgentId, command.TargetQueueId);

        return Task.FromResult(TelephonyProviderTransferResult.Succeeded(providerCallId, targetSessionId));
    }

    public Task<TelephonyProviderResult> StartRecordingAsync(
        TelephonyStartRecordingCommand command,
        CancellationToken cancellationToken = default)
    {
        var session = _activeSessions.GetOrAdd(command.CallId, id => new SimulatedCallSession
        {
            CallId = id,
            ProviderCallId = command.ProviderCallId ?? $"SIM-{id:N}",
            CreatedAt = DateTime.UtcNow
        });

        session.IsRecording = true;
        session.RecordingStartedAt = DateTime.UtcNow;
        session.ProviderRecordingId = $"SIM-REC-{command.CallId:N}";

        _logger?.LogInformation("[SimulatedTelephony] Recording started for Call {CallId} (RecordingId: {RecordingId})",
            command.CallId, session.ProviderRecordingId);

        return Task.FromResult(TelephonyProviderResult.SuccessResult());
    }

    public Task<TelephonyProviderResult> StopRecordingAsync(
        TelephonyStopRecordingCommand command,
        CancellationToken cancellationToken = default)
    {
        if (_activeSessions.TryGetValue(command.CallId, out var session))
        {
            session.IsRecording = false;
            session.RecordingEndedAt = DateTime.UtcNow;
        }

        _logger?.LogInformation("[SimulatedTelephony] Recording stopped for Call {CallId}", command.CallId);
        return Task.FromResult(TelephonyProviderResult.SuccessResult());
    }

    public Task<TelephonyRecordingEvent?> ProcessRecordingWebhookAsync(
        TelephonyWebhookPayload payload,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(payload.RawBody))
            {
                using var doc = JsonDocument.Parse(payload.RawBody);
                var root = doc.RootElement;

                var providerCallId = root.TryGetProperty("providerCallId", out var pCallId) ? pCallId.GetString() ?? "" : "";
                var providerRecordingId = root.TryGetProperty("providerRecordingId", out var pRecId) ? pRecId.GetString() ?? "" : $"SIM-REC-{Guid.NewGuid():N}";
                var recordingUrl = root.TryGetProperty("recordingUrl", out var rUrl) ? rUrl.GetString() ?? "" : $"https://recordings.falaqfood.local/{providerCallId}.mp3";
                var durationSeconds = root.TryGetProperty("durationSeconds", out var dur) ? dur.GetDouble() : 30.0;
                var fileSizeBytes = root.TryGetProperty("fileSizeBytes", out var size) ? size.GetInt64() : 1024L * 1024L;
                var contentType = root.TryGetProperty("contentType", out var cType) ? cType.GetString() ?? "audio/mpeg" : "audio/mpeg";
                var status = root.TryGetProperty("status", out var stat) ? stat.GetString() ?? "completed" : "completed";

                var ev = new TelephonyRecordingEvent(
                    providerCallId,
                    providerRecordingId,
                    recordingUrl,
                    TimeSpan.FromSeconds(durationSeconds),
                    fileSizeBytes,
                    contentType,
                    DateTime.UtcNow,
                    status);

                return Task.FromResult<TelephonyRecordingEvent?>(ev);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[SimulatedTelephony] Failed to parse recording webhook payload: {Raw}", payload.RawBody);
        }

        // Fallback synthetic event
        var fallback = new TelephonyRecordingEvent(
            $"SIM-{Guid.NewGuid():N}",
            $"SIM-REC-{Guid.NewGuid():N}",
            $"https://recordings.falaqfood.local/{Guid.NewGuid():N}.mp3",
            TimeSpan.FromSeconds(45),
            1440000,
            "audio/mpeg",
            DateTime.UtcNow,
            "completed");

        return Task.FromResult<TelephonyRecordingEvent?>(fallback);
    }

    public Task<TelephonyInboundCallEvent?> ProcessInboundWebhookAsync(
        TelephonyWebhookPayload payload,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(payload.RawBody))
            {
                var dict = ParsePayloadParams(payload.RawBody);

                var callerPhone = dict.GetValueOrDefault("callerPhoneNumber") ??
                                  dict.GetValueOrDefault("from") ??
                                  dict.GetValueOrDefault("phoneNumber") ?? "";
                var destPhone = dict.GetValueOrDefault("destinationPhoneNumber") ??
                                dict.GetValueOrDefault("to");
                var providerCallId = dict.GetValueOrDefault("providerCallId") ??
                                     dict.GetValueOrDefault("call_id") ??
                                     dict.GetValueOrDefault("callSid") ?? $"SIM-{Guid.NewGuid():N}";
                var correlationId = dict.GetValueOrDefault("correlationId") ??
                                    dict.GetValueOrDefault("correlation_id") ?? Guid.NewGuid().ToString("N");

                var ev = new TelephonyInboundCallEvent(
                    providerCallId,
                    callerPhone,
                    destPhone,
                    correlationId,
                    DateTime.UtcNow,
                    payload.Headers);

                return Task.FromResult<TelephonyInboundCallEvent?>(ev);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[SimulatedTelephony] Failed to parse inbound webhook payload: {Raw}", payload.RawBody);
        }

        return Task.FromResult<TelephonyInboundCallEvent?>(null);
    }

    public Task<TelephonyCallStatusEvent?> ProcessCallStatusWebhookAsync(
        TelephonyWebhookPayload payload,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(payload.RawBody))
            {
                var dict = ParsePayloadParams(payload.RawBody);

                var providerCallId = dict.GetValueOrDefault("providerCallId") ??
                                     dict.GetValueOrDefault("call_id") ??
                                     dict.GetValueOrDefault("callSid") ?? "";
                var statusStr = dict.GetValueOrDefault("status") ??
                                dict.GetValueOrDefault("callStatus") ?? "Completed";
                var durationStr = dict.GetValueOrDefault("durationSeconds") ??
                                  dict.GetValueOrDefault("duration");
                var duration = int.TryParse(durationStr, out var d) ? d : (int?)null;
                var reason = dict.GetValueOrDefault("reason");
                var seqStr = dict.GetValueOrDefault("sequenceNumber") ??
                             dict.GetValueOrDefault("sequence_number");
                var seq = int.TryParse(seqStr, out var s) ? s : (int?)null;

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
            _logger?.LogWarning(ex, "[SimulatedTelephony] Failed to parse call status webhook: {Raw}", payload.RawBody);
        }

        return Task.FromResult<TelephonyCallStatusEvent?>(null);
    }

    private static Dictionary<string, string> ParsePayloadParams(string rawBody)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return result;
        }

        var trimmed = rawBody.Trim();
        if (trimmed.StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    result[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString() ?? "",
                        JsonValueKind.Number => prop.Value.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        JsonValueKind.Null => "",
                        _ => prop.Value.GetRawText()
                    };
                }
                return result;
            }
            catch
            {
                // Fall through to query/form parsing
            }
        }

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

    public WebhookValidationResult ValidateWebhookSecurity(TelephonyWebhookPayload payload)
    {
        if (payload.Headers.TryGetValue("X-Simulated-Reject-Signature", out var reject) &&
            string.Equals(reject, "true", StringComparison.OrdinalIgnoreCase))
        {
            return WebhookValidationResult.Failure("Simulated signature rejection.", "SIMULATED_INVALID_SIGNATURE");
        }

        // If an HMAC or Twilio signature header is explicitly provided, validate it against the configured secret
        if (payload.Headers.ContainsKey("X-Telephony-Signature") || payload.Headers.ContainsKey("x-telephony-signature"))
        {
            return Security.TelephonyWebhookSecurity.ValidateStandardSignature(
                payload.RawBody,
                payload.Headers,
                _options?.WebhookSecret,
                _options?.WebhookToleranceSeconds ?? 300,
                requireSignature: _options?.RequireWebhookSignature ?? true);
        }

        return WebhookValidationResult.Success();
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
        _ => Enum.TryParse<CallStatus>(status, true, out var parsed) ? parsed : CallStatus.Completed
    };



    /// <summary>
    /// For testing/diagnostics: returns in-memory state of a call session.
    /// </summary>
    public SimulatedCallSession? GetSession(Guid callId) =>
        _activeSessions.TryGetValue(callId, out var session) ? session : null;

    /// <summary>
    /// Generates a simulated client token for browser softphones in development.
    /// </summary>
    public Task<string> GenerateClientTokenAsync(
        string identity,
        int ttlMinutes = 15,
        CancellationToken cancellationToken = default)
    {
        var token = $"sim-token-{identity}-{Guid.NewGuid():N}";
        return Task.FromResult(token);
    }

    /// <summary>
    /// For testing: clears active sessions.
    /// </summary>
    public void Reset() => _activeSessions.Clear();
}

/// <summary>
/// Internal in-memory representation of a simulated telephony call session.
/// </summary>
public sealed class SimulatedCallSession
{
    public Guid CallId { get; set; }
    public string ProviderCallId { get; set; } = null!;
    public string? PhoneNumber { get; set; }
    public Guid? AgentId { get; set; }
    public CallStatus Status { get; set; }
    public bool IsRecording { get; set; }
    public string? ProviderRecordingId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? AnsweredAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public DateTime? LastHeldAt { get; set; }
    public DateTime? LastResumedAt { get; set; }
    public DateTime? RecordingStartedAt { get; set; }
    public DateTime? RecordingEndedAt { get; set; }
}
