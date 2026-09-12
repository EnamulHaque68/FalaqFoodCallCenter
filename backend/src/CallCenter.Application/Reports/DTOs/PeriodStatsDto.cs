namespace CallCenter.Application.Reports.DTOs;

public sealed class PeriodStatsDto
{
    public string PeriodName { get; init; } = null!;
    public int TotalCalls { get; init; }
    public int Incoming { get; init; }
    public int Outgoing { get; init; }
    public int Completed { get; init; }
    public int Missed { get; init; }
    public int Rejected { get; init; }
    public double AverageDurationSeconds { get; init; }
    public double CompletionRatePercent { get; init; }
}
