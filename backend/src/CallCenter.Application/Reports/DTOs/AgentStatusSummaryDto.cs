namespace CallCenter.Application.Reports.DTOs;

public sealed class AgentStatusSummaryDto
{
    public int Available { get; init; }
    public int Busy { get; init; }
    public int Away { get; init; }
    public int Offline { get; init; }
    public int Total { get; init; }
    public IReadOnlyList<AgentStatusItemDto> Agents { get; init; } = [];
}
