using System.ComponentModel.DataAnnotations;

namespace CallCenter.Application.Queues.DTOs;

public sealed class CreateQueueRequestDto
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = null!;

    public int Priority { get; set; } = 1;

    public bool IsActive { get; set; } = true;
}
