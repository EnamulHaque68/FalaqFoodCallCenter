using CallCenter.Application.Audit;
using CallCenter.Application.RealTime;
using CallCenter.Application.RealTime.Contracts;
using CallCenter.Application.Routing;
using CallCenter.Application.Routing.DTOs;
using CallCenter.Application.Settings;
using CallCenter.Domain.Entities;
using CallCenter.Domain.Enums;
using CallCenter.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Infrastructure.Routing;

public sealed class RoutingService(
    CallCenterDbContext dbContext,
    IRealTimeNotifier realTimeNotifier,
    IRoutingStrategyFactory? strategyFactory = null,
    ISettingsService? settingsService = null,
    IAuditLogService? auditLogService = null) : IRoutingService
{
    private readonly IRoutingStrategyFactory routingStrategyFactory =
        strategyFactory ?? new RoutingStrategyFactory();

    private static readonly SemaphoreSlim RoutingLock = new(1, 1);

    public async Task<RoutingResultDto> RouteCallAsync(
        Guid callId,
        RoutingStrategyType? strategyType = null,
        CancellationToken cancellationToken = default)
    {
        var call = await dbContext.Calls
            .Include(x => x.CallQueue)
            .SingleOrDefaultAsync(x => x.Id == callId, cancellationToken)
            ?? throw new KeyNotFoundException("The specified call was not found.");

        if (call.Status is not CallStatus.Queued and not CallStatus.Ringing)
        {
            throw new InvalidOperationException($"Call in '{call.Status}' state cannot be routed.");
        }

        if (call.AssignedAgentId.HasValue)
        {
            return Result(call, "Call is already assigned.", strategyType?.ToString());
        }

        var now = DateTime.UtcNow;

        var autoAssignment = settingsService is not null
            ? await settingsService.GetValueAsync("AutoAssignmentEnabled", true, cancellationToken)
            : true;

        if (!autoAssignment)
        {
            var queue = await GetDefaultQueueAsync(cancellationToken);
            await EnsureQueuedAsync(call, queue.Id, cancellationToken);
            call.CallQueueId = queue.Id;
            call.Status = CallStatus.Queued;
            call.UpdatedAt = now;

            dbContext.CallEvents.Add(new CallEvent
            {
                Id = Guid.NewGuid(),
                CallId = call.Id,
                EventType = "Queued",
                OccurredAt = now,
                MetadataJson = $"{{\"queueId\":\"{queue.Id}\",\"queueName\":\"{queue.Name}\",\"reason\":\"AutoAssignmentDisabled\"}}"
            });

            await dbContext.SaveChangesAsync(cancellationToken);

            await realTimeNotifier.NotifyQueueUpdatedAsync(
                await BuildQueueEventAsync(queue.Id, cancellationToken), cancellationToken);
            await NotifyCallAsync(call, cancellationToken);

            return new RoutingResultDto
            {
                CallId = call.Id,
                QueueId = queue.Id,
                AgentId = null,
                CallStatus = call.Status,
                StrategyUsed = "None",
                Result = "Auto assignment disabled in system settings. Call enqueued."
            };
        }

        await RoutingLock.WaitAsync(cancellationToken);
        Agent? selectedAgent;
        RoutingStrategyType effectiveStrategyType;
        List<Guid> dequeuedQueueIds;

        try
        {
            // Re-verify call assignment under lock
            if (call.AssignedAgentId.HasValue)
            {
                return Result(call, "Call is already assigned.", strategyType?.ToString());
            }

            var availableAgents = await dbContext.Agents
                .Where(x => x.IsActive && x.Status == AgentStatus.Available)
                .Include(x => x.AssignedCalls)
                .ToListAsync(cancellationToken);

            if (availableAgents.Count == 0)
            {
                var queue = await GetDefaultQueueAsync(cancellationToken);

                await EnsureQueuedAsync(call, queue.Id, cancellationToken);
                call.CallQueueId = queue.Id;
                call.Status = CallStatus.Queued;
                call.UpdatedAt = now;
                await dbContext.SaveChangesAsync(cancellationToken);

                await realTimeNotifier.NotifyQueueUpdatedAsync(
                    await BuildQueueEventAsync(queue.Id, cancellationToken), cancellationToken);
                await NotifyCallAsync(call, cancellationToken);

                return new RoutingResultDto
                {
                    CallId = call.Id,
                    QueueId = queue.Id,
                    AgentId = null,
                    CallStatus = call.Status,
                    StrategyUsed = strategyType?.ToString() ?? "None",
                    Result = "No eligible agent was available. Call queued."
                };
            }

            var todayUtc = now.Date;
            var candidates = availableAgents.Select(agent =>
            {
                var activeCalls = agent.AssignedCalls.Count(c =>
                    c.Status == CallStatus.Ringing ||
                    c.Status == CallStatus.Connected ||
                    c.Status == CallStatus.OnHold);

                var completedToday = agent.AssignedCalls.Count(c =>
                    c.Status == CallStatus.Completed &&
                    c.StartedAt >= todayUtc);

                var lastEnded = agent.AssignedCalls
                    .Where(c => c.EndedAt.HasValue)
                    .OrderByDescending(c => c.EndedAt)
                    .Select(c => c.EndedAt)
                    .FirstOrDefault();

                return new RoutingCandidate
                {
                    Agent = agent,
                    ActiveCallsCount = activeCalls,
                    CompletedCallsTodayCount = completedToday,
                    LastCallEndedAt = lastEnded
                };
            }).ToList();

            var context = new RoutingContext
            {
                Call = call,
                Candidates = candidates,
                PreferredTeam = call.CallQueue?.Name,
                CallPriority = call.CallQueue?.Priority ?? 0
            };

            effectiveStrategyType = strategyType ?? RoutingStrategyType.LeastBusy;
            var strategy = routingStrategyFactory.GetStrategy(effectiveStrategyType);
            selectedAgent = strategy.SelectAgent(context);

            if (selectedAgent is null)
            {
                var queue = await GetDefaultQueueAsync(cancellationToken);

                await EnsureQueuedAsync(call, queue.Id, cancellationToken);
                call.CallQueueId = queue.Id;
                call.Status = CallStatus.Queued;
                call.UpdatedAt = now;

                dbContext.CallEvents.Add(new CallEvent
                {
                    Id = Guid.NewGuid(),
                    CallId = call.Id,
                    EventType = "Queued",
                    OccurredAt = now,
                    MetadataJson = $"{{\"queueId\":\"{queue.Id}\",\"queueName\":\"{queue.Name}\"}}"
                });

                await dbContext.SaveChangesAsync(cancellationToken);

                await realTimeNotifier.NotifyQueueUpdatedAsync(
                    await BuildQueueEventAsync(queue.Id, cancellationToken), cancellationToken);
                await NotifyCallAsync(call, cancellationToken);

                return new RoutingResultDto
                {
                    CallId = call.Id,
                    QueueId = queue.Id,
                    AgentId = null,
                    CallStatus = call.Status,
                    StrategyUsed = effectiveStrategyType.ToString(),
                    Result = "No eligible agent was matched by strategy. Call queued."
                };
            }

            call.AssignedAgentId = selectedAgent.Id;
            call.CallQueueId = null;
            call.Status = CallStatus.Ringing;
            call.UpdatedAt = now;

            dequeuedQueueIds = await DequeueActiveEntriesAsync(call.Id, cancellationToken);

            dbContext.CallEvents.Add(new CallEvent
            {
                Id = Guid.NewGuid(),
                CallId = call.Id,
                AgentId = selectedAgent.Id,
                EventType = "Assigned",
                OccurredAt = now,
                MetadataJson = $"{{\"agentId\":\"{selectedAgent.Id}\",\"agentName\":\"{selectedAgent.DisplayName}\",\"strategy\":\"{effectiveStrategyType}\"}}"
            });
            dbContext.CallEvents.Add(new CallEvent
            {
                Id = Guid.NewGuid(),
                CallId = call.Id,
                AgentId = selectedAgent.Id,
                EventType = "Ringing",
                OccurredAt = now,
                MetadataJson = $"{{\"agentId\":\"{selectedAgent.Id}\",\"agentName\":\"{selectedAgent.DisplayName}\"}}"
            });

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            RoutingLock.Release();
        }

        if (auditLogService is not null)
        {
            await auditLogService.LogAsync(
                null,
                "CallAssigned",
                "Call",
                call.Id.ToString(),
                new { selectedAgent.DisplayName, AgentId = selectedAgent.Id, Strategy = effectiveStrategyType.ToString() },
                cancellationToken);
        }

        foreach (var qId in dequeuedQueueIds)
        {
            await realTimeNotifier.NotifyQueueUpdatedAsync(
                await BuildQueueEventAsync(qId, cancellationToken), cancellationToken);
        }

        await realTimeNotifier.NotifyCallAssignedAsync(
            new CallAssignedEvent(
                call.Id,
                selectedAgent.Id,
                selectedAgent.DisplayName,
                call.CustomerId,
                call.Customer?.DisplayName,
                call.PhoneNumber,
                call.Direction.ToString(),
                call.Status.ToString(),
                DateTime.UtcNow),
            cancellationToken);

        await NotifyCallAsync(call, cancellationToken);

        return new RoutingResultDto
        {
            CallId = call.Id,
            QueueId = null,
            AgentId = selectedAgent.Id,
            CallStatus = call.Status,
            StrategyUsed = effectiveStrategyType.ToString(),
            Result = $"Call routed using {effectiveStrategyType} strategy to agent '{selectedAgent.DisplayName}'."
        };
    }

    public async Task<QueueEntryResponseDto> EnqueueCallAsync(
        EnqueueCallRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var call = await dbContext.Calls.SingleOrDefaultAsync(x => x.Id == request.CallId, cancellationToken)
            ?? throw new KeyNotFoundException("The specified call was not found.");

        var queue = await dbContext.CallQueues.SingleOrDefaultAsync(x => x.Id == request.QueueId && x.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException("The specified active call queue was not found.");

        if (call.Status is not CallStatus.Queued and not CallStatus.Ringing)
        {
            throw new InvalidOperationException($"Call in '{call.Status}' state cannot be queued.");
        }

        var existing = await dbContext.CallQueueEntries
            .SingleOrDefaultAsync(x => x.CallId == request.CallId && x.CallQueueId == request.QueueId && x.DequeuedAt == null, cancellationToken);

        if (existing is not null) return ToDto(existing);

        var nextPosition = (await dbContext.CallQueueEntries
            .Where(x => x.CallQueueId == queue.Id && x.DequeuedAt == null)
            .Select(x => (int?)x.Position)
            .MaxAsync(cancellationToken) ?? 0) + 1;

        var entry = new CallQueueEntry
        {
            Id = Guid.NewGuid(),
            CallQueueId = queue.Id,
            CallId = call.Id,
            Position = nextPosition,
            EnqueuedAt = DateTime.UtcNow
        };

        call.CallQueueId = queue.Id;
        call.Status = CallStatus.Queued;
        call.UpdatedAt = DateTime.UtcNow;

        dbContext.CallQueueEntries.Add(entry);
        dbContext.CallEvents.Add(new CallEvent
        {
            Id = Guid.NewGuid(),
            CallId = call.Id,
            EventType = "Queued",
            OccurredAt = DateTime.UtcNow,
            MetadataJson = $"{{\"queueId\":\"{queue.Id}\",\"queueName\":\"{queue.Name}\",\"position\":{entry.Position}}}"
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        await realTimeNotifier.NotifyQueueUpdatedAsync(
            await BuildQueueEventAsync(queue.Id, cancellationToken), cancellationToken);
        await NotifyCallAsync(call, cancellationToken);

        return ToDto(entry);
    }

    public async Task<RoutingResultDto> AssignCallAsync(
        Guid callId,
        AssignCallRequestDto request,
        CancellationToken cancellationToken = default)
    {
        await RoutingLock.WaitAsync(cancellationToken);
        List<Guid> queueIds;
        Call call;
        Agent agent;
        try
        {
            call = await dbContext.Calls.SingleOrDefaultAsync(x => x.Id == callId, cancellationToken)
                ?? throw new KeyNotFoundException("The specified call was not found.");

            agent = await dbContext.Agents.SingleOrDefaultAsync(x => x.Id == request.AgentId, cancellationToken)
                ?? throw new KeyNotFoundException("The specified agent was not found.");

            if (!agent.IsActive || agent.Status != AgentStatus.Available)
            {
                throw new InvalidOperationException("Only an active Available agent can receive a new call.");
            }

            if (call.Status is not CallStatus.Queued and not CallStatus.Ringing)
            {
                throw new InvalidOperationException($"Call in '{call.Status}' state cannot be assigned.");
            }

            call.AssignedAgentId = agent.Id;
            call.CallQueueId = null;
            call.Status = CallStatus.Ringing;
            call.UpdatedAt = DateTime.UtcNow;

            queueIds = await DequeueActiveEntriesAsync(call.Id, cancellationToken);

            dbContext.CallEvents.Add(new CallEvent
            {
                Id = Guid.NewGuid(),
                CallId = call.Id,
                AgentId = agent.Id,
                EventType = "Assigned",
                OccurredAt = DateTime.UtcNow,
                MetadataJson = $"{{\"agentId\":\"{agent.Id}\",\"agentName\":\"{agent.DisplayName}\",\"strategy\":\"ManualAssign\"}}"
            });
            dbContext.CallEvents.Add(new CallEvent
            {
                Id = Guid.NewGuid(),
                CallId = call.Id,
                AgentId = agent.Id,
                EventType = "Ringing",
                OccurredAt = DateTime.UtcNow,
                MetadataJson = $"{{\"agentId\":\"{agent.Id}\",\"agentName\":\"{agent.DisplayName}\"}}"
            });

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            RoutingLock.Release();
        }

        if (auditLogService is not null)
        {
            await auditLogService.LogAsync(
                null,
                "CallAssigned",
                "Call",
                call.Id.ToString(),
                new { agent.DisplayName, AgentId = agent.Id, Strategy = "ManualAssign" },
                cancellationToken);
        }

        foreach (var queueId in queueIds)
        {
            await realTimeNotifier.NotifyQueueUpdatedAsync(
                await BuildQueueEventAsync(queueId, cancellationToken), cancellationToken);
        }

        await realTimeNotifier.NotifyCallAssignedAsync(
            new CallAssignedEvent(
                call.Id,
                agent.Id,
                agent.DisplayName,
                call.CustomerId,
                call.Customer?.DisplayName,
                call.PhoneNumber,
                call.Direction.ToString(),
                call.Status.ToString(),
                DateTime.UtcNow),
            cancellationToken);

        await NotifyCallAsync(call, cancellationToken);

        return new RoutingResultDto
        {
            CallId = call.Id,
            AgentId = agent.Id,
            CallStatus = call.Status,
            StrategyUsed = "ManualAssign",
            Result = "Call assigned successfully."
        };
    }

    public async Task<RoutingResultDto> ReassignCallAsync(
        Guid callId,
        ReassignCallRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var call = await dbContext.Calls.SingleOrDefaultAsync(x => x.Id == callId, cancellationToken)
            ?? throw new KeyNotFoundException("The specified call was not found.");

        if (call.Status != CallStatus.Ringing || !call.AssignedAgentId.HasValue)
        {
            throw new InvalidOperationException("Only a ringing assigned call can be reassigned.");
        }

        var newAgent = await dbContext.Agents.SingleOrDefaultAsync(x => x.Id == request.AgentId, cancellationToken)
            ?? throw new KeyNotFoundException("The specified agent was not found.");

        if (!newAgent.IsActive || newAgent.Status != AgentStatus.Available)
        {
            throw new InvalidOperationException("Only an active Available agent can receive a reassigned call.");
        }

        if (newAgent.Id == call.AssignedAgentId.Value)
        {
            throw new InvalidOperationException("The call is already assigned to this agent.");
        }

        call.AssignedAgentId = newAgent.Id;
        call.UpdatedAt = DateTime.UtcNow;

        dbContext.CallEvents.Add(new CallEvent
        {
            Id = Guid.NewGuid(),
            CallId = call.Id,
            AgentId = newAgent.Id,
            EventType = "Assigned",
            OccurredAt = DateTime.UtcNow,
            MetadataJson = $"{{\"agentId\":\"{newAgent.Id}\",\"agentName\":\"{newAgent.DisplayName}\",\"strategy\":\"ManualReassign\"}}"
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        if (auditLogService is not null)
        {
            await auditLogService.LogAsync(
                null,
                "CallAssigned",
                "Call",
                call.Id.ToString(),
                new { newAgent.DisplayName, AgentId = newAgent.Id, Strategy = "ManualReassign" },
                cancellationToken);
        }

        await realTimeNotifier.NotifyCallAssignedAsync(
            new CallAssignedEvent(
                call.Id,
                newAgent.Id,
                newAgent.DisplayName,
                call.CustomerId,
                call.Customer?.DisplayName,
                call.PhoneNumber,
                call.Direction.ToString(),
                call.Status.ToString(),
                DateTime.UtcNow),
            cancellationToken);

        await NotifyCallAsync(call, cancellationToken);

        return new RoutingResultDto
        {
            CallId = call.Id,
            AgentId = newAgent.Id,
            CallStatus = call.Status,
            StrategyUsed = "ManualReassign",
            Result = "Call reassigned successfully."
        };
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

    private async Task EnsureQueuedAsync(Call call, Guid queueId, CancellationToken cancellationToken)
    {
        var existing = await dbContext.CallQueueEntries
            .AnyAsync(x => x.CallId == call.Id && x.CallQueueId == queueId && x.DequeuedAt == null, cancellationToken);

        if (existing) return;

        var next = (await dbContext.CallQueueEntries
            .Where(x => x.CallQueueId == queueId && x.DequeuedAt == null)
            .Select(x => (int?)x.Position)
            .MaxAsync(cancellationToken) ?? 0) + 1;

        dbContext.CallQueueEntries.Add(new CallQueueEntry
        {
            Id = Guid.NewGuid(),
            CallQueueId = queueId,
            CallId = call.Id,
            Position = next,
            EnqueuedAt = DateTime.UtcNow
        });
    }

    private async Task<List<Guid>> DequeueActiveEntriesAsync(Guid callId, CancellationToken cancellationToken)
    {
        var entries = await dbContext.CallQueueEntries
            .Where(x => x.CallId == callId && x.DequeuedAt == null)
            .ToListAsync(cancellationToken);

        var ids = entries.Select(x => x.CallQueueId).Distinct().ToList();
        var now = DateTime.UtcNow;
        foreach (var e in entries) e.DequeuedAt = now;
        return ids;
    }

    private async Task<QueueUpdatedEvent> BuildQueueEventAsync(Guid queueId, CancellationToken cancellationToken) =>
        new(queueId, await dbContext.CallQueueEntries.CountAsync(x => x.CallQueueId == queueId && x.DequeuedAt == null, cancellationToken), DateTime.UtcNow);

    private Task NotifyCallAsync(Call call, CancellationToken cancellationToken) =>
        realTimeNotifier.NotifyCallStatusChangedAsync(
            new CallStatusChangedEvent(call.Id, call.AssignedAgentId, call.CustomerId, call.Status.ToString(), DateTime.UtcNow), cancellationToken);

    private static RoutingResultDto Result(Call call, string result, string? strategy) =>
        new()
        {
            CallId = call.Id,
            QueueId = call.CallQueueId,
            AgentId = call.AssignedAgentId,
            CallStatus = call.Status,
            StrategyUsed = strategy ?? "None",
            Result = result
        };

    private static QueueEntryResponseDto ToDto(CallQueueEntry entry) =>
        new()
        {
            Id = entry.Id,
            QueueId = entry.CallQueueId,
            CallId = entry.CallId,
            Position = entry.Position,
            EnqueuedAt = entry.EnqueuedAt
        };
}
