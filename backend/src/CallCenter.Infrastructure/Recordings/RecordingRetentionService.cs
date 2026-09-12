using CallCenter.Application.Audit;
using CallCenter.Application.Recordings;
using CallCenter.Application.Recordings.DTOs;
using CallCenter.Domain.Enums;
using CallCenter.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CallCenter.Infrastructure.Recordings;

public sealed class RecordingRetentionService : IRecordingRetentionService
{
    private readonly CallCenterDbContext _dbContext;
    private readonly IRecordingStorage _storage;
    private readonly IAuditLogService? _auditLogService;
    private readonly ILogger<RecordingRetentionService> _logger;
    private readonly RecordingStorageOptions _options;

    public RecordingRetentionService(
        CallCenterDbContext dbContext,
        IRecordingStorage storage,
        IOptions<RecordingStorageOptions> options,
        ILogger<RecordingRetentionService> logger,
        IAuditLogService? auditLogService = null)
    {
        _dbContext = dbContext;
        _storage = storage;
        _logger = logger;
        _auditLogService = auditLogService;
        _options = options.Value;
    }

    public async Task<RetentionCleanupResultDto> ProcessExpiredRecordingsAsync(
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var expiredRecordings = await _dbContext.CallRecordings
            .Where(r => !r.IsDeleted && r.RetentionUntil.HasValue && r.RetentionUntil.Value <= now)
            .ToListAsync(cancellationToken);

        if (expiredRecordings.Count == 0)
        {
            return new RetentionCleanupResultDto
            {
                PurgedCount = 0,
                PurgedRecordingIds = [],
                ExecutedAt = now,
                Message = "No expired recordings found for cleanup."
            };
        }

        var purgedIds = new List<Guid>();

        foreach (var recording in expiredRecordings)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(recording.StorageKey))
                {
                    await _storage.DeleteAsync(recording.StorageKey, cancellationToken);
                }

                recording.IsDeleted = true;
                recording.Status = RecordingStatus.Deleted;
                recording.DeletedAt = now;
                purgedIds.Add(recording.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to purge audio file for recording {RecordingId}", recording.Id);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (_auditLogService is not null)
        {
            await _auditLogService.LogAsync(
                null,
                "RecordingRetentionPurged",
                "CallRecording",
                $"{purgedIds.Count} recordings",
                new { PurgedCount = purgedIds.Count, PurgedIds = purgedIds, ExecutedAt = now },
                cancellationToken);
        }

        _logger.LogInformation("Retention cleanup completed. Purged {Count} expired call recordings.", purgedIds.Count);

        return new RetentionCleanupResultDto
        {
            PurgedCount = purgedIds.Count,
            PurgedRecordingIds = purgedIds,
            ExecutedAt = now,
            Message = $"Successfully purged {purgedIds.Count} expired recordings."
        };
    }

    public async Task<RetentionSummaryDto> GetRetentionSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var total = await _dbContext.CallRecordings.CountAsync(cancellationToken);
        var active = await _dbContext.CallRecordings.CountAsync(r => !r.IsDeleted, cancellationToken);
        var expiredPending = await _dbContext.CallRecordings
            .CountAsync(r => !r.IsDeleted && r.RetentionUntil.HasValue && r.RetentionUntil.Value <= now, cancellationToken);
        var purged = await _dbContext.CallRecordings.CountAsync(r => r.IsDeleted, cancellationToken);

        var oldestActive = await _dbContext.CallRecordings
            .Where(r => !r.IsDeleted)
            .OrderBy(r => r.CreatedAt)
            .Select(r => (DateTime?)r.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return new RetentionSummaryDto
        {
            TotalRecordings = total,
            ActiveRecordings = active,
            ExpiredPendingPurge = expiredPending,
            PurgedRecordings = purged,
            OldestActiveCreatedAt = oldestActive,
            DefaultRetentionDays = _options.DefaultRetentionDays
        };
    }
}
