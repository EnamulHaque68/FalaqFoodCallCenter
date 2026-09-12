using System.ComponentModel.DataAnnotations;

namespace CallCenter.Application.Routing.DTOs;

public sealed class AssignCallRequestDto
{
    [Required]
    public Guid AgentId { get; set; }
}
