using System.ComponentModel.DataAnnotations;

namespace CallCenter.Application.Dispositions.DTOs;

public sealed class CreateDispositionRequestDto
{
    [Required]
    [StringLength(50, MinimumLength = 2)]
    public string Code { get; set; } = null!;

    [Required]
    [StringLength(150, MinimumLength = 2)]
    public string Name { get; set; } = null!;

    [StringLength(500)]
    public string? Description { get; set; }

    public bool RequiresFollowUp { get; set; } = false;

    public bool RequiresNotes { get; set; } = false;

    public int SortOrder { get; set; } = 0;
}
