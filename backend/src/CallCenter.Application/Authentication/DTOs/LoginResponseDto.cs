namespace CallCenter.Application.Authentication.DTOs;

public sealed class LoginResponseDto
{
    public string AccessToken { get; init; } = null!;
    public DateTime ExpiresAtUtc { get; init; }
    public Guid UserId { get; init; }
    public string UserName { get; init; } = null!;
    public string Role { get; init; } = null!;
    public IReadOnlyList<string> Permissions { get; init; } = [];
}
