using CallCenter.Application.Agents.DTOs;

namespace CallCenter.Application.Reports.DTOs;

public sealed class QueueStatusSummaryDto
{
    public int TotalWaiting { get; init; }
    public double AverageWaitSeconds { get; init; }
    public double LongestWaitSeconds { get; init; }
    public IReadOnlyList<QueueItemSummaryDto> Entries { get; init; } = [];
}
