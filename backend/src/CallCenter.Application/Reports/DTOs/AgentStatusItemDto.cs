using CallCenter.Domain.Enums;

namespace CallCenter.Application.Reports.DTOs;

public sealed class AgentStatusItemDto
{
    public Guid Id { get; init; }
    public string DisplayName { get; init; } = null!;
    public string EmployeeCode { get; init; } = null!;
    public string? Team { get; init; }
    public AgentStatus Status { get; init; }
    public bool IsActive { get; init; }
    public string? CurrentCallPhoneNumber { get; init; }
    public DateTime? CurrentCallStartedAt { get; init; }
}
