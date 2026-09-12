using CallCenter.Domain.Enums;

namespace CallCenter.Application.Telephony.Providers;

/// <summary>
/// Defines feature capabilities supported by a telephony provider implementation.
/// </summary>
public sealed class TelephonyProviderCapabilities
{
    public bool SupportsOutbound { get; init; } = true;
    public bool SupportsHoldResume { get; init; } = true;
    public bool SupportsTransfer { get; init; } = true;
    public bool SupportsWarmTransfer { get; init; } = true;
    public bool SupportsCallRecording { get; init; } = true;
    public bool SupportsSimulatedInbound { get; init; } = true;
    public bool SupportsWebhooks { get; init; } = true;
    public IReadOnlyList<string> SupportedCodecs { get; init; } = ["PCMU", "PCMA", "OPUS"];
}

/// <summary>
/// General result returned by low-level telephony operations.
/// </summary>
public class TelephonyProviderResult
{
    public bool Success { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    public static TelephonyProviderResult SuccessResult() => new() { Success = true };

    public static TelephonyProviderResult Failure(string message, string? code = null) => new()
    {
        Success = false,
        ErrorMessage = message,
        ErrorCode = code ?? "TELEPHONY_ERROR"
    };
}

/// <summary>
/// Result returned by call initiation or state-changing operations that yield provider call identifiers.
/// </summary>
public sealed class TelephonyProviderCallResult : TelephonyProviderResult
{
    public string? ProviderCallId { get; init; }
    public CallStatus Status { get; init; }

    public static TelephonyProviderCallResult Succeeded(string providerCallId, CallStatus status) => new()
    {
        Success = true,
        ProviderCallId = providerCallId,
        Status = status
    };

    public static new TelephonyProviderCallResult Failure(string message, string? code = null) => new()
    {
        Success = false,
        ErrorMessage = message,
        ErrorCode = code ?? "TELEPHONY_CALL_ERROR"
    };
}

/// <summary>
/// Result returned when transferring a call via a telephony provider.
/// </summary>
public sealed class TelephonyProviderTransferResult : TelephonyProviderResult
{
    public string? ProviderCallId { get; init; }
    public string? TargetSessionId { get; init; }

    public static TelephonyProviderTransferResult Succeeded(string providerCallId, string? targetSessionId = null) => new()
    {
        Success = true,
        ProviderCallId = providerCallId,
        TargetSessionId = targetSessionId
    };

    public static new TelephonyProviderTransferResult Failure(string message, string? code = null) => new()
    {
        Success = false,
        ErrorMessage = message,
        ErrorCode = code ?? "TELEPHONY_TRANSFER_ERROR"
    };
}

/// <summary>
/// Command for initiating an outbound call on a telephony provider.
/// </summary>
public sealed record TelephonyOutboundCommand(
    Guid CallId,
    string PhoneNumber,
    string? CallerId,
    Guid AgentId,
    string CorrelationId,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>
/// Command for accepting an incoming or ringing call on a telephony provider.
/// </summary>
public sealed record TelephonyAcceptCommand(
    Guid CallId,
    string? ProviderCallId,
    Guid? ActingAgentId);

/// <summary>
/// Command for rejecting a call on a telephony provider.
/// </summary>
public sealed record TelephonyRejectCommand(
    Guid CallId,
    string? ProviderCallId,
    Guid? ActingAgentId,
    string? Reason = null);

/// <summary>
/// Command for ending a call on a telephony provider.
/// </summary>
public sealed record TelephonyEndCommand(
    Guid CallId,
    string? ProviderCallId,
    Guid? ActingAgentId,
    string? Reason = null);

/// <summary>
/// Command for placing an active call on hold.
/// </summary>
public sealed record TelephonyHoldCommand(
    Guid CallId,
    string? ProviderCallId,
    Guid? ActingAgentId);

/// <summary>
/// Command for resuming a call from hold.
/// </summary>
public sealed record TelephonyResumeCommand(
    Guid CallId,
    string? ProviderCallId,
    Guid? ActingAgentId);

/// <summary>
/// Command for transferring a call to an agent, queue, or external number.
/// </summary>
public sealed record TelephonyTransferCommand(
    Guid CallId,
    string? ProviderCallId,
    TransferType TransferType,
    Guid? TargetAgentId = null,
    string? TargetPhoneNumber = null,
    Guid? TargetQueueId = null,
    Guid? ActingAgentId = null,
    string? Reason = null);

/// <summary>
/// Command to begin recording an active call.
/// </summary>
public sealed record TelephonyStartRecordingCommand(
    Guid CallId,
    string? ProviderCallId,
    Guid? ActingAgentId,
    bool DualChannel = true);

/// <summary>
/// Command to stop recording an active call.
/// </summary>
public sealed record TelephonyStopRecordingCommand(
    Guid CallId,
    string? ProviderCallId,
    string? ProviderRecordingId,
    Guid? ActingAgentId);

/// <summary>
/// Event model for provider-generated incoming calls.
/// </summary>
public sealed record TelephonyInboundCallEvent(
    string ProviderCallId,
    string CallerPhoneNumber,
    string? DestinationPhoneNumber,
    string? CorrelationId,
    DateTime OccurredAt,
    IReadOnlyDictionary<string, string>? Headers = null);

/// <summary>
/// Event model for provider-generated recording completion or updates.
/// </summary>
public sealed record TelephonyRecordingEvent(
    string ProviderCallId,
    string ProviderRecordingId,
    string RecordingUrl,
    TimeSpan Duration,
    long FileSizeBytes,
    string ContentType,
    DateTime CreatedAt,
    string Status);

/// <summary>
/// Event model for provider-generated call status updates (e.g. ringing, connected, completed, failed).
/// </summary>
public sealed record TelephonyCallStatusEvent(
    string ProviderCallId,
    CallStatus Status,
    DateTime Timestamp,
    int? DurationSeconds = null,
    string? Reason = null,
    int? SequenceNumber = null,
    string? RawStatus = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>
/// Result of a webhook signature and security validation check.
/// </summary>
public sealed record WebhookValidationResult(
    bool IsValid,
    string? ErrorMessage = null,
    string? ErrorCode = null)
{
    public static WebhookValidationResult Success() => new(true);
    public static WebhookValidationResult Failure(string message, string? code = "INVALID_SIGNATURE") => new(false, message, code);
}

/// <summary>
/// Raw or standardized webhook payload received from a telephony provider.
/// </summary>
public sealed record TelephonyWebhookPayload(
    string EventType,
    string RawBody,
    IReadOnlyDictionary<string, string> Headers);

