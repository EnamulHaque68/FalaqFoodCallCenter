using CallCenter.Application.Users.DTOs;

namespace CallCenter.Application.Users;

public interface IUserService
{
    Task<IReadOnlyList<UserListItemDto>> GetAllAsync(
        string? search = null,
        string? role = null,
        bool? isActive = null,
        CancellationToken cancellationToken = default);

    Task<UserListItemDto?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<UserListItemDto> CreateAsync(
        CreateUserRequestDto request,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    Task<UserListItemDto?> UpdateAsync(
        Guid id,
        UpdateUserRequestDto request,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    Task<UserListItemDto?> DeactivateAsync(
        Guid id,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    Task<UserListItemDto?> ReactivateAsync(
        Guid id,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    Task<UserListItemDto?> ResetPasswordAsync(
        Guid id,
        ResetPasswordRequestDto request,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    Task<bool> ChangePasswordAsync(
        Guid id,
        ChangePasswordRequestDto request,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RoleDto>> GetRolesAsync(
        CancellationToken cancellationToken = default);
}
