using CallCenter.Application.Calls.DTOs;
using CallCenter.Domain.Enums;

namespace CallCenter.Application.Calls;

public interface ICallService
{
    Task<CallResponseDto> CreateIncomingAsync(
        CreateIncomingCallRequestDto request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<CallResponseDto> CreateOutgoingAsync(
        CreateOutgoingCallRequestDto request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<CallResponseDto?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<CallPagedResultDto> GetHistoryAsync(
        string? search = null,
        Guid? customerId = null,
        Guid? agentId = null,
        CallDirection? direction = null,
        CallStatus? status = null,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        Guid? dispositionId = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    Task<CallResponseDto?> TransitionAsync(
        Guid id,
        CallStatus status,
        CancellationToken cancellationToken = default);

    Task<CallResponseDto?> CompleteAsync(
        Guid id,
        Guid dispositionId,
        string? notes = null,
        DateTime? followUpAt = null,
        string? followUpNotes = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CallTimelineEventDto>> GetTimelineAsync(
        Guid callId,
        CancellationToken cancellationToken = default);

    Task<CallResponseDto?> UpdateNotesAsync(
        Guid callId,
        string notes,
        CancellationToken cancellationToken = default);
}
