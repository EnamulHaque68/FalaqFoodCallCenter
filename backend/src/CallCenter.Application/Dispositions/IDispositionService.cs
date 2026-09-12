using CallCenter.Application.Dispositions.DTOs;

namespace CallCenter.Application.Dispositions;

public interface IDispositionService
{
    Task<IReadOnlyList<CallDispositionDto>> GetAllAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    Task<CallDispositionDto?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<CallDispositionDto> CreateAsync(
        CreateDispositionRequestDto request,
        CancellationToken cancellationToken = default);

    Task<CallDispositionDto> UpdateAsync(
        Guid id,
        UpdateDispositionRequestDto request,
        CancellationToken cancellationToken = default);

    Task<CallDispositionDto> ToggleStatusAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
