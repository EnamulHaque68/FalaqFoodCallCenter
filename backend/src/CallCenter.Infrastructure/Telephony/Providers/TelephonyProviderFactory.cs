using CallCenter.Application.Telephony.Providers;
using Microsoft.Extensions.Options;

namespace CallCenter.Infrastructure.Telephony.Providers;

/// <summary>
/// Default implementation of ITelephonyProviderFactory.
/// Resolves providers registered in DI by name or active configuration.
/// </summary>
public sealed class TelephonyProviderFactory : ITelephonyProviderFactory
{
    private readonly IEnumerable<ITelephonyProvider> _providers;
    private readonly TelephonyOptions _options;

    public TelephonyProviderFactory(
        IEnumerable<ITelephonyProvider> providers,
        IOptions<TelephonyOptions> options)
    {
        _providers = providers;
        _options = options.Value;
    }

    public ITelephonyProvider GetActiveProvider()
    {
        var targetName = string.IsNullOrWhiteSpace(_options.Provider)
            ? "Simulated"
            : _options.Provider.Trim();

        return _providers.FirstOrDefault(p =>
            string.Equals(p.ProviderName, targetName, StringComparison.OrdinalIgnoreCase))
            ?? _providers.FirstOrDefault(p =>
                string.Equals(p.ProviderName, "Simulated", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"No telephony provider registered matching '{targetName}'.");
    }

    public ITelephonyProvider GetProvider(string providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            throw new ArgumentException("Provider name is required.", nameof(providerName));
        }

        return _providers.FirstOrDefault(p =>
            string.Equals(p.ProviderName, providerName.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Telephony provider '{providerName}' was not found.");
    }

    public IReadOnlyDictionary<string, TelephonyProviderCapabilities> GetAvailableProviders()
    {
        return _providers.ToDictionary(
            p => p.ProviderName,
            p => p.Capabilities,
            StringComparer.OrdinalIgnoreCase);
    }
}
