using CallCenter.Domain.Enums;

namespace CallCenter.Application.Agents.DTOs;

public sealed class AgentResponseDto
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public string EmployeeCode { get; init; } = null!;
    public string DisplayName { get; init; } = null!;
    public string? Team { get; init; }
    public bool IsActive { get; init; } = true;
    public string? RoleName { get; init; }
    public string? UserName { get; init; }
    public AgentStatus Status { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}
