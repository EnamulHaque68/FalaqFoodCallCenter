using System.ComponentModel.DataAnnotations;

namespace CallCenter.Application.Routing.DTOs;

public sealed class ReassignCallRequestDto
{
    [Required]
    public Guid AgentId { get; set; }
}
