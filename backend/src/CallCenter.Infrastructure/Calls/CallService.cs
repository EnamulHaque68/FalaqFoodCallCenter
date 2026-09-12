using CallCenter.Application.Calls;
using CallCenter.Application.Calls.DTOs;
using CallCenter.Domain.Entities;
using CallCenter.Domain.Enums;
using CallCenter.Infrastructure.Persistence;
using CallCenter.Application.RealTime;
using CallCenter.Application.RealTime.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Infrastructure.Calls;

public sealed class CallService(
    CallCenterDbContext dbContext,
    IRealTimeNotifier realTimeNotifier) : ICallService
{
    public Task<CallResponseDto> CreateIncomingAsync(
        CreateIncomingCallRequestDto request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default) =>
        CreateAsync(
            request.CustomerId,
            request.PhoneNumber,
            request.CorrelationId,
            idempotencyKey,
            CallDirection.Inbound,
            CallStatus.Queued,
            cancellationToken);

    public Task<CallResponseDto> CreateOutgoingAsync(
        CreateOutgoingCallRequestDto request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default) =>
        CreateAsync(
            request.CustomerId,
            request.PhoneNumber,
            request.CorrelationId,
            idempotencyKey,
            CallDirection.Outbound,
            CallStatus.Ringing,
            cancellationToken);

    public async Task<CallResponseDto?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var call = await dbContext.Calls
            .AsNoTracking()
            .Include(x => x.Customer)
            .Include(x => x.AssignedAgent)
            .Include(x => x.CallDisposition)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        return call is null ? null : ToDto(call);
    }

    public async Task<CallPagedResultDto> GetHistoryAsync(
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
        CancellationToken cancellationToken = default)
    {
        if (page < 1)
            throw new ArgumentOutOfRangeException(nameof(page));

        if (pageSize < 1 || pageSize > 100)
            throw new ArgumentOutOfRangeException(nameof(pageSize));

        var query = dbContext.Calls.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x =>
                x.Customer.DisplayName.Contains(term) ||
                x.Customer.PhoneNumber.Contains(term) ||
                x.PhoneNumber.Contains(term) ||
                (x.AssignedAgent != null && x.AssignedAgent.DisplayName.Contains(term)));
        }

        if (customerId.HasValue)
            query = query.Where(x => x.CustomerId == customerId.Value);

        if (agentId.HasValue)
            query = query.Where(x => x.AssignedAgentId == agentId.Value);

        if (direction.HasValue)
            query = query.Where(x => x.Direction == direction.Value);

        if (status.HasValue)
            query = query.Where(x => x.Status == status.Value);

        if (fromUtc.HasValue)
            query = query.Where(x => x.StartedAt >= fromUtc.Value);

        if (toUtc.HasValue)
            query = query.Where(x => x.StartedAt <= toUtc.Value);

        if (dispositionId.HasValue)
            query = query.Where(x => x.CallDispositionId == dispositionId.Value);

        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = totalCount == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)pageSize);

        var calls = await query
            .Include(x => x.Customer)
            .Include(x => x.AssignedAgent)
            .Include(x => x.CallDisposition)
            .OrderByDescending(x => x.StartedAt)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = calls.Select(ToDto).ToList();

        return new CallPagedResultDto
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages
        };
    }

    public async Task<CallResponseDto?> TransitionAsync(
        Guid id,
        CallStatus status,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));

        var call = await dbContext.Calls
            .Include(x => x.Customer)
            .Include(x => x.AssignedAgent)
            .Include(x => x.CallDisposition)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (call is null)
            return null;

        // Idempotent duplicate state notifications must not create duplicate events.
        if (call.Status == status)
            return ToDto(call);

        var now = DateTime.UtcNow;
        call.TransitionTo(status, now);
        AddEvent(call, status.ToString(), now);

        await dbContext.SaveChangesAsync(cancellationToken);
        await realTimeNotifier.NotifyCallStatusChangedAsync(
            new CallStatusChangedEvent(call.Id, call.AssignedAgentId, call.CustomerId, call.Status.ToString(), DateTime.UtcNow),
            cancellationToken);
        return ToDto(call);
    }

    public async Task<CallResponseDto?> CompleteAsync(
        Guid id,
        Guid dispositionId,
        string? notes = null,
        DateTime? followUpAt = null,
        string? followUpNotes = null,
        CancellationToken cancellationToken = default)
    {
        var call = await dbContext.Calls
            .Include(x => x.Customer)
            .Include(x => x.AssignedAgent)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (call is null)
            return null;

        var disposition = await dbContext.CallDispositions
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == dispositionId && x.IsActive, cancellationToken);

        if (disposition is null)
        {
            throw new KeyNotFoundException(
                "The specified active call disposition was not found.");
        }

        if (disposition.RequiresFollowUp)
        {
            if (!followUpAt.HasValue)
            {
                throw new ArgumentException("A follow-up date and time is required for this disposition.", nameof(followUpAt));
            }

            if (followUpAt.Value <= DateTime.UtcNow)
            {
                throw new ArgumentException("The follow-up date and time must be in the future.", nameof(followUpAt));
            }
        }

        if (disposition.RequiresNotes && string.IsNullOrWhiteSpace(notes))
        {
            throw new ArgumentException("Call notes are required for this disposition.", nameof(notes));
        }

        var now = DateTime.UtcNow;
        call.Complete(dispositionId, now, notes, followUpAt, followUpNotes);
        call.CallDisposition = disposition;

        var metadataJson = $"{{\"disposition\":\"{disposition.Code}\",\"name\":\"{disposition.Name}\",\"followUpAt\":\"{followUpAt:O}\"}}";
        AddEvent(call, "Completed", now, metadataJson);

        // Free assigned agent to Available
        if (call.AssignedAgentId.HasValue)
        {
            var agent = await dbContext.Agents
                .SingleOrDefaultAsync(x => x.Id == call.AssignedAgentId.Value, cancellationToken);
            if (agent is not null && agent.Status == AgentStatus.Busy)
            {
                agent.Status = AgentStatus.Available;
                agent.UpdatedAt = now;
                await realTimeNotifier.NotifyAgentStatusChangedAsync(
                    new AgentStatusChangedEvent(agent.Id, agent.UserId, agent.Status.ToString(), now),
                    cancellationToken);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await realTimeNotifier.NotifyCallStatusChangedAsync(
            new CallStatusChangedEvent(call.Id, call.AssignedAgentId, call.CustomerId, call.Status.ToString(), now),
            cancellationToken);

        return ToDto(call);
    }

    public async Task<IReadOnlyList<CallTimelineEventDto>> GetTimelineAsync(
        Guid callId,
        CancellationToken cancellationToken = default)
    {
        var events = await dbContext.CallEvents
            .AsNoTracking()
            .Include(x => x.Agent)
            .Where(x => x.CallId == callId)
            .OrderBy(x => x.OccurredAt)
            .ToListAsync(cancellationToken);

        return events.Select(e => new CallTimelineEventDto
        {
            Id = e.Id,
            CallId = e.CallId,
            EventType = e.EventType,
            Description = GetEventDescription(e.EventType, e.MetadataJson),
            MetadataJson = e.MetadataJson,
            AgentId = e.AgentId,
            AgentName = e.Agent?.DisplayName,
            OccurredAt = e.OccurredAt
        }).ToList();
    }

    public async Task<CallResponseDto?> UpdateNotesAsync(
        Guid callId,
        string notes,
        CancellationToken cancellationToken = default)
    {
        var call = await dbContext.Calls
            .Include(x => x.Customer)
            .Include(x => x.AssignedAgent)
            .Include(x => x.CallDisposition)
            .SingleOrDefaultAsync(x => x.Id == callId, cancellationToken);

        if (call is null) return null;

        var now = DateTime.UtcNow;
        call.Notes = notes.Trim();
        call.UpdatedAt = now;

        AddEvent(call, "NotesUpdated", now);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ToDto(call);
    }

    private static string GetEventDescription(string eventType, string? metadata) => eventType switch
    {
        "Created" => "Call initiated",
        "Incoming" => "Inbound call received",
        "IncomingCallCreated" => "Inbound call received",
        "Outgoing" => "Outbound call initiated",
        "OutgoingCallCreated" => "Outbound call initiated",
        "Queued" => "Call placed in waiting queue",
        "Assigned" => FormatAssignedDescription(metadata),
        "Ringing" => "Call routed and ringing",
        "Connected" => "Call answered and connected",
        "Hold" => "Call placed on hold",
        "Resume" => FormatResumedDescription(metadata),
        "Resumed" => FormatResumedDescription(metadata),
        "Transferred" => FormatTransferredDescription(metadata),
        "TransferredToQueue" => "Call transferred to queue",
        "Completed" => FormatCompletedDescription(metadata),
        "Ended" => "Call ended",
        "Abandoned" => "Call abandoned",
        "Rejected" => "Call rejected",
        "NotesUpdated" => "Call notes updated",
        _ => eventType
    };

    private static string FormatAssignedDescription(string? metadata)
    {
        if (!string.IsNullOrWhiteSpace(metadata))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(metadata);
                if (doc.RootElement.TryGetProperty("agentName", out var nameProp))
                {
                    var name = nameProp.GetString();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        return $"Call assigned to agent {name}";
                    }
                }
            }
            catch { }
        }
        return "Call assigned to agent";
    }

    private static string FormatTransferredDescription(string? metadata)
    {
        if (!string.IsNullOrWhiteSpace(metadata))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(metadata);
                if (doc.RootElement.TryGetProperty("transferType", out var typeProp))
                {
                    var type = typeProp.GetString();
                    if (!string.IsNullOrWhiteSpace(type))
                    {
                        return $"Call transferred ({type} Transfer)";
                    }
                }
            }
            catch { }
        }
        return "Call transferred";
    }

    private static string FormatCompletedDescription(string? metadata)
    {
        if (!string.IsNullOrWhiteSpace(metadata))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(metadata);
                if (doc.RootElement.TryGetProperty("name", out var nameProp))
                {
                    var name = nameProp.GetString();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        return $"Call completed ({name})";
                    }
                }
            }
            catch { }
        }
        return "Call completed";
    }

    private static string FormatResumedDescription(string? metadata)
    {
        if (!string.IsNullOrWhiteSpace(metadata) && metadata.Contains("holdDurationSeconds"))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(metadata);
                if (doc.RootElement.TryGetProperty("holdDurationSeconds", out var prop) && prop.TryGetInt32(out var seconds) && seconds > 0)
                {
                    return $"Call resumed from hold (held for {seconds}s)";
                }
            }
            catch
            {
                // Fallback
            }
        }

        return "Call resumed from hold";
    }

    private async Task<CallResponseDto> CreateAsync(
        Guid customerId,
        string phoneNumber,
        string correlationId,
        string? idempotencyKey,
        CallDirection direction,
        CallStatus initialStatus,
        CancellationToken cancellationToken)
    {
        var normalizedPhone = NormalizePhone(phoneNumber);
        var normalizedCorrelationId = correlationId.Trim();
        var normalizedIdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey)
            ? null
            : idempotencyKey.Trim();

        if (string.IsNullOrWhiteSpace(normalizedPhone))
            throw new ArgumentException("A valid phone number is required.", nameof(phoneNumber));

        if (string.IsNullOrWhiteSpace(normalizedCorrelationId))
            throw new ArgumentException("CorrelationId is required.", nameof(correlationId));

        var customer = await dbContext.Customers
            .SingleOrDefaultAsync(x => x.Id == customerId, cancellationToken);

        if (customer is null)
            throw new KeyNotFoundException("The specified customer was not found.");

        if (normalizedIdempotencyKey is not null)
        {
            var existingByKey = await dbContext.Calls
                .Include(x => x.Customer)
                .Include(x => x.AssignedAgent)
                .Include(x => x.CallDisposition)
                .SingleOrDefaultAsync(x => x.IdempotencyKey == normalizedIdempotencyKey, cancellationToken);

            if (existingByKey is not null)
            {
                if (existingByKey.CustomerId != customerId ||
                    existingByKey.Direction != direction ||
                    existingByKey.PhoneNumber != normalizedPhone)
                {
                    throw new InvalidOperationException(
                        "The idempotency key was already used for a different call request.");
                }

                return ToDto(existingByKey);
            }
        }

        var existingByCorrelation = await dbContext.Calls
            .SingleOrDefaultAsync(x => x.CorrelationId == normalizedCorrelationId, cancellationToken);

        if (existingByCorrelation is not null)
        {
            throw new InvalidOperationException(
                "A call with the specified correlation ID already exists.");
        }

        var now = DateTime.UtcNow;
        var call = new Call
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            Customer = customer,
            PhoneNumber = normalizedPhone,
            CorrelationId = normalizedCorrelationId,
            IdempotencyKey = normalizedIdempotencyKey,
            Direction = direction,
            Status = initialStatus,
            StartedAt = now,
            CreatedAt = now
        };

        dbContext.Calls.Add(call);
        AddEvent(call, direction == CallDirection.Inbound ? "IncomingCallCreated" : "OutgoingCallCreated", now);
        await dbContext.SaveChangesAsync(cancellationToken);

        await realTimeNotifier.NotifyCallStatusChangedAsync(
            new CallStatusChangedEvent(call.Id, call.AssignedAgentId, call.CustomerId, call.Status.ToString(), now),
            cancellationToken);

        return ToDto(call);
    }

    private void AddEvent(Call call, string eventType, DateTime occurredAt, string? metadata = null)
    {
        dbContext.CallEvents.Add(new CallEvent
        {
            Id = Guid.NewGuid(),
            CallId = call.Id,
            AgentId = call.AssignedAgentId,
            EventType = eventType,
            MetadataJson = metadata,
            OccurredAt = occurredAt
        });
    }

    private static string NormalizePhone(string phone) =>
        new(phone.Where(char.IsDigit).ToArray());

    private static CallResponseDto ToDto(Call call) =>
        new()
        {
            Id = call.Id,
            CustomerId = call.CustomerId,
            CustomerName = call.Customer != null ? call.Customer.DisplayName : string.Empty,
            CustomerPhone = call.Customer != null ? call.Customer.PhoneNumber : call.PhoneNumber,
            AssignedAgentId = call.AssignedAgentId,
            AgentName = call.AssignedAgent != null ? call.AssignedAgent.DisplayName : null,
            CallQueueId = call.CallQueueId,
            CallDispositionId = call.CallDispositionId,
            DispositionName = call.CallDisposition != null ? call.CallDisposition.Name : null,
            ProviderCallId = call.ProviderCallId,
            CorrelationId = call.CorrelationId,
            Direction = call.Direction,
            Status = call.Status,
            PhoneNumber = call.PhoneNumber,
            Notes = call.Notes,
            FollowUpAt = call.FollowUpAt,
            FollowUpNotes = call.FollowUpNotes,
            StartedAt = call.StartedAt,
            AnsweredAt = call.AnsweredAt,
            EndedAt = call.EndedAt,
            DurationSeconds = call.AnsweredAt.HasValue && call.EndedAt.HasValue
                ? (int?)(call.EndedAt.Value - call.AnsweredAt.Value).TotalSeconds
                : null,
            CreatedAt = call.CreatedAt,
            UpdatedAt = call.UpdatedAt
        };
}
