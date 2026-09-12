using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using CallCenter.Application.Telephony.Providers;

namespace CallCenter.Infrastructure.Telephony.Security;

/// <summary>
/// Cryptographic security utilities for verifying telephony provider webhooks,
/// preventing timing attacks, and enforcing replay attack protection.
/// </summary>
public static class TelephonyWebhookSecurity
{
    private static readonly ConcurrentDictionary<string, DateTime> SeenWebhooks = new();

    /// <summary>
    /// Verifies standard HMAC-SHA256 webhook signatures with timestamp-based replay protection.
    /// Supports both hex and base64 encoded signatures.
    /// </summary>
    public static WebhookValidationResult ValidateStandardSignature(
        string rawBody,
        IReadOnlyDictionary<string, string> headers,
        string? secret,
        int toleranceSeconds = 300,
        bool requireSignature = true)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return requireSignature
                ? WebhookValidationResult.Failure("Webhook secret is not configured.", "MISSING_SECRET")
                : WebhookValidationResult.Success();
        }

        // Try extracting signature from headers
        var signature = GetHeaderValue(headers, "X-Telephony-Signature")
                     ?? GetHeaderValue(headers, "X-Signature")
                     ?? GetHeaderValue(headers, "X-Hub-Signature-256");

        if (string.IsNullOrWhiteSpace(signature))
        {
            return requireSignature
                ? WebhookValidationResult.Failure("Missing X-Telephony-Signature header.", "MISSING_SIGNATURE")
                : WebhookValidationResult.Success();
        }

        // Strip prefixes if present (e.g. "sha256=", "t=...")
        var cleanSignature = signature.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)
            ? signature[7..]
            : signature;

        // Check timestamp for replay protection
        var timestampHeader = GetHeaderValue(headers, "X-Telephony-Timestamp")
                           ?? GetHeaderValue(headers, "X-Timestamp");

        var payloadToSign = rawBody;

        if (!string.IsNullOrWhiteSpace(timestampHeader))
        {
            if (long.TryParse(timestampHeader, out var epochSeconds))
            {
                var requestTime = DateTimeOffset.FromUnixTimeSeconds(epochSeconds).UtcDateTime;
                var age = Math.Abs((DateTime.UtcNow - requestTime).TotalSeconds);
                if (age > toleranceSeconds)
                {
                    return WebhookValidationResult.Failure(
                        $"Webhook timestamp expired or in future (skew: {age:F0}s, tolerance: {toleranceSeconds}s).",
                        "REPLAY_EXPIRED");
                }
            }
            else if (DateTime.TryParse(timestampHeader, out var parsedDate))
            {
                var age = Math.Abs((DateTime.UtcNow - parsedDate.ToUniversalTime()).TotalSeconds);
                if (age > toleranceSeconds)
                {
                    return WebhookValidationResult.Failure(
                        $"Webhook timestamp expired or in future (skew: {age:F0}s, tolerance: {toleranceSeconds}s).",
                        "REPLAY_EXPIRED");
                }
            }

            // Check if signed with timestamp prefix: timestamp.rawBody
            payloadToSign = $"{timestampHeader}.{rawBody}";
        }

        // Compute HMAC-SHA256
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        using var hmac = new HMACSHA256(keyBytes);

        // Try matching with timestamp prefix first, then fallback to rawBody alone
        if (VerifyHash(hmac, payloadToSign, cleanSignature) || VerifyHash(hmac, rawBody, cleanSignature))
        {
            // Replay cache check
            var seenKey = $"{cleanSignature}:{timestampHeader}";
            var now = DateTime.UtcNow;
            PruneSeenWebhooks(now, toleranceSeconds);

            if (!SeenWebhooks.TryAdd(seenKey, now))
            {
                return WebhookValidationResult.Failure("Duplicate webhook delivery detected.", "REPLAY_DUPLICATE");
            }

            return WebhookValidationResult.Success();
        }

        return WebhookValidationResult.Failure("Webhook signature verification failed.", "INVALID_SIGNATURE");
    }

    /// <summary>
    /// Verifies Twilio-formatted webhook signatures (HMAC-SHA1 over URL + sorted parameters).
    /// </summary>
    public static WebhookValidationResult ValidateTwilioSignature(
        string? requestUrl,
        IReadOnlyDictionary<string, string> parameters,
        IReadOnlyDictionary<string, string> headers,
        string? authToken,
        int toleranceSeconds = 300,
        bool requireSignature = true)
    {
        if (string.IsNullOrWhiteSpace(authToken))
        {
            return requireSignature
                ? WebhookValidationResult.Failure("Twilio AuthToken is not configured.", "MISSING_SECRET")
                : WebhookValidationResult.Success();
        }

        var signature = GetHeaderValue(headers, "X-Twilio-Signature");
        if (string.IsNullOrWhiteSpace(signature))
        {
            return requireSignature
                ? WebhookValidationResult.Failure("Missing X-Twilio-Signature header.", "MISSING_SIGNATURE")
                : WebhookValidationResult.Success();
        }

        // Build string to sign: URL + sorted parameter keys and values
        var sb = new StringBuilder(requestUrl ?? string.Empty);
        foreach (var kvp in parameters.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            sb.Append(kvp.Key).Append(kvp.Value);
        }

        var toSign = sb.ToString();
        var keyBytes = Encoding.UTF8.GetBytes(authToken);
        using var hmac = new HMACSHA1(keyBytes);
        var computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(toSign));
        var expectedBase64 = Convert.ToBase64String(computedHash);

        var expectedBytes = Encoding.UTF8.GetBytes(expectedBase64);
        var actualBytes = Encoding.UTF8.GetBytes(signature.Trim());

        if (CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes))
        {
            return WebhookValidationResult.Success();
        }

        return WebhookValidationResult.Failure("Twilio signature verification failed.", "INVALID_SIGNATURE");
    }

    /// <summary>
    /// Computes a standard HMAC-SHA256 signature for outgoing webhook generation or testing.
    /// </summary>
    public static string ComputeSignatureHex(string secret, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Computes a standard HMAC-SHA256 signature in Base64 format.
    /// </summary>
    public static string ComputeSignatureBase64(string secret, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToBase64String(hash);
    }

    private static bool VerifyHash(HMACSHA256 hmac, string payload, string expectedSignature)
    {
        var computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var computedHex = Convert.ToHexString(computedHash).ToLowerInvariant();
        var computedBase64 = Convert.ToBase64String(computedHash);

        var expectedClean = expectedSignature.Trim();

        var expectedBytes = Encoding.UTF8.GetBytes(expectedClean.ToLowerInvariant());
        var hexBytes = Encoding.UTF8.GetBytes(computedHex);

        if (expectedBytes.Length == hexBytes.Length && CryptographicOperations.FixedTimeEquals(expectedBytes, hexBytes))
        {
            return true;
        }

        var base64Bytes = Encoding.UTF8.GetBytes(computedBase64);
        var expectedRawBytes = Encoding.UTF8.GetBytes(expectedClean);

        return expectedRawBytes.Length == base64Bytes.Length && CryptographicOperations.FixedTimeEquals(expectedRawBytes, base64Bytes);
    }

    private static string? GetHeaderValue(IReadOnlyDictionary<string, string> headers, string key)
    {
        if (headers.TryGetValue(key, out var val))
        {
            return val;
        }

        var match = headers.FirstOrDefault(h => string.Equals(h.Key, key, StringComparison.OrdinalIgnoreCase));
        return match.Value;
    }

    private static void PruneSeenWebhooks(DateTime now, int toleranceSeconds)
    {
        var cutoff = now.AddSeconds(-toleranceSeconds * 2);
        foreach (var (k, v) in SeenWebhooks)
        {
            if (v < cutoff)
            {
                SeenWebhooks.TryRemove(k, out _);
            }
        }
    }
}
