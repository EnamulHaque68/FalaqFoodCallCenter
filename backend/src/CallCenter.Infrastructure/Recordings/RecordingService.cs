using System.Security.Cryptography;
using CallCenter.Application.Audit;
using CallCenter.Application.Recordings;
using CallCenter.Application.Recordings.DTOs;
using CallCenter.Domain.Entities;
using CallCenter.Domain.Enums;
using CallCenter.Infrastructure.Persistence;
using CallCenter.Infrastructure.Recordings.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CallCenter.Infrastructure.Recordings;

public sealed class RecordingService : IRecordingService
{
    private readonly CallCenterDbContext _dbContext;
    private readonly IRecordingStorage _storage;
    private readonly RecordingPlaybackTokenGenerator _tokenGenerator;
    private readonly IAuditLogService? _auditLogService;
    private readonly RecordingStorageOptions _options;

    public RecordingService(
        CallCenterDbContext dbContext,
        IRecordingStorage storage,
        RecordingPlaybackTokenGenerator tokenGenerator,
        IOptions<RecordingStorageOptions> options,
        IAuditLogService? auditLogService = null)
    {
        _dbContext = dbContext;
        _storage = storage;
        _tokenGenerator = tokenGenerator;
        _auditLogService = auditLogService;
        _options = options.Value;
    }

    public async Task<CallRecordingDto?> GetByIdAsync(
        Guid id,
        Guid? actingAgentId,
        bool isPrivileged,
        CancellationToken cancellationToken = default)
    {
        var recording = await _dbContext.CallRecordings
            .Include(r => r.Call)
                .ThenInclude(c => c.AssignedAgent)
            .SingleOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (recording is null) return null;

        EnsureRecordingAccess(recording, actingAgentId, isPrivileged);
        return MapToDto(recording);
    }

    public async Task<IReadOnlyList<CallRecordingDto>> GetByCallIdAsync(
        Guid callId,
        Guid? actingAgentId,
        bool isPrivileged,
        CancellationToken cancellationToken = default)
    {
        var recordings = await _dbContext.CallRecordings
            .Include(r => r.Call)
                .ThenInclude(c => c.AssignedAgent)
            .Where(r => r.CallId == callId && !r.IsDeleted)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

        if (recordings.Count == 0) return [];

        // Check access on the parent call
        var first = recordings[0];
        EnsureRecordingAccess(first, actingAgentId, isPrivileged);

        return recordings.Select(MapToDto).ToList();
    }

