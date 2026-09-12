namespace CallCenter.Application.Reports.DTOs;

public sealed class CallTrendPointDto
{
    public string Date { get; init; } = null!;
    public string PeriodLabel { get; init; } = null!;
    public int TotalCalls { get; init; }
    public int CompletedCalls { get; init; }
    public int MissedCalls { get; init; }
    public int RejectedCalls { get; init; }
    public int IncomingCalls { get; init; }
    public int OutgoingCalls { get; init; }
}
