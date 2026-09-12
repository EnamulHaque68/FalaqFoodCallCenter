using CallCenter.Application.Telephony.Providers;

namespace CallCenter.Application.Telephony.DTOs;

/// <summary>
/// Information describing the active and available telephony providers.
/// </summary>
public sealed class TelephonyProviderInfoResponseDto
{
    public string ActiveProvider { get; init; } = null!;
    public TelephonyProviderCapabilities Capabilities { get; init; } = null!;
    public IReadOnlyDictionary<string, TelephonyProviderCapabilities> AvailableProviders { get; init; } =
        new Dictionary<string, TelephonyProviderCapabilities>();
}
