namespace CallCenter.Application.Telephony.Providers;

/// <summary>
/// Factory for resolving active or named telephony provider instances.
/// Enables runtime switching and testing across multiple providers.
/// </summary>
public interface ITelephonyProviderFactory
{
    /// <summary>
    /// Gets the currently configured active telephony provider.
    /// </summary>
    ITelephonyProvider GetActiveProvider();

    /// <summary>
    /// Gets a specific telephony provider by name (e.g., "Simulated", "Real").
    /// </summary>
    ITelephonyProvider GetProvider(string providerName);

    /// <summary>
    /// Lists all registered provider names and their capabilities.
    /// </summary>
    IReadOnlyDictionary<string, TelephonyProviderCapabilities> GetAvailableProviders();
}
