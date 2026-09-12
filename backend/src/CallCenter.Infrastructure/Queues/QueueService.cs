using CallCenter.Application.Queues;
using CallCenter.Application.Queues.DTOs;
using CallCenter.Application.RealTime;
using CallCenter.Application.RealTime.Contracts;
using CallCenter.Application.Routing;
using CallCenter.Application.Routing.DTOs;
using CallCenter.Domain.Entities;
using CallCenter.Domain.Enums;
using CallCenter.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CallCenter.Infrastructure.Queues;

public sealed class QueueService(
    CallCenterDbContext dbContext,
    IRealTimeNotifier realTimeNotifier,
    IRoutingStrategyFactory routingStrategyFactory,
    ILogger<QueueService> logger)
    : IQueueService
{
    private static readonly SemaphoreSlim AutoAssignLock = new(1, 1);

    public async Task<IReadOnlyList<CallQueueDto>> GetQueuesAsync(CancellationToken cancellationToken = default)
    {
        var queues = await dbContext.CallQueues
            .Include(q => q.Entries.Where(e => e.DequeuedAt == null))
            .OrderByDescending(q => q.Priority)
            .ThenBy(q => q.Name)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        return queues.Select(q =>
        {
            var waiting = q.Entries.Where(e => e.DequeuedAt == null).ToList();
            var count = waiting.Count;
            var avgWait = count > 0 ? waiting.Average(e => Math.Max(0, (now - e.EnqueuedAt).TotalSeconds)) : 0.0;
            var maxWait = count > 0 ? waiting.Max(e => Math.Max(0, (now - e.EnqueuedAt).TotalSeconds)) : 0.0;

            return new CallQueueDto
            {
                Id = q.Id,
                Name = q.Name,
                Priority = q.Priority,
                IsActive = q.IsActive,
                WaitingCallsCount = count,
                AverageWaitSeconds = Math.Round(avgWait, 1),
                LongestWaitSeconds = Math.Round(maxWait, 1),
                CreatedAt = q.CreatedAt,
                UpdatedAt = q.UpdatedAt
            };
        }).ToList();
    }

    public async Task<QueueSummaryDto> GetQueueSummaryAsync(CancellationToken cancellationToken = default)
    {
        var totalQueues = await dbContext.CallQueues.CountAsync(cancellationToken);
        var activeQueues = await dbContext.CallQueues.CountAsync(q => q.IsActive, cancellationToken);
        var waitingEntries = await dbContext.CallQueueEntries
            .Where(e => e.DequeuedAt == null)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var totalWaiting = waitingEntries.Count;
        var avgWait = totalWaiting > 0
            ? waitingEntries.Average(e => Math.Max(0, (now - e.EnqueuedAt).TotalSeconds))
            : 0.0;
        var maxWait = totalWaiting > 0
            ? waitingEntries.Max(e => Math.Max(0, (now - e.EnqueuedAt).TotalSeconds))
            : 0.0;

        var availableAgents = await dbContext.Agents
            .CountAsync(a => a.IsActive && a.Status == AgentStatus.Available, cancellationToken);

        return new QueueSummaryDto
        {
            TotalQueues = totalQueues,
            ActiveQueues = activeQueues,
            TotalWaitingCalls = totalWaiting,
            AverageWaitSeconds = Math.Round(avgWait, 1),
            LongestWaitSeconds = Math.Round(maxWait, 1),
            AvailableAgentsCount = availableAgents
        };
    }

    public async Task<CallQueueDto?> GetQueueByIdAsync(Guid queueId, CancellationToken cancellationToken = default)
    {
        var queue = await dbContext.CallQueues
            .Include(q => q.Entries.Where(e => e.DequeuedAt == null))
            .SingleOrDefaultAsync(q => q.Id == queueId, cancellationToken);

        if (queue is null) return null;

        var now = DateTime.UtcNow;
        var waiting = queue.Entries.Where(e => e.DequeuedAt == null).ToList();
        var count = waiting.Count;
        var avgWait = count > 0 ? waiting.Average(e => Math.Max(0, (now - e.EnqueuedAt).TotalSeconds)) : 0.0;
        var maxWait = count > 0 ? waiting.Max(e => Math.Max(0, (now - e.EnqueuedAt).TotalSeconds)) : 0.0;

        return new CallQueueDto
        {
            Id = queue.Id,
            Name = queue.Name,
            Priority = queue.Priority,
            IsActive = queue.IsActive,
            WaitingCallsCount = count,
            AverageWaitSeconds = Math.Round(avgWait, 1),
            LongestWaitSeconds = Math.Round(maxWait, 1),
            CreatedAt = queue.CreatedAt,
            UpdatedAt = queue.UpdatedAt
        };
    }

    public async Task<CallQueueDto> CreateQueueAsync(CreateQueueRequestDto request, CancellationToken cancellationToken = default)
    {
        var exists = await dbContext.CallQueues
            .AnyAsync(q => q.Name == request.Name, cancellationToken);

        if (exists)
        {
            throw new InvalidOperationException($"A queue named '{request.Name}' already exists.");
        }

        var queue = new CallQueue
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Priority = request.Priority,
            IsActive = request.IsActive,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.CallQueues.Add(queue);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CallQueueDto
        {
            Id = queue.Id,
            Name = queue.Name,
            Priority = queue.Priority,
            IsActive = queue.IsActive,
            WaitingCallsCount = 0,
            AverageWaitSeconds = 0,
            LongestWaitSeconds = 0,
            CreatedAt = queue.CreatedAt
        };
    }

    public async Task<CallQueueDto> UpdateQueueAsync(Guid queueId, UpdateQueueRequestDto request, CancellationToken cancellationToken = default)
    {
        var queue = await dbContext.CallQueues
            .Include(q => q.Entries.Where(e => e.DequeuedAt == null))
            .SingleOrDefaultAsync(q => q.Id == queueId, cancellationToken)
            ?? throw new KeyNotFoundException("The specified queue was not found.");

        var nameConflict = await dbContext.CallQueues
            .AnyAsync(q => q.Id != queueId && q.Name == request.Name, cancellationToken);

        if (nameConflict)
        {
            throw new InvalidOperationException($"Another queue with name '{request.Name}' already exists.");
        }

        queue.Name = request.Name.Trim();
        queue.Priority = request.Priority;
        queue.IsActive = request.IsActive;
        queue.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var waiting = queue.Entries.Where(e => e.DequeuedAt == null).ToList();
        var count = waiting.Count;
        var avgWait = count > 0 ? waiting.Average(e => Math.Max(0, (now - e.EnqueuedAt).TotalSeconds)) : 0.0;
        var maxWait = count > 0 ? waiting.Max(e => Math.Max(0, (now - e.EnqueuedAt).TotalSeconds)) : 0.0;

        return new CallQueueDto
        {
            Id = queue.Id,
            Name = queue.Name,
            Priority = queue.Priority,
            IsActive = queue.IsActive,
            WaitingCallsCount = count,
            AverageWaitSeconds = Math.Round(avgWait, 1),
            LongestWaitSeconds = Math.Round(maxWait, 1),
            CreatedAt = queue.CreatedAt,
            UpdatedAt = queue.UpdatedAt
        };
    }

    public async Task<bool> DeleteQueueAsync(Guid queueId, CancellationToken cancellationToken = default)
    {
        var queue = await dbContext.CallQueues
            .Include(q => q.Entries.Where(e => e.DequeuedAt == null))
            .SingleOrDefaultAsync(q => q.Id == queueId, cancellationToken);

        if (queue is null) return false;

        if (queue.Entries.Any(e => e.DequeuedAt == null))
        {
            throw new InvalidOperationException("Cannot deactivate or delete a queue that currently contains waiting calls.");
        }

        queue.IsActive = false;
        queue.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<CallQueueEntryDto>> GetQueueEntriesAsync(Guid queueId, CancellationToken cancellationToken = default)
    {
        var entries = await dbContext.CallQueueEntries
            .Include(e => e.CallQueue)
            .Include(e => e.Call)
                .ThenInclude(c => c.Customer)
            .Where(e => e.CallQueueId == queueId && e.DequeuedAt == null)
            .OrderByDescending(e => e.Priority)
            .ThenBy(e => e.Position)
            .ThenBy(e => e.EnqueuedAt)
            .ToListAsync(cancellationToken);

        return entries.Select(ToEntryDto).ToList();
    }

    public async Task<CallQueueEntryDto> PrioritizeEntryAsync(Guid entryId, int newPriority, CancellationToken cancellationToken = default)
    {
        var entry = await dbContext.CallQueueEntries
            .Include(e => e.CallQueue)
            .Include(e => e.Call)
                .ThenInclude(c => c.Customer)
            .SingleOrDefaultAsync(e => e.Id == entryId && e.DequeuedAt == null, cancellationToken)
            ?? throw new KeyNotFoundException("The specified active queue entry was not found.");

        entry.Priority = newPriority;
        await dbContext.SaveChangesAsync(cancellationToken);

        await CompactPositionsAsync(entry.CallQueueId, cancellationToken);

        var updated = await dbContext.CallQueueEntries
            .Include(e => e.CallQueue)
            .Include(e => e.Call)
                .ThenInclude(c => c.Customer)
            .SingleAsync(e => e.Id == entryId, cancellationToken);

        var waitingCount = await dbContext.CallQueueEntries
            .CountAsync(e => e.CallQueueId == entry.CallQueueId && e.DequeuedAt == null, cancellationToken);

        await realTimeNotifier.NotifyQueueUpdatedAsync(
            new QueueUpdatedEvent(entry.CallQueueId, waitingCount, DateTime.UtcNow), cancellationToken);

        return ToEntryDto(updated);
    }

    public async Task<bool> CancelQueueEntryAsync(Guid callId, string? reason = null, CancellationToken cancellationToken = default)
    {
        var entry = await dbContext.CallQueueEntries
            .Include(e => e.Call)
            .SingleOrDefaultAsync(e => e.CallId == callId && e.DequeuedAt == null, cancellationToken);

        if (entry is null) return false;

        var now = DateTime.UtcNow;
        entry.DequeuedAt = now;

        if (entry.Call.Status is CallStatus.Queued or CallStatus.Ringing)
        {
            entry.Call.Status = CallStatus.Abandoned;
            entry.Call.EndedAt = now;
            entry.Call.UpdatedAt = now;
            if (!string.IsNullOrWhiteSpace(reason))
            {
                entry.Call.Notes = string.IsNullOrWhiteSpace(entry.Call.Notes)
                    ? $"Queue cancelled: {reason}"
                    : $"{entry.Call.Notes} | Queue cancelled: {reason}";
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await CompactPositionsAsync(entry.CallQueueId, cancellationToken);

        var waitingCount = await dbContext.CallQueueEntries
            .CountAsync(e => e.CallQueueId == entry.CallQueueId && e.DequeuedAt == null, cancellationToken);

        await realTimeNotifier.NotifyQueueUpdatedAsync(
            new QueueUpdatedEvent(entry.CallQueueId, waitingCount, now), cancellationToken);

        await realTimeNotifier.NotifyCallStatusChangedAsync(
            new CallStatusChangedEvent(
                entry.Call.Id,
                entry.Call.AssignedAgentId,
                entry.Call.CustomerId,
                entry.Call.Status.ToString(),
                now),
            cancellationToken);

        return true;
    }

    public async Task<bool> CompleteQueueEntryAsync(Guid callId, CancellationToken cancellationToken = default)
    {
        var entry = await dbContext.CallQueueEntries
            .SingleOrDefaultAsync(e => e.CallId == callId && e.DequeuedAt == null, cancellationToken);

        if (entry is null) return false;

        var now = DateTime.UtcNow;
        entry.DequeuedAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);
        await CompactPositionsAsync(entry.CallQueueId, cancellationToken);

        var waitingCount = await dbContext.CallQueueEntries
            .CountAsync(e => e.CallQueueId == entry.CallQueueId && e.DequeuedAt == null, cancellationToken);

        await realTimeNotifier.NotifyQueueUpdatedAsync(
            new QueueUpdatedEvent(entry.CallQueueId, waitingCount, now), cancellationToken);

        return true;
    }

    public async Task<RoutingResultDto?> TryAutoAssignNextCallAsync(
        Guid? availableAgentId = null,
        CancellationToken cancellationToken = default)
    {
        await AutoAssignLock.WaitAsync(cancellationToken);
        try
        {
            var agentQuery = dbContext.Agents.Where(a => a.IsActive && a.Status == AgentStatus.Available);
            if (availableAgentId.HasValue)
            {
                agentQuery = agentQuery.Where(a => a.Id == availableAgentId.Value);
            }

            var availableAgents = await agentQuery.ToListAsync(cancellationToken);
            if (availableAgents.Count == 0)
            {
                return null;
            }

            var nextEntryQuery = dbContext.CallQueueEntries
                .Include(e => e.CallQueue)
                .Include(e => e.Call)
                    .ThenInclude(c => c.Customer)
                .Where(e => e.DequeuedAt == null && e.CallQueue.IsActive && e.Call.Status == CallStatus.Queued);

            if (availableAgentId.HasValue)
            {
                nextEntryQuery = nextEntryQuery.Where(e => !dbContext.CallEvents.Any(ev =>
                    ev.CallId == e.CallId &&
                    ev.EventType == "TransferredToQueue" &&
                    ev.AgentId == availableAgentId.Value));
            }

            var nextEntry = await nextEntryQuery
                .OrderByDescending(e => e.CallQueue.Priority)
                .ThenByDescending(e => e.Priority)
                .ThenBy(e => e.Position)
                .ThenBy(e => e.EnqueuedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (nextEntry is null)
            {
                return null;
            }

            Agent selectedAgent;
            if (availableAgentId.HasValue)
            {
                selectedAgent = availableAgents[0];
            }
            else
            {
                var candidates = new List<RoutingCandidate>();
                var today = DateTime.UtcNow.Date;
                var transferringAgentIds = await dbContext.CallEvents
                    .Where(ev => ev.CallId == nextEntry.CallId && ev.EventType == "TransferredToQueue" && ev.AgentId != null)
                    .Select(ev => ev.AgentId!.Value)
                    .ToListAsync(cancellationToken);

                var eligibleAgents = availableAgents.Where(a => !transferringAgentIds.Contains(a.Id)).ToList();
                if (eligibleAgents.Count == 0)
                {
                    return null;
                }

                foreach (var a in eligibleAgents)
                {
                    var activeCalls = await dbContext.Calls.CountAsync(
                        c => c.AssignedAgentId == a.Id &&
                            (c.Status == CallStatus.Connected || c.Status == CallStatus.Ringing || c.Status == CallStatus.OnHold),
                        cancellationToken);

                    var completedToday = await dbContext.Calls.CountAsync(
                        c => c.AssignedAgentId == a.Id && c.Status == CallStatus.Completed && c.EndedAt >= today,
                        cancellationToken);

                    var lastCall = await dbContext.Calls
                        .Where(c => c.AssignedAgentId == a.Id && c.EndedAt != null)
                        .OrderByDescending(c => c.EndedAt)
                        .FirstOrDefaultAsync(cancellationToken);

                    candidates.Add(new RoutingCandidate
                    {
                        Agent = a,
                        ActiveCallsCount = activeCalls,
                        CompletedCallsTodayCount = completedToday,
                        LastCallEndedAt = lastCall?.EndedAt
                    });
                }

                var context = new RoutingContext
                {
                    Call = nextEntry.Call,
                    Candidates = candidates,
                    PreferredTeam = nextEntry.CallQueue.Name,
                    CallPriority = nextEntry.CallQueue.Priority
                };

                var strategy = routingStrategyFactory.GetStrategy(RoutingStrategyType.LeastBusy);
                selectedAgent = strategy.SelectAgent(context) ?? eligibleAgents[0];
            }

            var now = DateTime.UtcNow;

            // Atomic concurrency verification: ensure call and entry were not claimed concurrently
            if (nextEntry.DequeuedAt != null || nextEntry.Call.Status != CallStatus.Queued)
            {
                return null;
            }

            nextEntry.Call.AssignedAgentId = selectedAgent.Id;
            nextEntry.Call.CallQueueId = null;
            nextEntry.Call.Status = CallStatus.Ringing;
            nextEntry.Call.UpdatedAt = now;
            nextEntry.DequeuedAt = now;

            await dbContext.SaveChangesAsync(cancellationToken);

            await CompactPositionsAsync(nextEntry.CallQueueId, cancellationToken);

            var waitingCount = await dbContext.CallQueueEntries
                .CountAsync(e => e.CallQueueId == nextEntry.CallQueueId && e.DequeuedAt == null, cancellationToken);

            await realTimeNotifier.NotifyQueueUpdatedAsync(
                new QueueUpdatedEvent(nextEntry.CallQueueId, waitingCount, now), cancellationToken);

            await realTimeNotifier.NotifyCallStatusChangedAsync(
                new CallStatusChangedEvent(
                    nextEntry.Call.Id,
                    selectedAgent.Id,
                    nextEntry.Call.CustomerId,
                    nextEntry.Call.Status.ToString(),
                    now),
                cancellationToken);

            await realTimeNotifier.NotifyIncomingCallAsync(
                new IncomingCallEvent(
                    nextEntry.Call.Id,
                    nextEntry.Call.CustomerId,
                    nextEntry.Call.PhoneNumber,
                    nextEntry.Call.Direction.ToString(),
                    nextEntry.Call.Status.ToString(),
                    now),
                cancellationToken);

            await realTimeNotifier.NotifyCallAssignedAsync(
                new CallAssignedEvent(
                    nextEntry.Call.Id,
                    selectedAgent.Id,
                    selectedAgent.DisplayName,
                    nextEntry.Call.CustomerId,
                    nextEntry.Call.Customer?.DisplayName,
                    nextEntry.Call.PhoneNumber,
                    nextEntry.Call.Direction.ToString(),
                    nextEntry.Call.Status.ToString(),
                    now),
                cancellationToken);

            logger.LogInformation(
                "Auto-assigned call {CallId} from queue '{QueueName}' to agent '{AgentName}' ({AgentId}).",
                nextEntry.Call.Id, nextEntry.CallQueue.Name, selectedAgent.DisplayName, selectedAgent.Id);

            return new RoutingResultDto
            {
                CallId = nextEntry.Call.Id,
                QueueId = null,
                AgentId = selectedAgent.Id,
                CallStatus = nextEntry.Call.Status,
                StrategyUsed = "AutoQueueAssign",
                Result = $"Call automatically assigned to agent '{selectedAgent.DisplayName}' from queue '{nextEntry.CallQueue.Name}'."
            };
        }
        finally
        {
            AutoAssignLock.Release();
        }
    }

    private async Task CompactPositionsAsync(Guid queueId, CancellationToken cancellationToken)
    {
        var remaining = await dbContext.CallQueueEntries
            .Where(e => e.CallQueueId == queueId && e.DequeuedAt == null)
            .OrderByDescending(e => e.Priority)
            .ThenBy(e => e.Position)
            .ThenBy(e => e.EnqueuedAt)
            .ToListAsync(cancellationToken);

        int pos = 1;
        bool changed = false;
        foreach (var entry in remaining)
        {
            if (entry.Position != pos)
            {
                entry.Position = pos;
                changed = true;
            }
            pos++;
        }

        if (changed)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static CallQueueEntryDto ToEntryDto(CallQueueEntry entry)
    {
        var now = DateTime.UtcNow;
        var wait = Math.Max(0, (now - entry.EnqueuedAt).TotalSeconds);

        return new CallQueueEntryDto
        {
            Id = entry.Id,
            CallQueueId = entry.CallQueueId,
            QueueName = entry.CallQueue?.Name ?? "Queue",
            CallId = entry.CallId,
            Position = entry.Position,
            Priority = entry.Priority,
            EnqueuedAt = entry.EnqueuedAt,
            WaitSeconds = Math.Round(wait, 1),
            PhoneNumber = entry.Call?.PhoneNumber ?? string.Empty,
            CustomerId = entry.Call?.CustomerId,
            CustomerName = entry.Call?.Customer?.DisplayName,
            Direction = entry.Call?.Direction ?? CallDirection.Inbound,
            Status = entry.Call?.Status ?? CallStatus.Queued
        };
    }
}
