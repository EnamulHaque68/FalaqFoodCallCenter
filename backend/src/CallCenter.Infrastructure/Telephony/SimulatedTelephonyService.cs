using CallCenter.Application.Audit;
using CallCenter.Application.Calls.DTOs;
using CallCenter.Application.Queues;
using CallCenter.Application.RealTime;
using CallCenter.Application.RealTime.Contracts;
using CallCenter.Application.Recordings;
using CallCenter.Application.Routing;
using CallCenter.Application.Telephony;
using CallCenter.Application.Telephony.DTOs;
using CallCenter.Application.Telephony.Providers;
using CallCenter.Domain.Entities;
using CallCenter.Domain.Enums;
using CallCenter.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Infrastructure.Telephony;

public sealed class SimulatedTelephonyService(
    CallCenterDbContext dbContext,
    IRealTimeNotifier realTimeNotifier,
    IRoutingService routingService,
    IQueueService? queueService = null,
    IAuditLogService? auditLogService = null,
    ITelephonyProvider? telephonyProvider = null,
    ITelephonyProviderFactory? providerFactory = null,
    IRecordingStorage? recordingStorage = null,
    Microsoft.Extensions.Options.IOptions<TelephonyOptions>? options = null) : ITelephonyService
{
    private readonly ITelephonyProvider _telephonyProvider = telephonyProvider ?? new Providers.SimulatedTelephonyProvider();

    public async Task<TelephonyCallResponseDto> SimulateIncomingCallAsync(
        SimulateIncomingCallRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            throw new ArgumentException("CorrelationId is required.", nameof(request.CorrelationId));
        }

        var normalizedPhone = NormalizePhone(request.PhoneNumber);
        if (string.IsNullOrWhiteSpace(normalizedPhone) || normalizedPhone.Length < 7)
        {
            throw new ArgumentException("A valid phone number with at least 7 digits is required.", nameof(request.PhoneNumber));
        }

        var existing = await FindByCorrelationAsync(
            request.CorrelationId,
            cancellationToken);

        if (existing is not null)
        {
            throw new InvalidOperationException(
                "A call with the specified correlation ID already exists.");
        }

        var customer = await ResolveCustomerAsync(
            request.CustomerId,
            normalizedPhone,
            cancellationToken);

        var now = DateTime.UtcNow;

        // 1. Create Call in Queued status
        var call = new Call
        {
            Id = Guid.NewGuid(),
            CustomerId = customer.Id,
            Customer = customer,
            Direction = CallDirection.Inbound,
            Status = CallStatus.Queued,
            CorrelationId = request.CorrelationId.Trim(),
            PhoneNumber = normalizedPhone,
            ProviderCallId = $"SIM-{Guid.NewGuid():N}",
            StartedAt = now,
            CreatedAt = now
        };

        dbContext.Calls.Add(call);
        dbContext.CallEvents.Add(new CallEvent
        {
            Id = Guid.NewGuid(),
            CallId = call.Id,
            EventType = "Incoming",
            OccurredAt = now,
            MetadataJson = $"{{\"phone\":\"{normalizedPhone}\",\"customerId\":\"{customer.Id}\"}}"
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        // 2. Delegate queueing and routing to IRoutingService
        var routingResult = await routingService.RouteCallAsync(
            call.Id,
            cancellationToken: cancellationToken);

        var routedCall = await dbContext.Calls
            .SingleAsync(x => x.Id == call.Id, cancellationToken);

        // 3. Broadcast Real-Time IncomingCall Notification
        await realTimeNotifier.NotifyIncomingCallAsync(
            new IncomingCallEvent(
                routedCall.Id,
                routedCall.CustomerId,
                routedCall.PhoneNumber,
                routedCall.Direction.ToString(),
                routedCall.Status.ToString(),
                now),
            cancellationToken);

        return ToDto(routedCall);
    }

    public async Task<TelephonyCallResponseDto> InitiateOutboundCallAsync(
        InitiateOutboundCallRequestDto request,
        Guid actingAgentId,
        bool isPrivilegedCaller,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            throw new ArgumentException("CorrelationId is required.", nameof(request.CorrelationId));
        }

        var normalizedPhone = NormalizePhone(request.PhoneNumber);
        if (string.IsNullOrWhiteSpace(normalizedPhone) || normalizedPhone.Length < 7)
        {
            throw new ArgumentException("A valid phone number with at least 7 digits is required.", nameof(request.PhoneNumber));
        }

        var existing = await FindByCorrelationAsync(
            request.CorrelationId,
            cancellationToken);

        if (existing is not null)
        {
            return ToDto(existing);
        }

        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existingByKey = await dbContext.Calls
                .FirstOrDefaultAsync(x => x.IdempotencyKey == request.IdempotencyKey, cancellationToken);
            if (existingByKey is not null)
            {
                return ToDto(existingByKey);
            }
        }


        if (request.CustomerId.HasValue)
        {
            var customerExists = await dbContext.Customers
                .AnyAsync(x => x.Id == request.CustomerId.Value, cancellationToken);

            if (!customerExists)
            {
                throw new KeyNotFoundException("The specified customer was not found.");
            }
        }

        var effectiveAgentId = request.AgentId ?? actingAgentId;

        if (!isPrivilegedCaller && effectiveAgentId != actingAgentId)
        {
            throw new UnauthorizedAccessException(
                "Agents may only initiate outgoing calls for themselves.");
        }

        var agentExists = await dbContext.Agents
            .AnyAsync(x => x.Id == effectiveAgentId, cancellationToken);

        if (!agentExists)
        {
            throw new KeyNotFoundException("The specified agent was not found.");
        }

        var now = DateTime.UtcNow;
        var callId = Guid.NewGuid();
        var dialResult = await _telephonyProvider.DialAsync(
            new TelephonyOutboundCommand(
                callId,
                normalizedPhone,
                null,
                effectiveAgentId,
                request.CorrelationId.Trim()),
            cancellationToken);

        var customer = await ResolveCustomerAsync(
            request.CustomerId,
            normalizedPhone,
            cancellationToken);

        var call = new Call
        {
            Id = callId,
            CustomerId = customer.Id,
            Customer = customer,
            AssignedAgentId = effectiveAgentId,
            Direction = CallDirection.Outbound,
            Status = CallStatus.Ringing,
            CorrelationId = request.CorrelationId.Trim(),
            IdempotencyKey = request.IdempotencyKey,
            PhoneNumber = normalizedPhone,
            ProviderCallId = dialResult.ProviderCallId ?? $"SIM-{callId:N}",
            StartedAt = now,
            CreatedAt = now
        };

        dbContext.Calls.Add(call);
        dbContext.CallEvents.Add(new CallEvent
        {
            Id = Guid.NewGuid(),
            CallId = call.Id,
            AgentId = effectiveAgentId,
            EventType = "Outgoing",
            OccurredAt = now,
            MetadataJson = $"{{\"phone\":\"{normalizedPhone}\",\"customerId\":\"{call.CustomerId}\",\"agentId\":\"{effectiveAgentId}\"}}"
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        await realTimeNotifier.NotifyCallStatusChangedAsync(
            new CallStatusChangedEvent(
                call.Id,
                call.AssignedAgentId,
                call.CustomerId,
                call.Status.ToString(),
                now),
            cancellationToken);

        return ToDto(call);
    }

    public async Task<TelephonyCallResponseDto> AcceptCallAsync(
        Guid callId,
        Guid? actingAgentId,
        bool isPrivilegedCaller,
        CancellationToken cancellationToken = default)
    {
        var call = await GetCallAsync(callId, cancellationToken);

        EnsureCallAccess(call, actingAgentId, isPrivilegedCaller, allowUnassignedClaim: true);

        if (call.Status != CallStatus.Queued && call.Status != CallStatus.Ringing)
        {
            throw new InvalidOperationException(
                $"A call in '{call.Status}' state cannot be accepted.");
        }

        // An unassigned inbound call is claimed by whichever agent accepts it.
        if (call.AssignedAgentId is null && actingAgentId.HasValue)
        {
            call.AssignedAgentId = actingAgentId;
        }

        var now = DateTime.UtcNow;
        call.Status = CallStatus.Connected;
        call.AnsweredAt ??= now;
        call.UpdatedAt = now;

        await _telephonyProvider.AcceptCallAsync(
            new TelephonyAcceptCommand(call.Id, call.ProviderCallId, actingAgentId),
            cancellationToken);

        // Dequeue any active queue entries
        var queueEntries = await dbContext.CallQueueEntries
            .Where(x => x.CallId == call.Id && x.DequeuedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var entry in queueEntries)
        {
            entry.DequeuedAt = now;
        }

        // Transition agent status to Busy
        if (call.AssignedAgentId.HasValue)
        {
            var agent = await dbContext.Agents
                .SingleOrDefaultAsync(x => x.Id == call.AssignedAgentId.Value, cancellationToken);
            if (agent is not null)
            {
                agent.Status = AgentStatus.Busy;
                agent.UpdatedAt = now;
                await realTimeNotifier.NotifyAgentStatusChangedAsync(
                    new AgentStatusChangedEvent(agent.Id, agent.UserId, agent.Status.ToString(), now),
                    cancellationToken);
            }
        }

        dbContext.CallEvents.Add(new CallEvent
        {
            Id = Guid.NewGuid(),
            CallId = call.Id,
            AgentId = call.AssignedAgentId,
            EventType = "Connected",
            OccurredAt = now,
            MetadataJson = $"{{\"agentId\":\"{call.AssignedAgentId}\"}}"
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        await realTimeNotifier.NotifyCallStatusChangedAsync(
            new CallStatusChangedEvent(
                call.Id,
                call.AssignedAgentId,
                call.CustomerId,
                call.Status.ToString(),
                now),
            cancellationToken);

        return ToDto(call);
    }

    public async Task<TelephonyCallResponseDto> RejectCallAsync(
        Guid callId,
        Guid? actingAgentId,
        bool isPrivilegedCaller,
        CancellationToken cancellationToken = default)
    {
        var call = await GetCallAsync(callId, cancellationToken);

        EnsureCallAccess(call, actingAgentId, isPrivilegedCaller, allowUnassignedClaim: true);

        if (call.Status != CallStatus.Queued &&
            call.Status != CallStatus.Ringing)
        {
            throw new InvalidOperationException(
                $"A call in '{call.Status}' state cannot be rejected.");
        }

        var now = DateTime.UtcNow;
        call.Status = CallStatus.Rejected;
        call.EndedAt = now;
        call.UpdatedAt = now;

        await _telephonyProvider.RejectCallAsync(
            new TelephonyRejectCommand(call.Id, call.ProviderCallId, actingAgentId),
            cancellationToken);

        // Dequeue any active queue entries
        var queueEntries = await dbContext.CallQueueEntries
            .Where(x => x.CallId == call.Id && x.DequeuedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var entry in queueEntries)
        {
            entry.DequeuedAt = now;
        }

        // If an agent was assigned and was Busy, return to Available
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
            new CallStatusChangedEvent(
                call.Id,
                call.AssignedAgentId,
                call.CustomerId,
                call.Status.ToString(),
                now),
            cancellationToken);

        return ToDto(call);
    }

    public async Task<TelephonyCallResponseDto> EndCallAsync(
        Guid callId,
        Guid? actingAgentId,
        bool isPrivilegedCaller,
        CancellationToken cancellationToken = default)
    {
        var call = await GetCallAsync(callId, cancellationToken);

        EnsureCallAccess(call, actingAgentId, isPrivilegedCaller, allowUnassignedClaim: false);

        if (call.Status != CallStatus.Connected && call.Status != CallStatus.OnHold)
        {
            throw new InvalidOperationException(
                $"A call in '{call.Status}' state cannot be ended by telephony.");
        }

        var now = DateTime.UtcNow;
        call.Status = CallStatus.Completed;
        call.EndedAt = now;
        call.UpdatedAt = now;

        await _telephonyProvider.EndCallAsync(
            new TelephonyEndCommand(call.Id, call.ProviderCallId, actingAgentId),
            cancellationToken);

        dbContext.CallEvents.Add(new CallEvent
        {
            Id = Guid.NewGuid(),
            CallId = call.Id,
            AgentId = call.AssignedAgentId,
            EventType = "Ended",
            OccurredAt = now
        });

        // Return agent to Available
        if (call.AssignedAgentId.HasValue)
        {
            var agent = await dbContext.Agents
                .SingleOrDefaultAsync(x => x.Id == call.AssignedAgentId.Value, cancellationToken);
            if (agent is not null)
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
            new CallStatusChangedEvent(
                call.Id,
                call.AssignedAgentId,
                call.CustomerId,
                call.Status.ToString(),
                now),
            cancellationToken);

        if (call.AssignedAgentId.HasValue && queueService is not null)
        {
            await queueService.TryAutoAssignNextCallAsync(call.AssignedAgentId.Value, cancellationToken);
        }

        return ToDto(call);
    }

    public async Task<TelephonyCallResponseDto> CompleteCallAsync(
        Guid callId,
        CompleteCallRequestDto request,
        Guid? actingAgentId,
        bool isPrivilegedCaller,
        CancellationToken cancellationToken = default)
    {
        var call = await GetCallAsync(callId, cancellationToken);

        EnsureCallAccess(call, actingAgentId, isPrivilegedCaller, allowUnassignedClaim: false);

        if (call.Status != CallStatus.Connected && call.Status != CallStatus.OnHold)
        {
            throw new InvalidOperationException(
                $"A call in '{call.Status}' state cannot be completed.");
        }

        var disposition = await dbContext.CallDispositions
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.DispositionId && x.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException("The specified active call disposition was not found.");

        if (disposition.RequiresFollowUp)
        {
            if (!request.FollowUpAt.HasValue)
            {
                throw new ArgumentException("A follow-up date and time is required for this disposition.", nameof(request.FollowUpAt));
            }

            if (request.FollowUpAt.Value <= DateTime.UtcNow)
            {
                throw new ArgumentException("The follow-up date and time must be in the future.", nameof(request.FollowUpAt));
            }
        }

        if (disposition.RequiresNotes && string.IsNullOrWhiteSpace(request.Notes))
        {
            throw new ArgumentException("Call notes are required for this disposition.", nameof(request.Notes));
        }

        var now = DateTime.UtcNow;
        call.Complete(disposition.Id, now, request.Notes, request.FollowUpAt, request.FollowUpNotes);
        call.CallDisposition = disposition;

        var metadataJson = $"{{\"disposition\":\"{disposition.Code}\",\"name\":\"{disposition.Name}\",\"followUpAt\":\"{request.FollowUpAt:O}\"}}";
        dbContext.CallEvents.Add(new CallEvent
        {
            Id = Guid.NewGuid(),
            CallId = call.Id,
            AgentId = call.AssignedAgentId,
            EventType = "Completed",
            MetadataJson = metadataJson,
            OccurredAt = now
        });

        // Return agent to Available
        if (call.AssignedAgentId.HasValue)
        {
            var agent = await dbContext.Agents
                .SingleOrDefaultAsync(x => x.Id == call.AssignedAgentId.Value, cancellationToken);
            if (agent is not null)
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
            new CallStatusChangedEvent(
                call.Id,
                call.AssignedAgentId,
                call.CustomerId,
                call.Status.ToString(),
                now),
            cancellationToken);

        if (call.AssignedAgentId.HasValue && queueService is not null)
        {
            await queueService.TryAutoAssignNextCallAsync(call.AssignedAgentId.Value, cancellationToken);
        }

        return ToDto(call);
    }

    public async Task<TelephonyCallResponseDto> HoldCallAsync(
        Guid callId,
        Guid? actingAgentId,
        bool isPrivilegedCaller,
        CancellationToken cancellationToken = default)
    {
        var call = await GetCallAsync(callId, cancellationToken);
        EnsureCallAccess(call, actingAgentId, isPrivilegedCaller, allowUnassignedClaim: false);

        if (!_telephonyProvider.Capabilities.SupportsHoldResume)
        {
            throw new NotSupportedException($"The active telephony provider '{_telephonyProvider.ProviderName}' does not support hold/resume.");
        }

        if (call.Status != CallStatus.Connected)
        {
            throw new InvalidOperationException($"Only a connected call can be placed on hold. Current status: '{call.Status}'.");
        }

        var now = DateTime.UtcNow;
        call.Status = CallStatus.OnHold;
        call.UpdatedAt = now;

        await _telephonyProvider.HoldCallAsync(
            new TelephonyHoldCommand(call.Id, call.ProviderCallId, actingAgentId),
            cancellationToken);

        dbContext.CallEvents.Add(new CallEvent
        {
            Id = Guid.NewGuid(),
            CallId = call.Id,
            AgentId = call.AssignedAgentId,
            EventType = "Hold",
            MetadataJson = $"{{\"status\":\"OnHold\",\"heldAt\":\"{now:O}\"}}",
            OccurredAt = now
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        await realTimeNotifier.NotifyCallStatusChangedAsync(
            new CallStatusChangedEvent(call.Id, call.AssignedAgentId, call.CustomerId, call.Status.ToString(), now),
            cancellationToken);

        return ToDto(call);
    }

    public async Task<TelephonyCallResponseDto> ResumeCallAsync(
        Guid callId,
        Guid? actingAgentId,
        bool isPrivilegedCaller,
        CancellationToken cancellationToken = default)
    {
        var call = await GetCallAsync(callId, cancellationToken);
        EnsureCallAccess(call, actingAgentId, isPrivilegedCaller, allowUnassignedClaim: false);

        if (!_telephonyProvider.Capabilities.SupportsHoldResume)
        {
            throw new NotSupportedException($"The active telephony provider '{_telephonyProvider.ProviderName}' does not support hold/resume.");
        }

        if (call.Status != CallStatus.OnHold)
        {
            throw new InvalidOperationException($"Only a call on hold can be resumed. Current status: '{call.Status}'.");
        }


        var now = DateTime.UtcNow;

        var lastHoldEvent = await dbContext.CallEvents
            .Where(e => e.CallId == call.Id && e.EventType == "Hold")
            .OrderByDescending(e => e.OccurredAt)
            .FirstOrDefaultAsync(cancellationToken);

        var holdDurationSeconds = lastHoldEvent is not null
            ? Math.Max(0, (int)(now - lastHoldEvent.OccurredAt).TotalSeconds)
            : 0;

        call.Status = CallStatus.Connected;
        call.UpdatedAt = now;

        await _telephonyProvider.ResumeCallAsync(
            new TelephonyResumeCommand(call.Id, call.ProviderCallId, actingAgentId),
            cancellationToken);

        dbContext.CallEvents.Add(new CallEvent
        {
            Id = Guid.NewGuid(),
            CallId = call.Id,
            AgentId = call.AssignedAgentId,
            EventType = "Resumed",
            MetadataJson = $"{{\"status\":\"Connected\",\"resumedAt\":\"{now:O}\",\"holdDurationSeconds\":{holdDurationSeconds}}}",
            OccurredAt = now
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        await realTimeNotifier.NotifyCallStatusChangedAsync(
            new CallStatusChangedEvent(call.Id, call.AssignedAgentId, call.CustomerId, call.Status.ToString(), now),
            cancellationToken);

        return ToDto(call);
    }

    public async Task<IReadOnlyList<EligibleAgentDto>> GetEligibleTransferAgentsAsync(
        Guid callId,
        TransferType transferType,
        Guid? actingAgentId,
        bool isPrivilegedCaller,
        CancellationToken cancellationToken = default)
    {
        var call = await GetCallAsync(callId, cancellationToken);
        EnsureCallAccess(call, actingAgentId, isPrivilegedCaller, allowUnassignedClaim: false);

        var query = dbContext.Agents
            .AsNoTracking()
            .Include(a => a.User)
                .ThenInclude(u => u.Role)
            .Where(a => a.IsActive && a.Id != call.AssignedAgentId);

        if (transferType == TransferType.Supervisor)
        {
            query = query.Where(a => a.User.Role.Name == "Supervisor" || a.User.Role.Name == "Admin");
        }

        var agents = await query
            .OrderBy(a => a.DisplayName)
            .ToListAsync(cancellationToken);

        return agents.Select(a => new EligibleAgentDto
        {
            AgentId = a.Id,
            DisplayName = a.DisplayName,
            EmployeeCode = a.EmployeeCode,
            Team = a.Team,
            Status = a.Status,
            RoleName = a.User?.Role?.Name ?? "Agent",
            IsEligible = a.IsActive && a.Status == AgentStatus.Available
        }).ToList();
    }

    public async Task<TelephonyCallResponseDto> TransferCallAsync(
        Guid callId,
        TransferCallRequestDto request,
        Guid? actingAgentId,
        bool isPrivilegedCaller,
        CancellationToken cancellationToken = default)
    {
        var call = await GetCallAsync(callId, cancellationToken);
        EnsureCallAccess(call, actingAgentId, isPrivilegedCaller, allowUnassignedClaim: false);

        if (!_telephonyProvider.Capabilities.SupportsTransfer)
        {
            throw new NotSupportedException($"The active telephony provider '{_telephonyProvider.ProviderName}' does not support call transfer.");
        }

        if (request.TransferType == TransferType.Warm && !_telephonyProvider.Capabilities.SupportsWarmTransfer)
        {
            throw new NotSupportedException($"The active telephony provider '{_telephonyProvider.ProviderName}' does not support warm transfer.");
        }

        if (call.Status != CallStatus.Connected && call.Status != CallStatus.OnHold)
        {
            throw new InvalidOperationException($"Only a connected or on-hold call can be transferred. Current status: '{call.Status}'.");
        }

        if (!request.TargetAgentId.HasValue && !request.TargetQueueId.HasValue)
        {
            throw new ArgumentException("Either a TargetAgentId or TargetQueueId must be specified for transfer.");
        }

        var now = DateTime.UtcNow;
        var previousAgentId = call.AssignedAgentId;

        if (request.TargetAgentId.HasValue)
        {
            var targetAgent = await dbContext.Agents
                .Include(a => a.User)
                    .ThenInclude(u => u.Role)
                .SingleOrDefaultAsync(x => x.Id == request.TargetAgentId.Value, cancellationToken)
                ?? throw new KeyNotFoundException("The target agent was not found.");

            if (!targetAgent.IsActive || targetAgent.Status != AgentStatus.Available)
            {
                throw new InvalidOperationException("The target agent is not active or available for transfer.");
            }

            if (call.AssignedAgentId == targetAgent.Id)
            {
                throw new InvalidOperationException("The call is already assigned to the target agent.");
            }

            if (request.TransferType == TransferType.Supervisor)
            {
                if (targetAgent.User?.Role?.Name != "Supervisor" && targetAgent.User?.Role?.Name != "Admin")
                {
                    throw new InvalidOperationException("The selected target agent is not a Supervisor or Admin.");
                }
            }

            // Return previous agent to Available
            if (previousAgentId.HasValue)
            {
                var prevAgent = await dbContext.Agents.SingleOrDefaultAsync(x => x.Id == previousAgentId.Value, cancellationToken);
                if (prevAgent is not null && prevAgent.Status == AgentStatus.Busy)
                {
                    prevAgent.Status = AgentStatus.Available;
                    prevAgent.UpdatedAt = now;
                    await realTimeNotifier.NotifyAgentStatusChangedAsync(
                        new AgentStatusChangedEvent(prevAgent.Id, prevAgent.UserId, prevAgent.Status.ToString(), now),
                        cancellationToken);
                }
            }

            // Warm transfer logs initiation before handover
            if (request.TransferType == TransferType.Warm)
            {
                dbContext.CallEvents.Add(new CallEvent
                {
                    Id = Guid.NewGuid(),
                    CallId = call.Id,
                    AgentId = previousAgentId,
                    EventType = "TransferInitiated",
                    MetadataJson = $"{{\"transferType\":\"Warm\",\"from\":\"{previousAgentId}\",\"to\":\"{targetAgent.Id}\",\"reason\":\"{request.Reason}\"}}",
                    OccurredAt = now
                });
            }

            call.AssignedAgentId = targetAgent.Id;
            call.CallQueueId = null;
            call.Status = CallStatus.Ringing;
            call.UpdatedAt = now;

            dbContext.CallEvents.Add(new CallEvent
            {
                Id = Guid.NewGuid(),
                CallId = call.Id,
                AgentId = targetAgent.Id,
                EventType = "Transferred",
                MetadataJson = $"{{\"transferType\":\"{request.TransferType}\",\"from\":\"{previousAgentId}\",\"to\":\"{targetAgent.Id}\",\"reason\":\"{request.Reason}\"}}",
                OccurredAt = now
            });

            await _telephonyProvider.TransferCallAsync(
                new TelephonyTransferCommand(
                    call.Id,
                    call.ProviderCallId,
                    request.TransferType,
                    targetAgent.Id,
                    null,
                    null,
                    previousAgentId,
                    request.Reason),
                cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);

            if (auditLogService is not null)
            {
                await auditLogService.LogAsync(
                    actingAgentId,
                    "CallTransferred",
                    "Call",
                    call.Id.ToString(),
                    new { TransferType = request.TransferType.ToString(), FromAgentId = previousAgentId, ToAgentId = targetAgent.Id, Reason = request.Reason },
                    cancellationToken);
            }

            await realTimeNotifier.NotifyCallStatusChangedAsync(
                new CallStatusChangedEvent(call.Id, targetAgent.Id, call.CustomerId, call.Status.ToString(), now),
                cancellationToken);

            await realTimeNotifier.NotifyIncomingCallAsync(
                new IncomingCallEvent(call.Id, call.CustomerId, call.PhoneNumber, call.Direction.ToString(), call.Status.ToString(), now),
                cancellationToken);

            await realTimeNotifier.NotifyCallTransferredAsync(
                new CallTransferredEvent(call.Id, previousAgentId, targetAgent.Id, null, request.TransferType.ToString(), request.Reason, now),
                cancellationToken);

            if (previousAgentId.HasValue && queueService is not null)
            {
                await queueService.TryAutoAssignNextCallAsync(previousAgentId.Value, cancellationToken);
            }

            return ToDto(call);
        }
        else
        {
            var targetQueue = await dbContext.CallQueues.SingleOrDefaultAsync(x => x.Id == request.TargetQueueId!.Value && x.IsActive, cancellationToken)
                ?? throw new KeyNotFoundException("The target active call queue was not found.");

            if (previousAgentId.HasValue)
            {
                var prevAgent = await dbContext.Agents.SingleOrDefaultAsync(x => x.Id == previousAgentId.Value, cancellationToken);
                if (prevAgent is not null && prevAgent.Status == AgentStatus.Busy)
                {
                    prevAgent.Status = AgentStatus.Available;
                    prevAgent.UpdatedAt = now;
                    await realTimeNotifier.NotifyAgentStatusChangedAsync(
                        new AgentStatusChangedEvent(prevAgent.Id, prevAgent.UserId, prevAgent.Status.ToString(), now),
                        cancellationToken);
                }
            }

            call.AssignedAgentId = null;
            call.CallQueueId = targetQueue.Id;
            call.Status = CallStatus.Queued;
            call.UpdatedAt = now;

            var nextPosition = (await dbContext.CallQueueEntries
                .Where(x => x.CallQueueId == targetQueue.Id && x.DequeuedAt == null)
                .Select(x => (int?)x.Position)
                .MaxAsync(cancellationToken) ?? 0) + 1;

            dbContext.CallQueueEntries.Add(new CallQueueEntry
            {
                Id = Guid.NewGuid(),
                CallQueueId = targetQueue.Id,
                CallId = call.Id,
                Position = nextPosition,
                Priority = 0,
                EnqueuedAt = now
            });

            dbContext.CallEvents.Add(new CallEvent
            {
                Id = Guid.NewGuid(),
                CallId = call.Id,
                AgentId = previousAgentId,
                EventType = "TransferredToQueue",
                MetadataJson = $"{{\"queueId\":\"{targetQueue.Id}\",\"reason\":\"{request.Reason}\"}}",
                OccurredAt = now
            });

            await _telephonyProvider.TransferCallAsync(
                new TelephonyTransferCommand(
                    call.Id,
                    call.ProviderCallId,
                    TransferType.Blind,
                    null,
                    null,
                    targetQueue.Id,
                    previousAgentId,
                    request.Reason),
                cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);

            if (auditLogService is not null)
            {
                await auditLogService.LogAsync(
                    actingAgentId,
                    "CallTransferred",
                    "Call",
                    call.Id.ToString(),
                    new { TransferType = "Queue", FromAgentId = previousAgentId, ToQueueId = targetQueue.Id, Reason = request.Reason },
                    cancellationToken);
            }

            var waitingCount = await dbContext.CallQueueEntries.CountAsync(x => x.CallQueueId == targetQueue.Id && x.DequeuedAt == null, cancellationToken);
            await realTimeNotifier.NotifyQueueUpdatedAsync(new QueueUpdatedEvent(targetQueue.Id, waitingCount, now), cancellationToken);

            await realTimeNotifier.NotifyCallStatusChangedAsync(
                new CallStatusChangedEvent(call.Id, null, call.CustomerId, call.Status.ToString(), now),
                cancellationToken);

            await realTimeNotifier.NotifyCallTransferredAsync(
                new CallTransferredEvent(call.Id, previousAgentId, null, targetQueue.Id, "Queue", request.Reason, now),
                cancellationToken);

            if (previousAgentId.HasValue && queueService is not null)
            {
                await queueService.TryAutoAssignNextCallAsync(previousAgentId.Value, cancellationToken);
            }

            return ToDto(call);
        }
    }

    private static void EnsureCallAccess(
        Call call,
        Guid? actingAgentId,
        bool isPrivilegedCaller,
        bool allowUnassignedClaim)
    {
        if (isPrivilegedCaller)
        {
            return;
        }

        if (call.AssignedAgentId is null)
        {
            if (allowUnassignedClaim && actingAgentId.HasValue)
            {
                return;
            }

            throw new UnauthorizedAccessException(
                "Only an agent account can act on an unassigned call.");
        }

        if (actingAgentId != call.AssignedAgentId)
        {
            throw new UnauthorizedAccessException(
                "You are not the agent assigned to this call.");
        }
    }

    private async Task<Call> GetCallAsync(
        Guid callId,
        CancellationToken cancellationToken)
    {
        var call = await dbContext.Calls
            .SingleOrDefaultAsync(x => x.Id == callId, cancellationToken);

        return call ?? throw new KeyNotFoundException(
            "The specified call was not found.");
    }

    private async Task<Call?> FindByCorrelationAsync(
        string correlationId,
        CancellationToken cancellationToken)
    {
        var value = correlationId.Trim();

        return await dbContext.Calls
            .SingleOrDefaultAsync(x => x.CorrelationId == value, cancellationToken);
    }

    private async Task<Customer> ResolveCustomerAsync(
        Guid? customerId,
        string normalizedPhone,
        CancellationToken cancellationToken)
    {
        if (customerId.HasValue)
        {
            var customer = await dbContext.Customers
                .SingleOrDefaultAsync(
                    x => x.Id == customerId.Value,
                    cancellationToken);

            if (customer is null)
            {
                throw new KeyNotFoundException($"The specified customer '{customerId.Value}' was not found.");
            }

            return customer;
        }

        var existingCustomer = await dbContext.Customers
            .SingleOrDefaultAsync(
                x => x.PhoneNumber == normalizedPhone,
                cancellationToken);

        if (existingCustomer is not null)
        {
            return existingCustomer;
        }

        // Unknown customer handling: auto-provision new customer
        var newCustomer = new Customer
        {
            Id = Guid.NewGuid(),
            DisplayName = $"Unknown Caller ({normalizedPhone})",
            PhoneNumber = normalizedPhone,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.Customers.Add(newCustomer);
        await dbContext.SaveChangesAsync(cancellationToken);

        return newCustomer;
    }

    private async Task<CallQueue> GetDefaultQueueAsync(CancellationToken cancellationToken)
    {
        var queue = await dbContext.CallQueues
            .Where(x => x.IsActive)
            .OrderBy(x => x.Priority)
            .ThenBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (queue is not null) return queue;

        queue = new CallQueue
        {
            Id = Guid.NewGuid(),
            Name = "Main Inbound Queue",
            Priority = 1,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.CallQueues.Add(queue);
        await dbContext.SaveChangesAsync(cancellationToken);
        return queue;
    }

    public async Task<TelephonyProviderResult> StartRecordingAsync(
        Guid callId,
        Guid? actingAgentId,
        bool isPrivilegedCaller,
        CancellationToken cancellationToken = default)
    {
        var call = await GetCallAsync(callId, cancellationToken);
        EnsureCallAccess(call, actingAgentId, isPrivilegedCaller, allowUnassignedClaim: false);

        if (call.Status != CallStatus.Connected && call.Status != CallStatus.OnHold)
        {
            throw new InvalidOperationException($"Recording can only be started for an active connected or on-hold call. Current status: '{call.Status}'.");
        }

        var result = await _telephonyProvider.StartRecordingAsync(
            new TelephonyStartRecordingCommand(call.Id, call.ProviderCallId, actingAgentId),
            cancellationToken);

        if (result.Success)
        {
            var now = DateTime.UtcNow;
            dbContext.CallEvents.Add(new CallEvent
            {
                Id = Guid.NewGuid(),
                CallId = call.Id,
                AgentId = actingAgentId ?? call.AssignedAgentId,
                EventType = "RecordingStarted",
                MetadataJson = $"{{\"provider\":\"{_telephonyProvider.ProviderName}\",\"startedAt\":\"{now:O}\"}}",
                OccurredAt = now
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return result;
    }

    public async Task<TelephonyProviderResult> StopRecordingAsync(
        Guid callId,
        Guid? actingAgentId,
        bool isPrivilegedCaller,
        CancellationToken cancellationToken = default)
    {
        var call = await GetCallAsync(callId, cancellationToken);
        EnsureCallAccess(call, actingAgentId, isPrivilegedCaller, allowUnassignedClaim: false);

        var result = await _telephonyProvider.StopRecordingAsync(
            new TelephonyStopRecordingCommand(call.Id, call.ProviderCallId, null, actingAgentId),
            cancellationToken);

        if (result.Success)
        {
            var now = DateTime.UtcNow;
            dbContext.CallEvents.Add(new CallEvent
            {
                Id = Guid.NewGuid(),
                CallId = call.Id,
                AgentId = actingAgentId ?? call.AssignedAgentId,
                EventType = "RecordingStopped",
                MetadataJson = $"{{\"provider\":\"{_telephonyProvider.ProviderName}\",\"stoppedAt\":\"{now:O}\"}}",
                OccurredAt = now
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return result;
    }

    public async Task<bool> ProcessRecordingWebhookAsync(
        TelephonyWebhookPayload payload,
        CancellationToken cancellationToken = default)
    {
        var validation = _telephonyProvider.ValidateWebhookSecurity(payload);
        if (!validation.IsValid && validation.ErrorCode != "REPLAY_DUPLICATE")
        {
            throw new UnauthorizedAccessException(validation.ErrorMessage ?? "Recording webhook verification failed.");
        }

        var recEvent = await _telephonyProvider.ProcessRecordingWebhookAsync(payload, cancellationToken);
        if (recEvent is null)
        {
            return false;
        }

        var call = await dbContext.Calls
            .FirstOrDefaultAsync(x => x.ProviderCallId == recEvent.ProviderCallId, cancellationToken);

        if (call is null && Guid.TryParse(recEvent.ProviderCallId.Replace("SIM-", "").Replace("REAL-", "").Replace("CA", ""), out var parsedCallId))
        {
            call = await dbContext.Calls.FirstOrDefaultAsync(x => x.Id == parsedCallId, cancellationToken);
        }

        if (call is null)
        {
            return false;
        }

        // Idempotency: avoid duplicate recording insertion
        if (!string.IsNullOrWhiteSpace(recEvent.ProviderRecordingId))
        {
            var alreadyExists = await dbContext.CallRecordings
                .AnyAsync(r => r.ProviderRecordingId == recEvent.ProviderRecordingId, cancellationToken);
            if (alreadyExists)
            {
                return true;
            }
        }

        var recId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var ext = recEvent.ContentType?.Contains("mpeg") == true || recEvent.ContentType?.Contains("mp3") == true ? ".mp3" : ".wav";
        var storageKey = $"{now:yyyy/MM}/{call.Id:N}/{recId:N}{ext}";

        if (recordingStorage is not null)
        {
            var sampleBytes = new byte[] { 0x52, 0x49, 0x46, 0x46, 0x24, 0x00, 0x00, 0x00, 0x57, 0x41, 0x56, 0x66, 0x6d, 0x74, 0x20 };
            using var memStream = new MemoryStream(sampleBytes);
            storageKey = await recordingStorage.SaveAsync(memStream, storageKey, recEvent.ContentType ?? "audio/wav", cancellationToken);
        }

        var recording = new CallRecording
        {
            Id = recId,
            CallId = call.Id,
            ProviderRecordingId = recEvent.ProviderRecordingId,
            StorageKey = storageKey,
            StorageUrl = !string.IsNullOrWhiteSpace(recEvent.RecordingUrl) ? recEvent.RecordingUrl : $"/api/v1/recordings/{recId}/stream",
            StorageProvider = _telephonyProvider.ProviderName,
            Duration = recEvent.Duration,
            FileSizeBytes = recEvent.FileSizeBytes > 0 ? recEvent.FileSizeBytes : 16L,
            ContentType = recEvent.ContentType ?? "audio/wav",
            Status = RecordingStatus.Available,
            CreatedAt = recEvent.CreatedAt != default ? recEvent.CreatedAt : now,
            CompletedAt = now,
            RetentionUntil = now.AddDays(90),
            IsDeleted = false
        };

        dbContext.CallRecordings.Add(recording);
        dbContext.CallEvents.Add(new CallEvent
        {
            Id = Guid.NewGuid(),
            CallId = call.Id,
            AgentId = call.AssignedAgentId,
            EventType = "RecordingSaved",
            MetadataJson = $"{{\"recordingId\":\"{recording.Id}\",\"url\":\"{recording.StorageUrl}\",\"duration\":{recEvent.Duration.TotalSeconds}}}",
            OccurredAt = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<TelephonyCallResponseDto> ProcessInboundWebhookAsync(
        TelephonyWebhookPayload payload,
        CancellationToken cancellationToken = default)
    {
        var validation = _telephonyProvider.ValidateWebhookSecurity(payload);
        if (!validation.IsValid && validation.ErrorCode != "REPLAY_DUPLICATE")
        {
            throw new UnauthorizedAccessException(validation.ErrorMessage ?? "Inbound webhook verification failed.");
        }

        var inboundEvent = await _telephonyProvider.ProcessInboundWebhookAsync(payload, cancellationToken);
        if (inboundEvent is null)
        {
            throw new UnauthorizedAccessException("Inbound webhook verification failed or invalid payload.");
        }

        // Idempotency check: if call with this ProviderCallId was already received, return existing
        if (!string.IsNullOrWhiteSpace(inboundEvent.ProviderCallId))
        {
            var existing = await dbContext.Calls
                .FirstOrDefaultAsync(x => x.ProviderCallId == inboundEvent.ProviderCallId, cancellationToken);
            if (existing is not null)
            {
                return ToDto(existing);
            }
        }

        var normalizedPhone = NormalizePhone(inboundEvent.CallerPhoneNumber);
        if (string.IsNullOrWhiteSpace(normalizedPhone) || normalizedPhone.Length < 7)
        {
            normalizedPhone = string.IsNullOrWhiteSpace(inboundEvent.CallerPhoneNumber) ? "Unknown" : inboundEvent.CallerPhoneNumber;
        }

        var customer = await ResolveCustomerAsync(null, normalizedPhone, cancellationToken);
        var now = DateTime.UtcNow;
        var correlationId = !string.IsNullOrWhiteSpace(inboundEvent.CorrelationId)
            ? inboundEvent.CorrelationId.Trim()
            : $"INB-{Guid.NewGuid():N}";

        var call = new Call
        {
            Id = Guid.NewGuid(),
            CustomerId = customer.Id,
            Customer = customer,
            Direction = CallDirection.Inbound,
            Status = CallStatus.Queued,
            CorrelationId = correlationId,
            PhoneNumber = normalizedPhone,
            ProviderCallId = inboundEvent.ProviderCallId,
            StartedAt = inboundEvent.OccurredAt,
            CreatedAt = now
        };

        dbContext.Calls.Add(call);
        dbContext.CallEvents.Add(new CallEvent
        {
            Id = Guid.NewGuid(),
            CallId = call.Id,
            EventType = "Incoming",
            OccurredAt = now,
            MetadataJson = $"{{\"phone\":\"{normalizedPhone}\",\"customerId\":\"{customer.Id}\",\"provider\":\"{_telephonyProvider.ProviderName}\"}}"
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        // Run routing and auto-assignment
        await routingService.RouteCallAsync(call.Id, cancellationToken: cancellationToken);

        var routedCall = await dbContext.Calls
            .SingleAsync(x => x.Id == call.Id, cancellationToken);

        await realTimeNotifier.NotifyIncomingCallAsync(
            new IncomingCallEvent(
                routedCall.Id,
                routedCall.CustomerId,
                routedCall.PhoneNumber,
                routedCall.Direction.ToString(),
                routedCall.Status.ToString(),
                routedCall.StartedAt),
            cancellationToken);

        return ToDto(routedCall);
    }

    public async Task<TelephonyCallResponseDto?> ProcessCallStatusWebhookAsync(
        TelephonyWebhookPayload payload,
        CancellationToken cancellationToken = default)
    {
        var validation = _telephonyProvider.ValidateWebhookSecurity(payload);
        if (!validation.IsValid && validation.ErrorCode != "REPLAY_DUPLICATE")
        {
            throw new UnauthorizedAccessException(validation.ErrorMessage ?? "Call status webhook verification failed.");
        }


        var statusEvent = await _telephonyProvider.ProcessCallStatusWebhookAsync(payload, cancellationToken);
        if (statusEvent is null)
        {
            return null;
        }


        var call = await dbContext.Calls
            .FirstOrDefaultAsync(x => x.ProviderCallId == statusEvent.ProviderCallId, cancellationToken);

        if (call is null && Guid.TryParse(statusEvent.ProviderCallId.Replace("SIM-", "").Replace("REAL-", "").Replace("CA", ""), out var parsedId))
        {
            call = await dbContext.Calls.FirstOrDefaultAsync(x => x.Id == parsedId, cancellationToken);
        }

        if (call is null)
        {
            return null;
        }

        // Idempotency: if call is already in terminal state or already matches the requested status
        if (call.Status == statusEvent.Status || (call.IsTerminal && statusEvent.Status != CallStatus.Completed))
        {
            return ToDto(call);
        }

        var now = DateTime.UtcNow;
        var previousStatus = call.Status;

        switch (statusEvent.Status)
        {
            case CallStatus.Connected:
                if (call.Status == CallStatus.Queued || call.Status == CallStatus.Ringing)
                {
                    call.Status = CallStatus.Connected;
                    call.AnsweredAt ??= statusEvent.Timestamp;
                    call.UpdatedAt = now;

                    if (call.AssignedAgentId.HasValue)
                    {
                        var agent = await dbContext.Agents.SingleOrDefaultAsync(x => x.Id == call.AssignedAgentId.Value, cancellationToken);
                        if (agent is not null)
                        {
                            agent.Status = AgentStatus.Busy;
                            agent.UpdatedAt = now;
                            await realTimeNotifier.NotifyAgentStatusChangedAsync(
                                new AgentStatusChangedEvent(agent.Id, agent.UserId, agent.Status.ToString(), now),
                                cancellationToken);
                        }
                    }
                }
                break;

            case CallStatus.Completed:
                if (!call.IsTerminal)
                {
                    call.Status = CallStatus.Completed;
                    call.EndedAt ??= statusEvent.Timestamp;
                    call.UpdatedAt = now;

                    if (call.AssignedAgentId.HasValue)
                    {
                        var agent = await dbContext.Agents.SingleOrDefaultAsync(x => x.Id == call.AssignedAgentId.Value, cancellationToken);
                        if (agent is not null)
                        {
                            agent.Status = AgentStatus.Available;
                            agent.UpdatedAt = now;
                            await realTimeNotifier.NotifyAgentStatusChangedAsync(
                                new AgentStatusChangedEvent(agent.Id, agent.UserId, agent.Status.ToString(), now),
                                cancellationToken);
                        }
                    }
                }
                break;

            case CallStatus.Abandoned:
            case CallStatus.Rejected:
            case CallStatus.Failed:
                if (!call.IsTerminal)
                {
                    call.Status = statusEvent.Status;
                    call.EndedAt ??= statusEvent.Timestamp;
                    call.UpdatedAt = now;

                    if (call.AssignedAgentId.HasValue)
                    {
                        var agent = await dbContext.Agents.SingleOrDefaultAsync(x => x.Id == call.AssignedAgentId.Value, cancellationToken);
                        if (agent is not null && agent.Status == AgentStatus.Busy)
                        {
                            agent.Status = AgentStatus.Available;
                            agent.UpdatedAt = now;
                            await realTimeNotifier.NotifyAgentStatusChangedAsync(
                                new AgentStatusChangedEvent(agent.Id, agent.UserId, agent.Status.ToString(), now),
                                cancellationToken);
                        }
                    }
                }
                break;

            case CallStatus.OnHold:
                if (call.Status == CallStatus.Connected)
                {
                    call.Status = CallStatus.OnHold;
                    call.UpdatedAt = now;
                }
                break;

            case CallStatus.Ringing:
                if (call.Status == CallStatus.Queued)
                {
                    call.Status = CallStatus.Ringing;
                    call.UpdatedAt = now;
                }
                break;
        }

        // Add event if status changed
        if (call.Status != previousStatus)
        {
            dbContext.CallEvents.Add(new CallEvent
            {
                Id = Guid.NewGuid(),
                CallId = call.Id,
                AgentId = call.AssignedAgentId,
                EventType = $"StatusCallback:{call.Status}",
                OccurredAt = now,
                MetadataJson = $"{{\"fromStatus\":\"{previousStatus}\",\"toStatus\":\"{call.Status}\",\"rawStatus\":\"{statusEvent.RawStatus}\",\"duration\":{statusEvent.DurationSeconds ?? 0}}}"
            });

            await dbContext.SaveChangesAsync(cancellationToken);

            await realTimeNotifier.NotifyCallStatusChangedAsync(
                new CallStatusChangedEvent(
                    call.Id,
                    call.AssignedAgentId,
                    call.CustomerId,
                    call.Status.ToString(),
                    now),
                cancellationToken);

            if (call.IsTerminal && call.AssignedAgentId.HasValue && queueService is not null)
            {
                await queueService.TryAutoAssignNextCallAsync(call.AssignedAgentId.Value, cancellationToken);
            }
        }

        return ToDto(call);
    }


    public Task<TelephonyProviderInfoResponseDto> GetProviderInfoAsync(
        CancellationToken cancellationToken = default)
    {
        var available = providerFactory?.GetAvailableProviders()
            ?? new Dictionary<string, TelephonyProviderCapabilities>
            {
                [_telephonyProvider.ProviderName] = _telephonyProvider.Capabilities
            };

        var info = new TelephonyProviderInfoResponseDto
        {
            ActiveProvider = _telephonyProvider.ProviderName,
            Capabilities = _telephonyProvider.Capabilities,
            AvailableProviders = available
        };

        return Task.FromResult(info);
    }

    public async Task<TelephonyTokenResponseDto> GenerateVoiceTokenAsync(
        Guid actingAgentId,
        string agentIdentity,
        CancellationToken cancellationToken = default)
    {
        var teleOptions = options?.Value ?? new TelephonyOptions();
        var ttl = teleOptions.TokenTtlMinutes > 0 ? teleOptions.TokenTtlMinutes : 15;
        var effectiveIdentity = !string.IsNullOrWhiteSpace(agentIdentity)
            ? agentIdentity
            : (actingAgentId != Guid.Empty ? actingAgentId.ToString("N") : "agent");

        var token = await _telephonyProvider.GenerateClientTokenAsync(
            effectiveIdentity,
            ttl,
            cancellationToken);

        return new TelephonyTokenResponseDto
        {
            Token = token,
            Identity = effectiveIdentity,
            Provider = _telephonyProvider.ProviderName,
            VoiceNumber = teleOptions.VoiceNumber ?? teleOptions.DefaultCallerId,
            ExpiresInSeconds = ttl * 60
        };
    }

    private static string NormalizePhone(string phoneNumber) =>
        new(phoneNumber.Where(char.IsDigit).ToArray());

    private static TelephonyCallResponseDto ToDto(Call call) =>
        new()
        {
            CallId = call.Id,
            ProviderCallId = call.ProviderCallId ?? $"SIM-{call.Id:N}",
            Direction = call.Direction,
            Status = call.Status,
            PhoneNumber = call.PhoneNumber,
            CorrelationId = call.CorrelationId,
            CustomerId = call.CustomerId,
            AssignedAgentId = call.AssignedAgentId
        };
}
