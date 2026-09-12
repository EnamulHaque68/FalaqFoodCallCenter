using CallCenter.Application.Recordings.DTOs;

namespace CallCenter.Application.Recordings;

public interface IRecordingRetentionService
{
    Task<RetentionCleanupResultDto> ProcessExpiredRecordingsAsync(CancellationToken cancellationToken = default);
    Task<RetentionSummaryDto> GetRetentionSummaryAsync(CancellationToken cancellationToken = default);
}
