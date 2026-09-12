using System.ComponentModel.DataAnnotations;

namespace CallCenter.Application.Agents.DTOs;

public sealed class CreateAgentRequestDto
{
    public Guid? UserId { get; set; }

    [StringLength(50, MinimumLength = 3)]
    public string? UserName { get; set; }

    [StringLength(100, MinimumLength = 6)]
    public string? Password { get; set; }

    [StringLength(50)]
    public string? RoleName { get; set; }

    [StringLength(100)]
    public string? Team { get; set; }

    [Required]
    [StringLength(50, MinimumLength = 1)]
    public string EmployeeCode { get; set; } = null!;

    [Required]
    [StringLength(150, MinimumLength = 2)]
    public string DisplayName { get; set; } = null!;
}
