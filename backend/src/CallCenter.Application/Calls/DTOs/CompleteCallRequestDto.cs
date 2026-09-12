using System.ComponentModel.DataAnnotations;

namespace CallCenter.Application.Calls.DTOs;

public sealed class CompleteCallRequestDto
{
    [Required]
    public Guid DispositionId { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }

    public DateTime? FollowUpAt { get; set; }

    [StringLength(1000)]
    public string? FollowUpNotes { get; set; }
}
