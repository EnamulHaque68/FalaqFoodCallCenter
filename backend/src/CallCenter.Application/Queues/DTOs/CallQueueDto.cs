namespace CallCenter.Application.Queues.DTOs;

public sealed class CallQueueDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = null!;
    public int Priority { get; init; }
    public bool IsActive { get; init; }
    public int WaitingCallsCount { get; init; }
    public double AverageWaitSeconds { get; init; }
    public double LongestWaitSeconds { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}
