using System.Security.Cryptography;
using System.Text;
using CallCenter.Infrastructure.Authentication;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace CallCenter.Infrastructure.Recordings.Security;

public sealed class RecordingPlaybackTokenGenerator
{
    private readonly byte[] _keyBytes;
    private readonly int _expirationSeconds;

    public RecordingPlaybackTokenGenerator(
        IOptions<RecordingStorageOptions> storageOptions,
        IOptions<JwtOptions> jwtOptions)
    {
        var secret = !string.IsNullOrWhiteSpace(storageOptions.Value.TokenSecretKey)
            ? storageOptions.Value.TokenSecretKey
            : jwtOptions.Value.SecretKey;

        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
        {
            throw new InvalidOperationException("Recording playback token secret must be at least 32 characters.");
        }

        _keyBytes = Encoding.UTF8.GetBytes(secret);
        _expirationSeconds = storageOptions.Value.TokenExpirationSeconds > 0
            ? storageOptions.Value.TokenExpirationSeconds
            : 60;
    }

    public (string Token, DateTime ExpiresAtUtc) GenerateToken(Guid recordingId, Guid userId)
    {
        var expiresAtUtc = DateTime.UtcNow.AddSeconds(_expirationSeconds);
        var expiresUnix = new DateTimeOffset(expiresAtUtc).ToUnixTimeSeconds();
        var payload = $"{recordingId:N}.{userId:N}.{expiresUnix}";

        using var hmac = new HMACSHA256(_keyBytes);
        var signatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var signature = WebEncoders.Base64UrlEncode(signatureBytes);

        var token = $"{payload}.{signature}";
        return (token, expiresAtUtc);
    }

    public (bool IsValid, Guid? UserId, string? ErrorReason) ValidateToken(Guid recordingId, string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return (false, null, "Playback token is missing.");
        }

        var parts = token.Split('.');
        if (parts.Length != 4)
        {
            return (false, null, "Malformed playback token format.");
        }

        if (!Guid.TryParseExact(parts[0], "N", out var tokenRecId) || tokenRecId != recordingId)
        {
            return (false, null, "Playback token does not match requested recording.");
        }

        if (!Guid.TryParseExact(parts[1], "N", out var tokenUserId))
        {
            return (false, null, "Invalid user in playback token.");
        }

        if (!long.TryParse(parts[2], out var expiresUnix))
        {
            return (false, null, "Invalid expiration in playback token.");
        }

        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (expiresUnix < nowUnix)
        {
            return (false, null, "Playback token has expired.");
        }

        var payload = $"{parts[0]}.{parts[1]}.{parts[2]}";
        using var hmac = new HMACSHA256(_keyBytes);
        var expectedSignatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var actualSignatureBytes = WebEncoders.Base64UrlDecode(parts[3]);

        if (!CryptographicOperations.FixedTimeEquals(expectedSignatureBytes, actualSignatureBytes))
        {
            return (false, null, "Playback token signature verification failed.");
        }

        return (true, tokenUserId, null);
    }
}
