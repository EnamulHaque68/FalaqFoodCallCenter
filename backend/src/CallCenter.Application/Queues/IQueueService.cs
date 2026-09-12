using CallCenter.Application.Queues.DTOs;
using CallCenter.Application.Routing.DTOs;

namespace CallCenter.Application.Queues;

public interface IQueueService
{
    Task<IReadOnlyList<CallQueueDto>> GetQueuesAsync(CancellationToken cancellationToken = default);

    Task<QueueSummaryDto> GetQueueSummaryAsync(CancellationToken cancellationToken = default);

    Task<CallQueueDto?> GetQueueByIdAsync(Guid queueId, CancellationToken cancellationToken = default);

    Task<CallQueueDto> CreateQueueAsync(CreateQueueRequestDto request, CancellationToken cancellationToken = default);

    Task<CallQueueDto> UpdateQueueAsync(Guid queueId, UpdateQueueRequestDto request, CancellationToken cancellationToken = default);

    Task<bool> DeleteQueueAsync(Guid queueId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CallQueueEntryDto>> GetQueueEntriesAsync(Guid queueId, CancellationToken cancellationToken = default);

    Task<CallQueueEntryDto> PrioritizeEntryAsync(Guid entryId, int newPriority, CancellationToken cancellationToken = default);

    Task<bool> CancelQueueEntryAsync(Guid callId, string? reason = null, CancellationToken cancellationToken = default);

    Task<bool> CompleteQueueEntryAsync(Guid callId, CancellationToken cancellationToken = default);

    Task<RoutingResultDto?> TryAutoAssignNextCallAsync(Guid? availableAgentId = null, CancellationToken cancellationToken = default);
}
