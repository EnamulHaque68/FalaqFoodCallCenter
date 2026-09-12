namespace CallCenter.Application.Reports.DTOs;

public sealed class ReportMetricsDto
{
    public int TotalCalls { get; init; }
    public int Incoming { get; init; }
    public int Outgoing { get; init; }
    public int Completed { get; init; }
    public int Missed { get; init; }
    public int Rejected { get; init; }
    public double AverageDurationSeconds { get; init; }
    public int AvailableAgents { get; init; }
    public int BusyAgents { get; init; }
    public int AwayAgents { get; init; }
    public int OfflineAgents { get; init; }
    public int TotalAgents { get; init; }
    public int ActiveCallsCount { get; init; }
    public int QueueSize { get; init; }
    public int DisposedCallsCount { get; init; }
    public int PendingFollowUpsCount { get; init; }
}
