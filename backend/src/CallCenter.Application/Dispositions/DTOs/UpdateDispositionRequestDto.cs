using System.ComponentModel.DataAnnotations;

namespace CallCenter.Application.Dispositions.DTOs;

public sealed class UpdateDispositionRequestDto
{
    [Required]
    [StringLength(150, MinimumLength = 2)]
    public string Name { get; set; } = null!;

    [StringLength(500)]
    public string? Description { get; set; }

    public bool RequiresFollowUp { get; set; }

    public bool RequiresNotes { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; }
}
