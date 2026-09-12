namespace CallCenter.Application.Reports.DTOs;

public sealed class CallVolumeReportDto
{
    public int TotalCalls { get; init; }
    public int Incoming { get; init; }
    public int Outgoing { get; init; }
    public int Completed { get; init; }
    public int Missed { get; init; }
    public int Rejected { get; init; }
    public double AverageDurationSeconds { get; init; }
    public double TotalTalkTimeSeconds { get; init; }
}

public sealed class AgentPerformanceReportDto
{
    public Guid AgentId { get; init; }
    public string AgentName { get; init; } = null!;
    public string EmployeeCode { get; init; } = null!;
    public string? Team { get; init; }
    public int TotalCalls { get; init; }
    public int CompletedCalls { get; init; }
    public int MissedCalls { get; init; }
    public int RejectedCalls { get; init; }
    public double TotalTalkTimeSeconds { get; init; }
    public double AverageTalkTimeSeconds { get; init; }
    public double CompletionRatePercent { get; init; }
}

public sealed class QueuePerformanceReportDto
{
    public Guid QueueId { get; init; }
    public string QueueName { get; init; } = null!;
    public int TotalEnqueued { get; init; }
    public int AnsweredCalls { get; init; }
    public int AbandonedCalls { get; init; }
    public double AverageWaitSeconds { get; init; }
    public double MaxWaitSeconds { get; init; }
    public int CurrentWaiting { get; init; }
}

public sealed class DispositionStatDto
{
    public Guid DispositionId { get; init; }
    public string DispositionCode { get; init; } = null!;
    public string DispositionName { get; init; } = null!;
    public int CallCount { get; init; }
    public double Percentage { get; init; }
    public double TotalTalkTimeSeconds { get; init; }
    public double AverageDurationSeconds { get; init; }
    public bool RequiresFollowUp { get; init; }
}

public sealed class CallTimeSeriesPointDto
{
    public string Timestamp { get; init; } = null!;
    public string PeriodLabel { get; init; } = null!;
    public int Total { get; init; }
    public int Incoming { get; init; }
    public int Outgoing { get; init; }
    public int Completed { get; init; }
    public int Missed { get; init; }
    public int Rejected { get; init; }
}

public sealed class DistributionItemDto
{
    public string Label { get; init; } = null!;
    public string Key { get; init; } = null!;
    public int Count { get; init; }
    public double Percentage { get; init; }
}

public sealed class AgentCallCountDto
{
    public Guid AgentId { get; init; }
    public string AgentName { get; init; } = null!;
    public int CallCount { get; init; }
    public int CompletedCount { get; init; }
    public double TalkTimeSeconds { get; init; }
}

public sealed class AnalyticsChartsDto
{
    public IReadOnlyList<CallTimeSeriesPointDto> CallsOverTime { get; init; } = [];
    public IReadOnlyList<AgentCallCountDto> CallsByAgent { get; init; } = [];
    public IReadOnlyList<DistributionItemDto> CallsByDirection { get; init; } = [];
    public IReadOnlyList<DistributionItemDto> CallsByStatus { get; init; } = [];
    public IReadOnlyList<DistributionItemDto> CallsByDisposition { get; init; } = [];
}

public sealed class ComprehensiveAnalyticsReportDto
{
    public string PeriodPreset { get; init; } = "Today";
    public DateTime FromUtc { get; init; }
    public DateTime ToUtc { get; init; }
    public CallVolumeReportDto Volume { get; init; } = null!;
    public IReadOnlyList<AgentPerformanceReportDto> Agents { get; init; } = [];
    public IReadOnlyList<QueuePerformanceReportDto> Queues { get; init; } = [];
    public IReadOnlyList<DispositionStatDto> Dispositions { get; init; } = [];
    public AnalyticsChartsDto Charts { get; init; } = null!;
    public DateTime GeneratedAt { get; init; }
}
