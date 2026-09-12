namespace CallCenter.Application.Telephony.Providers;

/// <summary>
/// Core vendor-neutral telephony provider interface.
/// Encapsulates lower-level telephony operations (SIP/PSTN/WebRTC signaling, media bridging, and recordings)
/// without coupling domain and application logic to any vendor-specific SDK.
/// </summary>
public interface ITelephonyProvider
{
    /// <summary>
    /// Unique identifier for the provider implementation (e.g., "Simulated", "Real", "Twilio", "Asterisk").
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Feature capabilities supported by this provider implementation.
    /// </summary>
    TelephonyProviderCapabilities Capabilities { get; }

    /// <summary>
    /// Initiates an outgoing call.
    /// </summary>
    Task<TelephonyProviderCallResult> DialAsync(
        TelephonyOutboundCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Signals acceptance / answering of an active ringing call.
    /// </summary>
    Task<TelephonyProviderResult> AcceptCallAsync(
        TelephonyAcceptCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rejects an incoming or ringing call.
    /// </summary>
    Task<TelephonyProviderResult> RejectCallAsync(
        TelephonyRejectCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Terminates / hangs up an active call session.
    /// </summary>
    Task<TelephonyProviderResult> EndCallAsync(
        TelephonyEndCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Places an active call session on hold (mutes/plays hold music).
    /// </summary>
    Task<TelephonyProviderResult> HoldCallAsync(
        TelephonyHoldCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes a call session currently on hold.
    /// </summary>
    Task<TelephonyProviderResult> ResumeCallAsync(
        TelephonyResumeCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Transfers a call to another target agent, queue, or external number.
    /// </summary>
    Task<TelephonyProviderTransferResult> TransferCallAsync(
        TelephonyTransferCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts recording the audio channel(s) for the specified call.
    /// </summary>
    Task<TelephonyProviderResult> StartRecordingAsync(
        TelephonyStartRecordingCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops recording the specified call session.
    /// </summary>
    Task<TelephonyProviderResult> StopRecordingAsync(
        TelephonyStopRecordingCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Parses and processes a recording webhook payload from the provider.
    /// </summary>
    Task<TelephonyRecordingEvent?> ProcessRecordingWebhookAsync(
        TelephonyWebhookPayload payload,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Parses and processes an inbound call webhook payload from the provider.
    /// </summary>
    Task<TelephonyInboundCallEvent?> ProcessInboundWebhookAsync(
        TelephonyWebhookPayload payload,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Parses and processes a call status update webhook payload from the provider.
    /// </summary>
    Task<TelephonyCallStatusEvent?> ProcessCallStatusWebhookAsync(
        TelephonyWebhookPayload payload,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates cryptographic signature and security parameters of a webhook payload.
    /// </summary>
    WebhookValidationResult ValidateWebhookSecurity(TelephonyWebhookPayload payload);

    /// <summary>
    /// Generates a browser client token (e.g. Twilio Voice SDK JWT) for WebRTC softphones.
    /// </summary>
    Task<string> GenerateClientTokenAsync(
        string identity,
        int ttlMinutes = 15,
        CancellationToken cancellationToken = default);
}
