using System.ComponentModel.DataAnnotations;
using CallCenter.Domain.Enums;

namespace CallCenter.Application.Agents.DTOs;

public sealed class UpdateAgentStatusRequestDto
{
    [Required]
    [EnumDataType(typeof(AgentStatus))]
    public AgentStatus Status { get; set; }
}
