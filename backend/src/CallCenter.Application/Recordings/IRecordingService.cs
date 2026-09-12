using CallCenter.Application.Recordings.DTOs;

namespace CallCenter.Application.Recordings;

public interface IRecordingService
{
    Task<CallRecordingDto?> GetByIdAsync(
        Guid id,
        Guid? actingAgentId,
        bool isPrivileged,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CallRecordingDto>> GetByCallIdAsync(
        Guid callId,
        Guid? actingAgentId,
        bool isPrivileged,
        CancellationToken cancellationToken = default);

    Task<RecordingPagedResultDto> SearchAsync(
        RecordingQueryDto query,
        Guid? actingAgentId,
        bool isPrivileged,
        CancellationToken cancellationToken = default);

    Task<PlaybackTokenResponseDto> CreatePlaybackTokenAsync(
        Guid id,
        Guid actingUserId,
        Guid? actingAgentId,
        bool isPrivileged,
        CancellationToken cancellationToken = default);

    (bool IsValid, Guid? UserId, string? ErrorReason) VerifyPlaybackToken(
        Guid id,
        string token);

    Task<(Stream AudioStream, string ContentType, long FileLength, string FileName)> GetStreamAsync(
        Guid id,
        Guid? actingAgentId,
        bool isPrivileged,
        CancellationToken cancellationToken = default);

    Task<(Stream AudioStream, string ContentType, long FileLength, string FileName)> DownloadAsync(
        Guid id,
        Guid? actingAgentId,
        bool isPrivileged,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        Guid id,
        Guid actingUserId,
        bool isPrivileged,
        CancellationToken cancellationToken = default);

    Task<CallRecordingDto> SaveRecordingAsync(
        Guid callId,
        string? providerRecordingId,
        Stream content,
        string contentType,
        TimeSpan? duration,
        CancellationToken cancellationToken = default);
}