    public async Task<RecordingPagedResultDto> SearchAsync(
        RecordingQueryDto query,
        Guid? actingAgentId,
        bool isPrivileged,
        CancellationToken cancellationToken = default)
    {
        var dbQuery = _dbContext.CallRecordings
            .Include(r => r.Call)
                .ThenInclude(c => c.AssignedAgent)
            .Where(r => !r.IsDeleted)
            .AsQueryable();

        // Agent Isolation: Agents can only search their own assigned calls
        if (!isPrivileged)
        {
            if (!actingAgentId.HasValue)
            {
                throw new UnauthorizedAccessException("Agent identifier is required to view call recordings.");
            }

            dbQuery = dbQuery.Where(r => r.Call.AssignedAgentId == actingAgentId.Value);
        }
        else if (query.AgentId.HasValue)
        {
            dbQuery = dbQuery.Where(r => r.Call.AssignedAgentId == query.AgentId.Value);
        }

        if (query.CallId.HasValue)
        {
            dbQuery = dbQuery.Where(r => r.CallId == query.CallId.Value);
        }

        if (query.CustomerId.HasValue)
        {
            dbQuery = dbQuery.Where(r => r.Call.CustomerId == query.CustomerId.Value);
        }

        if (query.Status.HasValue)
        {
            dbQuery = dbQuery.Where(r => r.Status == query.Status.Value);
        }

        if (query.FromUtc.HasValue)
        {
            dbQuery = dbQuery.Where(r => r.CreatedAt >= query.FromUtc.Value);
        }

        if (query.ToUtc.HasValue)
        {
            dbQuery = dbQuery.Where(r => r.CreatedAt <= query.ToUtc.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim();
            dbQuery = dbQuery.Where(r =>
                r.Call.PhoneNumber.Contains(s) ||
                (r.ProviderRecordingId != null && r.ProviderRecordingId.Contains(s)));
        }

        var totalCount = await dbQuery.CountAsync(cancellationToken);

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < 1 ? 20 : (query.PageSize > 100 ? 100 : query.PageSize);

        var items = await dbQuery
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new RecordingPagedResultDto
        {
            Items = items.Select(MapToDto).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<PlaybackTokenResponseDto> CreatePlaybackTokenAsync(
        Guid id,
        Guid actingUserId,
        Guid? actingAgentId,
        bool isPrivileged,
        CancellationToken cancellationToken = default)
    {
        var recording = await _dbContext.CallRecordings
            .Include(r => r.Call)
            .SingleOrDefaultAsync(r => r.Id == id && !r.IsDeleted, cancellationToken)
            ?? throw new KeyNotFoundException("Recording not found.");

        EnsureRecordingAccess(recording, actingAgentId, isPrivileged);

        if (recording.Status == RecordingStatus.Deleted)
        {
            throw new InvalidOperationException("This recording has been deleted.");
        }

        var (token, expiresAtUtc) = _tokenGenerator.GenerateToken(id, actingUserId);

        var streamUrl = $"/api/v1/recordings/{id}/stream?token={Uri.EscapeDataString(token)}";

        return new PlaybackTokenResponseDto
        {
            RecordingId = id,
            Token = token,
            ExpiresAtUtc = expiresAtUtc,
            StreamUrl = streamUrl
        };
    }

    public (bool IsValid, Guid? UserId, string? ErrorReason) VerifyPlaybackToken(
        Guid id,
        string token)
    {
        return _tokenGenerator.ValidateToken(id, token);
    }

    public async Task<(Stream AudioStream, string ContentType, long FileLength, string FileName)> GetStreamAsync(
        Guid id,
        Guid? actingAgentId,
        bool isPrivileged,
        CancellationToken cancellationToken = default)
    {
        var recording = await _dbContext.CallRecordings
            .Include(r => r.Call)
            .SingleOrDefaultAsync(r => r.Id == id && !r.IsDeleted, cancellationToken)
            ?? throw new KeyNotFoundException("Recording not found.");

        EnsureRecordingAccess(recording, actingAgentId, isPrivileged);

        if (recording.Status == RecordingStatus.Deleted)
        {
            throw new InvalidOperationException("This recording has been deleted.");
        }

        var stream = await _storage.GetStreamAsync(recording.StorageKey, cancellationToken)
            ?? throw new FileNotFoundException("Recording audio file not found on storage.");

        var length = recording.FileSizeBytes ?? stream.Length;
        var contentType = !string.IsNullOrWhiteSpace(recording.ContentType)
            ? recording.ContentType
            : "audio/wav";

        var ext = contentType.Contains("mpeg") || contentType.Contains("mp3") ? ".mp3" : ".wav";
        var fileName = $"recording_{recording.Id:N}{ext}";

        if (_auditLogService is not null)
        {
            await _auditLogService.LogAsync(
                actingAgentId,
                "RecordingStreamed",
                "CallRecording",
                recording.Id.ToString(),
                new { recording.CallId, Duration = recording.Duration?.TotalSeconds, ContentType = contentType },
                cancellationToken);
        }

        return (stream, contentType, length, fileName);
    }

    public async Task<(Stream AudioStream, string ContentType, long FileLength, string FileName)> DownloadAsync(
        Guid id,
        Guid? actingAgentId,
        bool isPrivileged,
        CancellationToken cancellationToken = default)
    {
        var recording = await _dbContext.CallRecordings
            .Include(r => r.Call)
            .SingleOrDefaultAsync(r => r.Id == id && !r.IsDeleted, cancellationToken)
            ?? throw new KeyNotFoundException("Recording not found.");

        EnsureRecordingAccess(recording, actingAgentId, isPrivileged);

        if (recording.Status == RecordingStatus.Deleted)
        {
            throw new InvalidOperationException("This recording has been deleted.");
        }

        var stream = await _storage.GetStreamAsync(recording.StorageKey, cancellationToken)
            ?? throw new FileNotFoundException("Recording audio file not found on storage.");

        var length = recording.FileSizeBytes ?? stream.Length;
        var contentType = !string.IsNullOrWhiteSpace(recording.ContentType)
            ? recording.ContentType
            : "audio/wav";

        var ext = contentType.Contains("mpeg") || contentType.Contains("mp3") ? ".mp3" : ".wav";
        var fileName = $"call_{recording.CallId:N}_{recording.Id:N}{ext}";

        if (_auditLogService is not null)
        {
            await _auditLogService.LogAsync(
                actingAgentId,
                "RecordingDownloaded",
                "CallRecording",
                recording.Id.ToString(),
                new { recording.CallId, Duration = recording.Duration?.TotalSeconds, FileSizeBytes = length },
                cancellationToken);
        }

        return (stream, contentType, length, fileName);
    }

    public async Task<bool> DeleteAsync(
        Guid id,
        Guid actingUserId,
        bool isPrivileged,
        CancellationToken cancellationToken = default)
    {
        if (!isPrivileged)
        {
            throw new UnauthorizedAccessException("Only administrators can delete call recordings.");
        }

        var recording = await _dbContext.CallRecordings
            .SingleOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Recording not found.");

        if (recording.IsDeleted) return true;

        var now = DateTime.UtcNow;
        recording.IsDeleted = true;
        recording.Status = RecordingStatus.Deleted;
        recording.DeletedAt = now;
        recording.DeletedByUserId = actingUserId;

        // Delete physical file
        if (!string.IsNullOrWhiteSpace(recording.StorageKey))
        {
            await _storage.DeleteAsync(recording.StorageKey, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (_auditLogService is not null)
        {
            await _auditLogService.LogAsync(
                null,
                "RecordingDeleted",
                "CallRecording",
                recording.Id.ToString(),
                new { recording.CallId, DeletedByUserId = actingUserId, DeletedAt = now },
                cancellationToken);
        }

        return true;
    }

    public async Task<CallRecordingDto> SaveRecordingAsync(
        Guid callId,
        string? providerRecordingId,
        Stream content,
        string contentType,
        TimeSpan? duration,
        CancellationToken cancellationToken = default)
    {
        var call = await _dbContext.Calls
            .SingleOrDefaultAsync(c => c.Id == callId, cancellationToken)
            ?? throw new KeyNotFoundException($"Call '{callId}' not found.");

        var recId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var ext = contentType.Contains("mpeg") || contentType.Contains("mp3") ? ".mp3" : ".wav";
        var relativeKey = $"{now:yyyy/MM}/{callId:N}/{recId:N}{ext}";

        var storageKey = await _storage.SaveAsync(content, relativeKey, contentType, cancellationToken);
        var fileSize = await _storage.GetFileSizeAsync(storageKey, cancellationToken);

        // Compute checksum
        string? checksum = null;
        if (content.CanSeek)
        {
            content.Position = 0;
            using var sha = SHA256.Create();
            var hashBytes = await sha.ComputeHashAsync(content, cancellationToken);
            checksum = Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        // Calculate retention
        DateTime? retentionUntil = null;
        if (_options.DefaultRetentionDays > 0)
        {
            retentionUntil = now.AddDays(_options.DefaultRetentionDays);
        }

        var recording = new CallRecording
        {
            Id = recId,
            CallId = call.Id,
            ProviderRecordingId = providerRecordingId,
            StorageKey = storageKey,
            StorageUrl = $"/api/v1/recordings/{recId}/stream",
            StorageProvider = "LocalStorage",
            ContentType = contentType,
            Duration = duration,
            FileSizeBytes = fileSize,
            Status = RecordingStatus.Available,
            ChecksumSha256 = checksum,
            CreatedAt = now,
            CompletedAt = now,
            RetentionUntil = retentionUntil,
            IsDeleted = false
        };

        _dbContext.CallRecordings.Add(recording);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapToDto(recording);
    }

    private static void EnsureRecordingAccess(
        CallRecording recording,
        Guid? actingAgentId,
        bool isPrivileged)
    {
        if (isPrivileged) return;

        if (!actingAgentId.HasValue)
        {
            throw new UnauthorizedAccessException("Agent identifier is required to view call recordings.");
        }

        if (recording.Call.AssignedAgentId != actingAgentId.Value)
        {
            throw new UnauthorizedAccessException("You are not authorized to view recordings of other agents' calls.");
        }
    }

    private static CallRecordingDto MapToDto(CallRecording r) => new()
    {
        Id = r.Id,
        CallId = r.CallId,
        CustomerPhone = r.Call?.PhoneNumber,
        AssignedAgentId = r.Call?.AssignedAgentId,
        AgentName = r.Call?.AssignedAgent?.DisplayName,
        ProviderRecordingId = r.ProviderRecordingId,
        DurationSeconds = r.Duration.HasValue ? (int)r.Duration.Value.TotalSeconds : null,
        FileSizeBytes = r.FileSizeBytes,
        ContentType = r.ContentType,
        Status = r.Status,
        CreatedAt = r.CreatedAt,
        CompletedAt = r.CompletedAt,
        RetentionUntil = r.RetentionUntil,
        IsDeleted = r.IsDeleted
    };
}
