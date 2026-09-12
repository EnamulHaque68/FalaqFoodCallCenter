namespace CallCenter.Application.Agents.DTOs;

public sealed class QueueItemSummaryDto
{
    public Guid CallId { get; init; }
    public string PhoneNumber { get; init; } = null!;
    public int Position { get; init; }
    public DateTime EnqueuedAt { get; init; }
    public string? QueueName { get; init; }
}
