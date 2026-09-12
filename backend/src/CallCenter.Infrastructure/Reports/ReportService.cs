using CallCenter.Application.Agents.DTOs;
using CallCenter.Application.Reports;
using CallCenter.Application.Reports.DTOs;
using CallCenter.Domain.Entities;
using CallCenter.Domain.Enums;
using CallCenter.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Infrastructure.Reports;

public sealed class ReportService(CallCenterDbContext dbContext) : IReportService
{
    private const int MaximumReportRangeDays = 366;
    private const int MaximumExportRows = 10_000;
    public async Task<ReportMetricsDto> GetMetricsAsync(CancellationToken cancellationToken = default)
    {
        var totalCalls = await dbContext.Calls.CountAsync(cancellationToken);
        var incoming = await dbContext.Calls.CountAsync(x => x.Direction == CallDirection.Inbound, cancellationToken);
        var outgoing = await dbContext.Calls.CountAsync(x => x.Direction == CallDirection.Outbound, cancellationToken);
        var completed = await dbContext.Calls.CountAsync(x => x.Status == CallStatus.Completed, cancellationToken);
        var missed = await dbContext.Calls.CountAsync(x => x.Status == CallStatus.Abandoned, cancellationToken);
        var rejected = await dbContext.Calls.CountAsync(x => x.Status == CallStatus.Rejected, cancellationToken);

        var activeCalls = await dbContext.Calls.CountAsync(
            x => x.Status == CallStatus.Ringing || x.Status == CallStatus.Connected || x.Status == CallStatus.OnHold,
            cancellationToken);

        var durations = await dbContext.Calls
            .AsNoTracking()
            .Where(x => x.AnsweredAt != null && x.EndedAt != null)
            .Select(x => new { x.AnsweredAt, x.EndedAt })
            .ToListAsync(cancellationToken);

        var averageDurationSeconds = durations.Count == 0
            ? 0
            : Math.Round(durations.Average(x => (x.EndedAt!.Value - x.AnsweredAt!.Value).TotalSeconds), 1);

        var availableAgents = await dbContext.Agents.CountAsync(x => x.Status == AgentStatus.Available, cancellationToken);
        var busyAgents = await dbContext.Agents.CountAsync(x => x.Status == AgentStatus.Busy, cancellationToken);
        var awayAgents = await dbContext.Agents.CountAsync(x => x.Status == AgentStatus.Away, cancellationToken);
        var offlineAgents = await dbContext.Agents.CountAsync(x => x.Status == AgentStatus.Offline, cancellationToken);
        var totalAgents = await dbContext.Agents.CountAsync(cancellationToken);

        var queueSize = await dbContext.CallQueueEntries.CountAsync(x => x.DequeuedAt == null, cancellationToken);
        var disposedCallsCount = await dbContext.Calls.CountAsync(x => x.CallDispositionId != null, cancellationToken);
        var pendingFollowUpsCount = await dbContext.Calls.CountAsync(x => x.FollowUpAt != null && x.FollowUpAt > DateTime.UtcNow, cancellationToken);

        return new ReportMetricsDto
        {
            TotalCalls = totalCalls,
            Incoming = incoming,
            Outgoing = outgoing,
            Completed = completed,
            Missed = missed,
            Rejected = rejected,
            AverageDurationSeconds = averageDurationSeconds,
            AvailableAgents = availableAgents,
            BusyAgents = busyAgents,
            AwayAgents = awayAgents,
            OfflineAgents = offlineAgents,
            TotalAgents = totalAgents,
            ActiveCallsCount = activeCalls,
            QueueSize = queueSize,
            DisposedCallsCount = disposedCallsCount,
            PendingFollowUpsCount = pendingFollowUpsCount
        };
    }

    public async Task<OperationsDashboardDto> GetOperationsDashboardAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = DateTime.UtcNow;
        var todayStart = nowUtc.Date;
        var weekStart = todayStart.AddDays(-6);
        var monthStart = todayStart.AddDays(-29);

        var metrics = await GetMetricsAsync(cancellationToken);

        var recentCalls = await dbContext.Calls
            .AsNoTracking()
            .Where(c => c.StartedAt >= monthStart)
            .Select(c => new CallSummaryRecord(
                c.Id,
                c.Direction,
                c.Status,
                c.StartedAt,
                c.AnsweredAt,
                c.EndedAt
            ))
            .ToListAsync(cancellationToken);

