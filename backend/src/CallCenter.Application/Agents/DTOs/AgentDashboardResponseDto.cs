using CallCenter.Application.Calls.DTOs;
using CallCenter.Domain.Enums;

namespace CallCenter.Application.Agents.DTOs;

public sealed class AgentDashboardResponseDto
{
    public Guid AgentId { get; init; }
    public Guid UserId { get; init; }
    public string EmployeeCode { get; init; } = null!;
    public string DisplayName { get; init; } = null!;
    public string? Team { get; init; }
    public AgentStatus Status { get; init; }
    public int TodaysCallsCount { get; init; }
    public int CompletedCallsCount { get; init; }
    public int MissedCallsCount { get; init; }
    public double AverageDurationSeconds { get; init; }
    public CallResponseDto? CurrentCall { get; init; }
    public int QueueCount { get; init; }
    public IReadOnlyList<QueueItemSummaryDto> IncomingQueue { get; init; } = [];
    public IReadOnlyList<CallResponseDto> RecentCalls { get; init; } = [];
}
