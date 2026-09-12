using System.ComponentModel.DataAnnotations;

namespace CallCenter.Application.Users.DTOs;

public sealed class UserListItemDto
{
    public Guid Id { get; init; }
    public string UserName { get; init; } = null!;
    public string RoleName { get; init; } = null!;
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public DateTime? LastLoginAt { get; init; }
    public Guid? AgentId { get; init; }
    public string? AgentName { get; init; }
    public string? EmployeeCode { get; init; }
    public string? Team { get; init; }
    public string? AgentStatus { get; init; }
}

public sealed class CreateUserRequestDto
{
    [Required, StringLength(50, MinimumLength = 3)]
    public string UserName { get; set; } = null!;

    [Required, StringLength(100, MinimumLength = 8)]
    public string Password { get; set; } = null!;

    [Required, StringLength(50)]
    public string RoleName { get; set; } = null!;

    [StringLength(150)]
    public string? DisplayName { get; set; }

    [StringLength(50)]
    public string? EmployeeCode { get; set; }

    [StringLength(100)]
    public string? Team { get; set; }
}

public sealed class UpdateUserRequestDto
{
    [Required, StringLength(50, MinimumLength = 3)]
    public string UserName { get; set; } = null!;

    [Required, StringLength(50)]
    public string RoleName { get; set; } = null!;

    public bool IsActive { get; set; } = true;

    [StringLength(150)]
    public string? DisplayName { get; set; }

    [StringLength(100)]
    public string? Team { get; set; }
}

public sealed class ResetPasswordRequestDto
{
    [Required, StringLength(100, MinimumLength = 8)]
    public string NewPassword { get; set; } = null!;
}

public sealed class ChangePasswordRequestDto
{
    [Required]
    public string CurrentPassword { get; set; } = null!;

    [Required, StringLength(100, MinimumLength = 8)]
    public string NewPassword { get; set; } = null!;
}

public sealed class RoleDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = null!;
    public string? Description { get; init; }
    public IReadOnlyList<string> Permissions { get; init; } = [];
}
