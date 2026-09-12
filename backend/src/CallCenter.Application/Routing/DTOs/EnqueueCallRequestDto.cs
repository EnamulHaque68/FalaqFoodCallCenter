using System.ComponentModel.DataAnnotations;

namespace CallCenter.Application.Routing.DTOs;

public sealed class EnqueueCallRequestDto
{
    [Required]
    public Guid CallId { get; set; }

    [Required]
    public Guid QueueId { get; set; }
}
