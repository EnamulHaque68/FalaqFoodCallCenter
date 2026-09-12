using CallCenter.Domain.Enums;

namespace CallCenter.Application.Queues.DTOs;

public sealed class CallQueueEntryDto
{
    public Guid Id { get; init; }
    public Guid CallQueueId { get; init; }
    public string QueueName { get; init; } = null!;
    public Guid CallId { get; init; }
    public int Position { get; init; }
    public int Priority { get; init; }
    public DateTime EnqueuedAt { get; init; }
    public double WaitSeconds { get; init; }
    public string PhoneNumber { get; init; } = null!;
    public Guid? CustomerId { get; init; }
    public string? CustomerName { get; init; }
    public CallDirection Direction { get; init; }
    public CallStatus Status { get; init; }
}
