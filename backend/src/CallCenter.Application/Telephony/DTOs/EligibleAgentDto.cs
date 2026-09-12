using CallCenter.Domain.Enums;

namespace CallCenter.Application.Telephony.DTOs;

public sealed class EligibleAgentDto
{
    public Guid AgentId { get; set; }
    public string DisplayName { get; set; } = null!;
    public string EmployeeCode { get; set; } = null!;
    public string? Team { get; set; }
    public AgentStatus Status { get; set; }
    public string RoleName { get; set; } = null!;
    public bool IsEligible { get; set; }
}