        var dailyCalls = recentCalls.Where(c => c.StartedAt >= todayStart).ToList();
        var weeklyCalls = recentCalls.Where(c => c.StartedAt >= weekStart).ToList();
        var monthlyCalls = recentCalls;

        var dailyStats = CalculatePeriodStats("Today", dailyCalls);
        var weeklyStats = CalculatePeriodStats("Last 7 Days", weeklyCalls);
        var monthlyStats = CalculatePeriodStats("Last 30 Days", monthlyCalls);

        var trendPoints = new List<CallTrendPointDto>();
        for (var i = 0; i < 7; i++)
        {
            var date = weekStart.AddDays(i);
            var nextDate = date.AddDays(1);
            var dayCalls = recentCalls.Where(c => c.StartedAt >= date && c.StartedAt < nextDate).ToList();

            trendPoints.Add(new CallTrendPointDto
            {
                Date = date.ToString("yyyy-MM-dd"),
                PeriodLabel = date.ToString("ddd, MMM dd"),
                TotalCalls = dayCalls.Count,
                CompletedCalls = dayCalls.Count(c => c.Status == CallStatus.Completed),
                MissedCalls = dayCalls.Count(c => c.Status == CallStatus.Abandoned),
                RejectedCalls = dayCalls.Count(c => c.Status == CallStatus.Rejected),
                IncomingCalls = dayCalls.Count(c => c.Direction == CallDirection.Inbound),
                OutgoingCalls = dayCalls.Count(c => c.Direction == CallDirection.Outbound)
            });
        }

        var activeCalls = await dbContext.Calls
            .AsNoTracking()
            .Where(c => c.AssignedAgentId != null &&
                (c.Status == CallStatus.Ringing || c.Status == CallStatus.Connected || c.Status == CallStatus.OnHold))
            .Select(c => new { c.AssignedAgentId, c.PhoneNumber, c.StartedAt })
            .ToListAsync(cancellationToken);

        var activeCallByAgent = activeCalls
            .GroupBy(c => c.AssignedAgentId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var agents = await dbContext.Agents
            .AsNoTracking()
            .OrderBy(a => a.DisplayName)
            .ToListAsync(cancellationToken);

        var agentItems = agents.Select(a =>
        {
            activeCallByAgent.TryGetValue(a.Id, out var currentCall);

            return new AgentStatusItemDto
            {
                Id = a.Id,
                DisplayName = a.DisplayName,
                EmployeeCode = a.EmployeeCode,
                Team = a.Team,
                Status = a.Status,
                IsActive = a.IsActive,
                CurrentCallPhoneNumber = currentCall?.PhoneNumber,
                CurrentCallStartedAt = currentCall?.StartedAt
            };
        }).ToList();

        var agentStatusSummary = new AgentStatusSummaryDto
        {
            Available = agents.Count(a => a.Status == AgentStatus.Available),
            Busy = agents.Count(a => a.Status == AgentStatus.Busy),
            Away = agents.Count(a => a.Status == AgentStatus.Away),
            Offline = agents.Count(a => a.Status == AgentStatus.Offline),
            Total = agents.Count,
            Agents = agentItems
        };

        var queueEntries = await dbContext.CallQueueEntries
            .AsNoTracking()
            .Include(e => e.Call)
            .Include(e => e.CallQueue)
            .Where(e => e.DequeuedAt == null)
            .OrderBy(e => e.Position)
            .ThenBy(e => e.EnqueuedAt)
            .ToListAsync(cancellationToken);

        var waitDurations = queueEntries
            .Select(e => Math.Max(0, (nowUtc - e.EnqueuedAt).TotalSeconds))
            .ToList();

        var avgWait = waitDurations.Count == 0 ? 0 : Math.Round(waitDurations.Average(), 1);
        var longestWait = waitDurations.Count == 0 ? 0 : Math.Round(waitDurations.Max(), 1);

        var queueItems = queueEntries.Select(e => new QueueItemSummaryDto
        {
            CallId = e.CallId,
            PhoneNumber = e.Call != null ? e.Call.PhoneNumber : "Unknown",
            Position = e.Position,
            EnqueuedAt = e.EnqueuedAt,
            QueueName = e.CallQueue != null ? e.CallQueue.Name : "Main Queue"
        }).ToList();

        var queueStatusSummary = new QueueStatusSummaryDto
        {
            TotalWaiting = queueEntries.Count,
            AverageWaitSeconds = avgWait,
            LongestWaitSeconds = longestWait,
            Entries = queueItems
        };

        var completedCallsWithDisp = await dbContext.Calls
            .AsNoTracking()
            .Where(x => x.Status == CallStatus.Completed && x.CallDispositionId != null)
            .Include(x => x.CallDisposition)
            .Select(x => new
            {
                x.CallDispositionId,
                Code = x.CallDisposition != null ? x.CallDisposition.Code : "UNKNOWN",
                Name = x.CallDisposition != null ? x.CallDisposition.Name : "Unknown",
                RequiresFollowUp = x.CallDisposition != null && x.CallDisposition.RequiresFollowUp,
                DurationSeconds = x.AnsweredAt != null && x.EndedAt != null
                    ? (x.EndedAt.Value - x.AnsweredAt.Value).TotalSeconds
                    : 0
            })
            .ToListAsync(cancellationToken);

        var totalDisposed = completedCallsWithDisp.Count;
        var dispositionBreakdowns = completedCallsWithDisp
            .GroupBy(x => new { x.CallDispositionId, x.Code, x.Name, x.RequiresFollowUp })
            .Select(g => new DispositionBreakdownDto
            {
                DispositionId = g.Key.CallDispositionId!.Value,
                DispositionCode = g.Key.Code,
                DispositionName = g.Key.Name,
                CallCount = g.Count(),
                Percentage = totalDisposed == 0 ? 0 : Math.Round((double)g.Count() / totalDisposed * 100, 1),
                AverageDurationSeconds = g.Any() ? Math.Round(g.Average(x => x.DurationSeconds), 1) : 0,
                RequiresFollowUp = g.Key.RequiresFollowUp
            })
            .OrderByDescending(x => x.CallCount)
            .ToList();

        var pendingFollowUpsCount = await dbContext.Calls
            .CountAsync(x => x.FollowUpAt != null && x.FollowUpAt > nowUtc, cancellationToken);

        return new OperationsDashboardDto
        {
            Metrics = metrics,
            DailyStats = dailyStats,
            WeeklyStats = weeklyStats,
            MonthlyStats = monthlyStats,
            CallTrends = trendPoints,
            AgentStatus = agentStatusSummary,
            QueueStatus = queueStatusSummary,
            DispositionBreakdown = dispositionBreakdowns,
            PendingFollowUpsCount = pendingFollowUpsCount,
            GeneratedAt = nowUtc
        };
    }

