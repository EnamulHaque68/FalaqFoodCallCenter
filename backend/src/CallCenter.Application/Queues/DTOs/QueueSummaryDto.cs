namespace CallCenter.Application.Queues.DTOs;

public sealed class QueueSummaryDto
{
    public int TotalQueues { get; init; }
    public int ActiveQueues { get; init; }
    public int TotalWaitingCalls { get; init; }
    public double AverageWaitSeconds { get; init; }
    public double LongestWaitSeconds { get; init; }
    public int AvailableAgentsCount { get; init; }
}
