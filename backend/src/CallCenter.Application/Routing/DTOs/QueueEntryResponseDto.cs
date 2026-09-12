using CallCenter.Domain.Enums;

namespace CallCenter.Application.Routing.DTOs;

public sealed class QueueEntryResponseDto
{
    public Guid Id { get; init; }
    public Guid QueueId { get; init; }
    public Guid CallId { get; init; }
    public int Position { get; init; }
    public DateTime EnqueuedAt { get; init; }
}
