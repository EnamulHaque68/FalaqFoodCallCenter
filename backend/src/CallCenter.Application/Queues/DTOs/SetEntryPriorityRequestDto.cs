using System.ComponentModel.DataAnnotations;

namespace CallCenter.Application.Queues.DTOs;

public sealed class SetEntryPriorityRequestDto
{
    [Range(0, 100)]
    public int Priority { get; set; }
}
