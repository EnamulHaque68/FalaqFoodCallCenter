namespace CallCenter.Application.Recordings;

public interface IRecordingStorage
{
    Task<string> SaveAsync(Stream content, string relativeKey, string contentType, CancellationToken cancellationToken = default);
    Task<Stream?> GetStreamAsync(string storageKey, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string storageKey, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken = default);
    Task<long?> GetFileSizeAsync(string storageKey, CancellationToken cancellationToken = default);
}