    public async Task<DispositionReportDto> GetDispositionReportAsync(
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var nowUtc = DateTime.UtcNow;
        var query = dbContext.Calls
            .AsNoTracking()
            .Where(c => c.Status == CallStatus.Completed);

        if (fromUtc.HasValue)
        {
            query = query.Where(c => c.EndedAt >= fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            query = query.Where(c => c.EndedAt <= toUtc.Value);
        }

        var completedCalls = await query
            .Include(c => c.CallDisposition)
            .Select(c => new
            {
                c.Id,
                c.CallDispositionId,
                Code = c.CallDisposition != null ? c.CallDisposition.Code : null,
                Name = c.CallDisposition != null ? c.CallDisposition.Name : null,
                RequiresFollowUp = c.CallDisposition != null && c.CallDisposition.RequiresFollowUp,
                DurationSeconds = c.AnsweredAt != null && c.EndedAt != null
                    ? (c.EndedAt.Value - c.AnsweredAt.Value).TotalSeconds
                    : 0
            })
            .ToListAsync(cancellationToken);

        var totalCompleted = completedCalls.Count;
        var disposedCalls = completedCalls.Where(c => c.CallDispositionId != null).ToList();
        var totalDisposed = disposedCalls.Count;

        var breakdowns = disposedCalls
            .GroupBy(c => new { c.CallDispositionId, c.Code, c.Name, c.RequiresFollowUp })
            .Select(g => new DispositionBreakdownDto
            {
                DispositionId = g.Key.CallDispositionId!.Value,
                DispositionCode = g.Key.Code ?? "UNKNOWN",
                DispositionName = g.Key.Name ?? "Unknown",
                CallCount = g.Count(),
                Percentage = totalDisposed == 0 ? 0 : Math.Round((double)g.Count() / totalDisposed * 100, 1),
                AverageDurationSeconds = g.Any() ? Math.Round(g.Average(c => c.DurationSeconds), 1) : 0,
                RequiresFollowUp = g.Key.RequiresFollowUp
            })
            .OrderByDescending(b => b.CallCount)
            .ToList();

        var upcomingFollowUps = await dbContext.Calls
            .AsNoTracking()
            .Include(c => c.Customer)
            .Include(c => c.AssignedAgent)
            .Include(c => c.CallDisposition)
            .Where(c => c.FollowUpAt != null && c.FollowUpAt > nowUtc)
            .OrderBy(c => c.FollowUpAt)
            .Take(20)
            .Select(c => new PendingFollowUpCallDto
            {
                CallId = c.Id,
                CustomerId = c.CustomerId,
                CustomerName = c.Customer != null ? c.Customer.DisplayName : "Unknown",
                PhoneNumber = c.PhoneNumber,
                AssignedAgentId = c.AssignedAgentId,
                AgentName = c.AssignedAgent != null ? c.AssignedAgent.DisplayName : "Unassigned",
                DispositionName = c.CallDisposition != null ? c.CallDisposition.Name : "Follow Up Required",
                FollowUpAt = c.FollowUpAt,
                FollowUpNotes = c.FollowUpNotes,
                CallEndedAt = c.EndedAt ?? c.UpdatedAt ?? nowUtc
            })
            .ToListAsync(cancellationToken);

        var totalFollowUpsScheduled = await dbContext.Calls
            .CountAsync(c => c.FollowUpAt != null, cancellationToken);

        return new DispositionReportDto
        {
            TotalCompletedCalls = totalCompleted,
            TotalDisposedCalls = totalDisposed,
            FollowUpsScheduledCount = totalFollowUpsScheduled,
            Breakdowns = breakdowns,
            UpcomingFollowUps = upcomingFollowUps,
            GeneratedAt = nowUtc
        };
    }

    private static PeriodStatsDto CalculatePeriodStats(string periodName, IReadOnlyList<CallSummaryRecord> calls)
    {
        var total = calls.Count;
        var incoming = calls.Count(c => c.Direction == CallDirection.Inbound);
        var outgoing = calls.Count(c => c.Direction == CallDirection.Outbound);
        var completed = calls.Count(c => c.Status == CallStatus.Completed);
        var missed = calls.Count(c => c.Status == CallStatus.Abandoned);
        var rejected = calls.Count(c => c.Status == CallStatus.Rejected);

        var completedCalls = calls
            .Where(c => c.AnsweredAt != null && c.EndedAt != null)
            .ToList();

        var avgSec = completedCalls.Count == 0
            ? 0
            : Math.Round(completedCalls.Average(c => (c.EndedAt!.Value - c.AnsweredAt!.Value).TotalSeconds), 1);

        var rate = total == 0
            ? 0
            : Math.Round((double)completed / total * 100.0, 1);

        return new PeriodStatsDto
        {
            PeriodName = periodName,
            TotalCalls = total,
            Incoming = incoming,
            Outgoing = outgoing,
            Completed = completed,
            Missed = missed,
            Rejected = rejected,
            AverageDurationSeconds = avgSec,
            CompletionRatePercent = rate
        };
    }

    public async Task<ComprehensiveAnalyticsReportDto> GetAnalyticsReportAsync(
        string? preset = null,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var (resolvedFrom, resolvedTo, periodName) = ResolvePeriod(preset, fromUtc, toUtc);
        var now = DateTime.UtcNow;

        var query = dbContext.Calls
            .AsNoTracking()
            .Where(c => c.StartedAt >= resolvedFrom && c.StartedAt <= resolvedTo);

        // 1. Call Volume & Mix
        var totalCalls = await query.CountAsync(cancellationToken);

        var dirCounts = await query
            .GroupBy(c => c.Direction)
            .Select(g => new { Direction = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var incoming = dirCounts.FirstOrDefault(x => x.Direction == CallDirection.Inbound)?.Count ?? 0;
        var outgoing = dirCounts.FirstOrDefault(x => x.Direction == CallDirection.Outbound)?.Count ?? 0;

        var statusCounts = await query
            .GroupBy(c => c.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var completed = statusCounts.FirstOrDefault(x => x.Status == CallStatus.Completed)?.Count ?? 0;
        var missed = statusCounts.FirstOrDefault(x => x.Status == CallStatus.Abandoned)?.Count ?? 0;
        var rejected = statusCounts.FirstOrDefault(x => x.Status == CallStatus.Rejected)?.Count ?? 0;

        var durationRecords = await query
            .Where(c => c.AnsweredAt != null && c.EndedAt != null)
            .Select(c => new { c.AnsweredAt, c.EndedAt })
            .ToListAsync(cancellationToken);

        var totalTalkTimeSeconds = durationRecords.Sum(d => (d.EndedAt!.Value - d.AnsweredAt!.Value).TotalSeconds);
        var avgDurationSeconds = durationRecords.Count == 0
            ? 0
            : Math.Round(totalTalkTimeSeconds / durationRecords.Count, 1);

        var volume = new CallVolumeReportDto
        {
            TotalCalls = totalCalls,
            Incoming = incoming,
            Outgoing = outgoing,
            Completed = completed,
            Missed = missed,
            Rejected = rejected,
            AverageDurationSeconds = avgDurationSeconds,
            TotalTalkTimeSeconds = Math.Round(totalTalkTimeSeconds, 1)
        };

        // 2. Agent Performance
        var agentGrouped = await query
            .Where(c => c.AssignedAgentId != null)
            .GroupBy(c => new { c.AssignedAgentId, c.AssignedAgent!.DisplayName, c.AssignedAgent!.EmployeeCode, c.AssignedAgent!.Team })
            .Select(g => new
            {
                AgentId = g.Key.AssignedAgentId!.Value,
                AgentName = g.Key.DisplayName,
                EmployeeCode = g.Key.EmployeeCode,
                Team = g.Key.Team,
                Total = g.Count(),
                Completed = g.Count(c => c.Status == CallStatus.Completed),
                Missed = g.Count(c => c.Status == CallStatus.Abandoned),
                Rejected = g.Count(c => c.Status == CallStatus.Rejected)
            })
            .ToListAsync(cancellationToken);

        var agentDurations = await query
            .Where(c => c.AssignedAgentId != null && c.AnsweredAt != null && c.EndedAt != null)
            .Select(c => new { c.AssignedAgentId, Duration = (c.EndedAt!.Value - c.AnsweredAt!.Value).TotalSeconds })
            .ToListAsync(cancellationToken);

        var agentDurationsMap = agentDurations
            .GroupBy(x => x.AssignedAgentId!.Value)
            .ToDictionary(g => g.Key, g => new { Total = g.Sum(x => x.Duration), Avg = g.Average(x => x.Duration) });

        var agentReports = agentGrouped
            .Select(a =>
            {
                var durationInfo = agentDurationsMap.TryGetValue(a.AgentId, out var d) ? d : null;
                var talkSec = durationInfo != null ? durationInfo.Total : 0;
                var avgSec = durationInfo != null ? Math.Round(durationInfo.Avg, 1) : 0;
                var rate = a.Total == 0 ? 0 : Math.Round((double)a.Completed / a.Total * 100.0, 1);

                return new AgentPerformanceReportDto
                {
                    AgentId = a.AgentId,
                    AgentName = a.AgentName,
                    EmployeeCode = a.EmployeeCode,
                    Team = a.Team,
                    TotalCalls = a.Total,
                    CompletedCalls = a.Completed,
                    MissedCalls = a.Missed,
                    RejectedCalls = a.Rejected,
                    TotalTalkTimeSeconds = Math.Round(talkSec, 1),
                    AverageTalkTimeSeconds = avgSec,
                    CompletionRatePercent = rate
                };
            })
            .OrderByDescending(a => a.TotalCalls)
            .ToList();

        // 3. Queue Performance
        var queueEntries = await dbContext.CallQueueEntries
            .AsNoTracking()
            .Include(e => e.CallQueue)
            .Include(e => e.Call)
            .Where(e => e.EnqueuedAt >= resolvedFrom && e.EnqueuedAt <= resolvedTo)
            .Select(e => new
            {
                e.CallQueueId,
                QueueName = e.CallQueue != null ? e.CallQueue.Name : "Main Queue",
                e.EnqueuedAt,
                e.DequeuedAt,
                CallStatus = e.Call != null ? e.Call.Status : CallStatus.Queued
            })
            .ToListAsync(cancellationToken);

        var currentWaitingByQueue = await dbContext.CallQueueEntries
            .AsNoTracking()
            .Where(e => e.DequeuedAt == null)
            .GroupBy(e => e.CallQueueId)
            .Select(g => new { QueueId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.QueueId, x => x.Count, cancellationToken);

        var queueReports = queueEntries
            .GroupBy(e => new { e.CallQueueId, e.QueueName })
            .Select(g =>
            {
                var totalEnqueued = g.Count();
                var answered = g.Count(x => x.CallStatus == CallStatus.Connected || x.CallStatus == CallStatus.Completed);
                var abandoned = g.Count(x => x.CallStatus == CallStatus.Abandoned);

                var waitTimes = g
                    .Where(x => x.DequeuedAt != null)
                    .Select(x => Math.Max(0, (x.DequeuedAt!.Value - x.EnqueuedAt).TotalSeconds))
                    .ToList();

                var avgWait = waitTimes.Count == 0 ? 0 : Math.Round(waitTimes.Average(), 1);
                var maxWait = waitTimes.Count == 0 ? 0 : Math.Round(waitTimes.Max(), 1);
                var currentWaiting = currentWaitingByQueue.TryGetValue(g.Key.CallQueueId, out var c) ? c : 0;

                return new QueuePerformanceReportDto
                {
                    QueueId = g.Key.CallQueueId,
                    QueueName = g.Key.QueueName,
                    TotalEnqueued = totalEnqueued,
                    AnsweredCalls = answered,
                    AbandonedCalls = abandoned,
                    AverageWaitSeconds = avgWait,
                    MaxWaitSeconds = maxWait,
                    CurrentWaiting = currentWaiting
                };
            })
            .OrderByDescending(q => q.TotalEnqueued)
            .ToList();

        // 4. Disposition Statistics
        var dispCalls = await query
            .Where(c => c.CallDispositionId != null)
            .Include(c => c.CallDisposition)
            .Select(c => new
            {
                c.CallDispositionId,
                Code = c.CallDisposition != null ? c.CallDisposition.Code : "UNKNOWN",
                Name = c.CallDisposition != null ? c.CallDisposition.Name : "Unknown",
                RequiresFollowUp = c.CallDisposition != null && c.CallDisposition.RequiresFollowUp,
                Duration = c.AnsweredAt != null && c.EndedAt != null
                    ? (c.EndedAt.Value - c.AnsweredAt.Value).TotalSeconds
                    : 0
            })
            .ToListAsync(cancellationToken);

        var totalDisposed = dispCalls.Count;
        var dispositionReports = dispCalls
            .GroupBy(d => new { d.CallDispositionId, d.Code, d.Name, d.RequiresFollowUp })
            .Select(g =>
            {
                var count = g.Count();
                var pct = totalDisposed == 0 ? 0 : Math.Round((double)count / totalDisposed * 100.0, 1);
                var totalDur = g.Sum(x => x.Duration);
                var avgDur = count == 0 ? 0 : Math.Round(g.Average(x => x.Duration), 1);

                return new DispositionStatDto
                {
                    DispositionId = g.Key.CallDispositionId!.Value,
                    DispositionCode = g.Key.Code,
                    DispositionName = g.Key.Name,
                    CallCount = count,
                    Percentage = pct,
                    TotalTalkTimeSeconds = Math.Round(totalDur, 1),
                    AverageDurationSeconds = avgDur,
                    RequiresFollowUp = g.Key.RequiresFollowUp
                };
            })
            .OrderByDescending(d => d.CallCount)
            .ToList();

        // 5. Charts
        var callsByDirection = dirCounts
            .Select(d => new DistributionItemDto
            {
                Key = d.Direction.ToString(),
                Label = d.Direction == CallDirection.Inbound ? "Inbound" : "Outbound",
                Count = d.Count,
                Percentage = totalCalls == 0 ? 0 : Math.Round((double)d.Count / totalCalls * 100.0, 1)
            })
            .ToList();

        var callsByStatus = statusCounts
            .Select(s => new DistributionItemDto
            {
                Key = s.Status.ToString(),
                Label = s.Status.ToString(),
                Count = s.Count,
                Percentage = totalCalls == 0 ? 0 : Math.Round((double)s.Count / totalCalls * 100.0, 1)
            })
            .OrderByDescending(s => s.Count)
            .ToList();

        var callsByDisposition = dispositionReports
            .Select(d => new DistributionItemDto
            {
                Key = d.DispositionCode,
                Label = d.DispositionName,
                Count = d.CallCount,
                Percentage = d.Percentage
            })
            .ToList();

        var callsByAgent = agentReports
            .Take(10)
            .Select(a => new AgentCallCountDto
            {
                AgentId = a.AgentId,
                AgentName = a.AgentName,
                CallCount = a.TotalCalls,
                CompletedCount = a.CompletedCalls,
                TalkTimeSeconds = a.TotalTalkTimeSeconds
            })
            .ToList();

        var callsOverTime = await BuildTimeSeriesPointsAsync(query, resolvedFrom, resolvedTo, cancellationToken);

        var charts = new AnalyticsChartsDto
        {
            CallsOverTime = callsOverTime,
            CallsByAgent = callsByAgent,
            CallsByDirection = callsByDirection,
            CallsByStatus = callsByStatus,
            CallsByDisposition = callsByDisposition
        };

        return new ComprehensiveAnalyticsReportDto
        {
            PeriodPreset = periodName,
            FromUtc = resolvedFrom,
            ToUtc = resolvedTo,
            Volume = volume,
            Agents = agentReports,
            Queues = queueReports,
            Dispositions = dispositionReports,
            Charts = charts,
            GeneratedAt = now
        };
    }

    public async Task<byte[]> ExportCallReportCsvAsync(
        string? preset = null,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var (resolvedFrom, resolvedTo, _) = ResolvePeriod(preset, fromUtc, toUtc);

        var calls = await dbContext.Calls
            .AsNoTracking()
            .Where(c => c.StartedAt >= resolvedFrom && c.StartedAt <= resolvedTo)
            .Include(c => c.Customer)
            .Include(c => c.AssignedAgent)
            .Include(c => c.CallDisposition)
            .OrderByDescending(c => c.StartedAt)
            .Select(c => new
            {
                c.Id,
                CustomerName = c.Customer != null ? c.Customer.DisplayName : "",
                CustomerPhone = c.PhoneNumber,
                AgentName = c.AssignedAgent != null ? c.AssignedAgent.DisplayName : "Unassigned",
                Direction = c.Direction.ToString(),
                Status = c.Status.ToString(),
                StartedAt = c.StartedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                AnsweredAt = c.AnsweredAt.HasValue ? c.AnsweredAt.Value.ToString("yyyy-MM-dd HH:mm:ss") : "",
                EndedAt = c.EndedAt.HasValue ? c.EndedAt.Value.ToString("yyyy-MM-dd HH:mm:ss") : "",
                DurationSeconds = c.AnsweredAt.HasValue && c.EndedAt.HasValue ? (int)(c.EndedAt.Value - c.AnsweredAt.Value).TotalSeconds : 0,
                Disposition = c.CallDisposition != null ? c.CallDisposition.Name : "",
                Notes = c.Notes ?? ""
            })
            .Take(MaximumExportRows + 1)
            .ToListAsync(cancellationToken);

        if (calls.Count > MaximumExportRows)
        {
            throw new ReportExportLimitExceededException(MaximumExportRows);
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("CallId,CustomerName,PhoneNumber,AgentName,Direction,Status,StartedAt,AnsweredAt,EndedAt,DurationSeconds,Disposition,Notes");
        foreach (var r in calls)
        {
            sb.AppendLine($"{EscapeCsv(r.Id.ToString())},{EscapeCsv(r.CustomerName)},{EscapeCsv(r.CustomerPhone)},{EscapeCsv(r.AgentName)},{EscapeCsv(r.Direction)},{EscapeCsv(r.Status)},{EscapeCsv(r.StartedAt)},{EscapeCsv(r.AnsweredAt)},{EscapeCsv(r.EndedAt)},{r.DurationSeconds},{EscapeCsv(r.Disposition)},{EscapeCsv(r.Notes)}");
        }

        return System.Text.Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static (DateTime FromUtc, DateTime ToUtc, string PeriodName) ResolvePeriod(
        string? preset,
        DateTime? fromUtc,
        DateTime? toUtc)
    {
        var now = DateTime.UtcNow;

        if (fromUtc.HasValue && toUtc.HasValue)
        {
            if (fromUtc.Value > toUtc.Value)
            {
                throw new ArgumentException("The report start date must be before the end date.");
            }

            if ((toUtc.Value - fromUtc.Value).TotalDays > MaximumReportRangeDays)
            {
                throw new ArgumentException($"A report date range cannot exceed {MaximumReportRangeDays} days.");
            }

            return (fromUtc.Value, toUtc.Value, "Custom");
        }

        switch (preset?.Trim().ToLowerInvariant())
        {
            case "yesterday":
                var yestStart = now.Date.AddDays(-1);
                var yestEnd = now.Date.AddDays(-1).AddHours(23).AddMinutes(59).AddSeconds(59).AddMilliseconds(999);
                return (yestStart, yestEnd, "Yesterday");

            case "thisweek":
                var diff = ((int)now.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
                var weekStart = now.Date.AddDays(-diff);
                return (weekStart, now, "This Week");

            case "thismonth":
                var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                return (monthStart, now, "This Month");

            case "today":
            default:
                var todayStart = now.Date;
                return (todayStart, now, "Today");
        }
    }

    private static async Task<IReadOnlyList<CallTimeSeriesPointDto>> BuildTimeSeriesPointsAsync(
        IQueryable<Call> query,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        var durationSpan = toUtc - fromUtc;
        var points = new List<CallTimeSeriesPointDto>();

        if (durationSpan.TotalHours <= 48)
        {
            var buckets = await query
                .GroupBy(c => new { c.StartedAt.Year, c.StartedAt.Month, c.StartedAt.Day, c.StartedAt.Hour })
                .Select(g => new
                {
                    g.Key.Year,
                    g.Key.Month,
                    g.Key.Day,
                    g.Key.Hour,
                    Total = g.Count(),
                    Incoming = g.Count(c => c.Direction == CallDirection.Inbound),
                    Outgoing = g.Count(c => c.Direction == CallDirection.Outbound),
                    Completed = g.Count(c => c.Status == CallStatus.Completed),
                    Missed = g.Count(c => c.Status == CallStatus.Abandoned),
                    Rejected = g.Count(c => c.Status == CallStatus.Rejected)
                })
                .ToListAsync(cancellationToken);

            var bucketMap = buckets.ToDictionary(
                x => new DateTime(x.Year, x.Month, x.Day, x.Hour, 0, 0, DateTimeKind.Utc));
            var current = new DateTime(fromUtc.Year, fromUtc.Month, fromUtc.Day, fromUtc.Hour, 0, 0, DateTimeKind.Utc);
            while (current <= toUtc)
            {
                var next = current.AddHours(1);
                bucketMap.TryGetValue(current, out var bucket);

                points.Add(new CallTimeSeriesPointDto
                {
                    Timestamp = current.ToString("o"),
                    PeriodLabel = current.ToString("htt"),
                    Total = bucket?.Total ?? 0,
                    Incoming = bucket?.Incoming ?? 0,
                    Outgoing = bucket?.Outgoing ?? 0,
                    Completed = bucket?.Completed ?? 0,
                    Missed = bucket?.Missed ?? 0,
                    Rejected = bucket?.Rejected ?? 0
                });

                current = next;
            }
        }
        else
        {
            var buckets = await query
                .GroupBy(c => new { c.StartedAt.Year, c.StartedAt.Month, c.StartedAt.Day })
                .Select(g => new
                {
                    g.Key.Year,
                    g.Key.Month,
                    g.Key.Day,
                    Total = g.Count(),
                    Incoming = g.Count(c => c.Direction == CallDirection.Inbound),
                    Outgoing = g.Count(c => c.Direction == CallDirection.Outbound),
                    Completed = g.Count(c => c.Status == CallStatus.Completed),
                    Missed = g.Count(c => c.Status == CallStatus.Abandoned),
                    Rejected = g.Count(c => c.Status == CallStatus.Rejected)
                })
                .ToListAsync(cancellationToken);

            var bucketMap = buckets.ToDictionary(
                x => new DateTime(x.Year, x.Month, x.Day, 0, 0, 0, DateTimeKind.Utc));
            var current = fromUtc.Date;
            while (current <= toUtc.Date)
            {
                var next = current.AddDays(1);
                bucketMap.TryGetValue(current, out var bucket);

                points.Add(new CallTimeSeriesPointDto
                {
                    Timestamp = current.ToString("yyyy-MM-dd"),
                    PeriodLabel = current.ToString("MMM dd"),
                    Total = bucket?.Total ?? 0,
                    Incoming = bucket?.Incoming ?? 0,
                    Outgoing = bucket?.Outgoing ?? 0,
                    Completed = bucket?.Completed ?? 0,
                    Missed = bucket?.Missed ?? 0,
                    Rejected = bucket?.Rejected ?? 0
                });

                current = next;
            }
        }

        return points;
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return $"\"{value}\"";
    }

    private sealed record CallSummaryRecord(
        Guid Id,
        CallDirection Direction,
        CallStatus Status,
        DateTime StartedAt,
        DateTime? AnsweredAt,
        DateTime? EndedAt
    );
}

public sealed class ReportExportLimitExceededException(int maximumRows) : Exception(
    $"This export exceeds the {maximumRows:N0}-row limit. Narrow the date range and try again.")
{
}
