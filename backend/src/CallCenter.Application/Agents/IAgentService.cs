using CallCenter.Application.Agents.DTOs;
using CallCenter.Domain.Enums;

namespace CallCenter.Application.Agents;

public interface IAgentService
{
    Task<IReadOnlyList<AgentResponseDto>> GetAllAsync(
        string? search = null,
        AgentStatus? status = null,
        string? team = null,
        bool? isActive = null,
        CancellationToken cancellationToken = default);

    Task<AgentResponseDto?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<AgentDetailsResponseDto?> GetDetailsAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<AgentDashboardResponseDto?> GetDashboardByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<AgentResponseDto> CreateAsync(
        CreateAgentRequestDto request,
        Guid? actorUserId = null,
        CancellationToken cancellationToken = default);

    Task<AgentResponseDto?> UpdateAsync(
        Guid id,
        UpdateAgentRequestDto request,
        Guid? actorUserId = null,
        CancellationToken cancellationToken = default);

    Task<AgentResponseDto?> DeactivateAsync(
        Guid id,
        Guid? actorUserId = null,
        CancellationToken cancellationToken = default);

    Task<AgentResponseDto?> ReactivateAsync(
        Guid id,
        Guid? actorUserId = null,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        Guid id,
        Guid? actorUserId = null,
        CancellationToken cancellationToken = default);

    Task<AgentResponseDto?> UpdateStatusAsync(
        Guid id,
        AgentStatus status,
        Guid? actorUserId = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CallCenter.Application.Calls.DTOs.CallResponseDto>> GetAgentCallsAsync(
        Guid id,
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default);
}
