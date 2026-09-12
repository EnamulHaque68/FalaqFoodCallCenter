namespace CallCenter.Application.Reports.DTOs;

public sealed class OperationsDashboardDto
{
    public ReportMetricsDto Metrics { get; init; } = null!;
    public PeriodStatsDto DailyStats { get; init; } = null!;
    public PeriodStatsDto WeeklyStats { get; init; } = null!;
    public PeriodStatsDto MonthlyStats { get; init; } = null!;
    public IReadOnlyList<CallTrendPointDto> CallTrends { get; init; } = [];
    public AgentStatusSummaryDto AgentStatus { get; init; } = null!;
    public QueueStatusSummaryDto QueueStatus { get; init; } = null!;
    public IReadOnlyList<DispositionBreakdownDto> DispositionBreakdown { get; init; } = [];
    public int PendingFollowUpsCount { get; init; }
    public DateTime GeneratedAt { get; init; }
}
