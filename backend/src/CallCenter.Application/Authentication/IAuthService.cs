using CallCenter.Application.Authentication.DTOs;

namespace CallCenter.Application.Authentication;

public interface IAuthService
{
    Task<LoginResponseDto?> LoginAsync(
        LoginRequestDto request,
        CancellationToken cancellationToken = default);

    Task LogoutAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<LoginResponseDto?> GetCurrentUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
