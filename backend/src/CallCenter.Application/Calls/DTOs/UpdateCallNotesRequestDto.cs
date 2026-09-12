using System.ComponentModel.DataAnnotations;

namespace CallCenter.Application.Calls.DTOs;

public sealed class UpdateCallNotesRequestDto
{
    [Required]
    public string Notes { get; set; } = null!;
}
