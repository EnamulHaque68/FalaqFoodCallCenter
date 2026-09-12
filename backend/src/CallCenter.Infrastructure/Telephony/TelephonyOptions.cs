namespace CallCenter.Infrastructure.Telephony;

/// <summary>
/// Configuration options for telephony providers.
/// </summary>
public sealed class TelephonyOptions
{
    public const string SectionName = "Telephony";

    /// <summary>
    /// Active telephony provider name ("Simulated" or "Real"). Default is "Simulated".
    /// </summary>
    public string Provider { get; set; } = "Simulated";

    /// <summary>
    /// Base API endpoint for the real telephony provider gateway.
    /// </summary>
    public string? ApiEndpoint { get; set; }

    /// <summary>
    /// Authentication key or token for the real telephony provider.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Secret used to validate incoming webhooks from the telephony provider.
    /// </summary>
    public string? WebhookSecret { get; set; }

    /// <summary>
    /// Default caller ID used for outbound calls if none is explicitly specified.
    /// </summary>
    public string? DefaultCallerId { get; set; }

    /// <summary>
    /// Clock skew / freshness tolerance in seconds for webhook replay protection. Default is 300 (5 minutes).
    /// </summary>
    public int WebhookToleranceSeconds { get; set; } = 300;

    /// <summary>
    /// Whether incoming webhooks must strictly have a valid cryptographic signature. Default is true.
    /// </summary>
    public bool RequireWebhookSignature { get; set; } = true;

    /// <summary>
    /// Twilio Account SID when using Twilio Voice provider.
    /// </summary>
    public string? AccountSid { get; set; }

    /// <summary>
    /// Twilio Auth Token when using Twilio Voice provider.
    /// </summary>
    public string? AuthToken { get; set; }

    /// <summary>
    /// Twilio API Key SID (used for generating Twilio Voice Access Tokens).
    /// </summary>
    public string? ApiKeySid { get; set; }

    /// <summary>
    /// Twilio API Key Secret (used for signing Twilio Voice Access Tokens).
    /// </summary>
    public string? ApiKeySecret { get; set; }

    /// <summary>
    /// TwiML Application SID (connects Voice SDK to backend TwiML webhook).
    /// </summary>
    public string? TwimlAppSid { get; set; }

    /// <summary>
    /// Twilio Voice Phone Number (caller ID).
    /// </summary>
    public string? VoiceNumber { get; set; }

    /// <summary>
    /// Short-lived access token expiration for browser softphone in minutes. Default is 15 minutes.
    /// </summary>
    public int TokenTtlMinutes { get; set; } = 15;

    /// <summary>
    /// Publicly reachable base URL used by providers for webhook callbacks (e.g. status and recording callbacks).
    /// </summary>
    public string? CallbackBaseUrl { get; set; }

    /// <summary>
    /// Backward-compatible alias for Provider.
    /// </summary>
    public string ActiveProvider
    {
        get => Provider;
        set => Provider = value;
    }

    /// <summary>
    /// Backward-compatible alias for CallbackBaseUrl.
    /// </summary>
    public string? PublicBaseUrl
    {
        get => CallbackBaseUrl;
        set => CallbackBaseUrl = value;
    }

    /// <summary>
    /// Indicates whether the real provider has minimum required configuration.
    /// </summary>
    public bool IsRealProviderConfigured =>
        !string.IsNullOrWhiteSpace(ApiEndpoint) && !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>
    /// Indicates whether Twilio provider has minimum required credentials.
    /// </summary>
    public bool IsTwilioConfigured =>
        !string.IsNullOrWhiteSpace(AccountSid) &&
        (!string.IsNullOrWhiteSpace(AuthToken) || (!string.IsNullOrWhiteSpace(ApiKeySid) && !string.IsNullOrWhiteSpace(ApiKeySecret)));

    /// <summary>
    /// Indicates whether Twilio Voice Token generation is fully configured for browser calling.
    /// </summary>
    public bool IsTwilioTokenGenerationConfigured =>
        !string.IsNullOrWhiteSpace(AccountSid) &&
        !string.IsNullOrWhiteSpace(ApiKeySid) &&
        !string.IsNullOrWhiteSpace(ApiKeySecret) &&
        !string.IsNullOrWhiteSpace(TwimlAppSid);
}

