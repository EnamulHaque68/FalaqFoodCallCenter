using System.ComponentModel.DataAnnotations;

namespace CallCenter.Application.Authentication.DTOs;

public sealed class LoginRequestDto
{
    [Required]
    [StringLength(100, MinimumLength = 3)]
    public string UserName { get; set; } = null!;

    [Required]
    [StringLength(128, MinimumLength = 1)]
    public string Password { get; set; } = null!;
}
