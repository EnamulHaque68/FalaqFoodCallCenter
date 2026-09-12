using System.Text.Json;
using CallCenter.Application.Settings.DTOs;

namespace CallCenter.Application.Settings;

public interface ISettingsService
{
    Task<SystemSettingsDto> GetAllSettingsAsync(CancellationToken cancellationToken = default);

    Task<T> GetValueAsync<T>(string key, T defaultValue, CancellationToken cancellationToken = default);

    Task<SystemSettingsDto> UpdateCategorySettingsAsync(
        string category,
        JsonElement payload,
        Guid? adminUserId,
        CancellationToken cancellationToken = default);

    Task<SystemSettingsDto> UpdateAllSettingsAsync(
        SystemSettingsDto dto,
        Guid? adminUserId,
        CancellationToken cancellationToken = default);

    Task<SystemSettingsDto> ResetToDefaultsAsync(
        Guid? adminUserId,
        CancellationToken cancellationToken = default);

    Task<TimeoutProcessResultDto> ProcessTimeoutsAsync(
        CancellationToken cancellationToken = default);
}
