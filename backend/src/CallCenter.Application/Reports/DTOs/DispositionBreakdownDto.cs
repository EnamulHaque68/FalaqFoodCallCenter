namespace CallCenter.Application.Reports.DTOs;

public sealed class DispositionBreakdownDto
{
    public Guid DispositionId { get; init; }
    public string DispositionCode { get; init; } = null!;
    public string DispositionName { get; init; } = null!;
    public int CallCount { get; init; }
    public double Percentage { get; init; }
    public double AverageDurationSeconds { get; init; }
    public bool RequiresFollowUp { get; init; }
}
