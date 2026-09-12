namespace CallCenter.Application.Telephony.DTOs;

public sealed class TelephonyTokenResponseDto
{
    public string Token { get; set; } = string.Empty;
    public string Identity { get; set; } = string.Empty;
    public string Provider { get; set; } = "Simulated";
    public string? VoiceNumber { get; set; }
    public int ExpiresInSeconds { get; set; } = 900;
}
