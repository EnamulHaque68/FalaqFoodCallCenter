using CallCenter.Application.Calls.DTOs;
using CallCenter.Domain.Enums;

namespace CallCenter.Application.Agents.DTOs;

public sealed class AgentDetailsResponseDto
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public string EmployeeCode { get; init; } = null!;
    public string DisplayName { get; init; } = null!;
    public string? Team { get; init; }
    public bool IsActive { get; init; }
    public string? RoleName { get; init; }
    public string? UserName { get; init; }
    public AgentStatus Status { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }

    public int TotalAssignedCalls { get; init; }
    public int CompletedCalls { get; init; }
    public Guid? ActiveCallId { get; init; }
    public IReadOnlyList<CallResponseDto> RecentCalls { get; init; } = [];
}
