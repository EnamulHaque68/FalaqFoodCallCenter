namespace CallCenter.Application.Reports.DTOs;

public sealed class DispositionReportDto
{
    public int TotalCompletedCalls { get; init; }
    public int TotalDisposedCalls { get; init; }
    public int FollowUpsScheduledCount { get; init; }
    public IReadOnlyList<DispositionBreakdownDto> Breakdowns { get; init; } = [];
    public IReadOnlyList<PendingFollowUpCallDto> UpcomingFollowUps { get; init; } = [];
    public DateTime GeneratedAt { get; init; }
}
