using CallCenter.Application.Routing.DTOs;

namespace CallCenter.Application.Routing;

public interface IRoutingService
{
    Task<RoutingResultDto> RouteCallAsync(
        Guid callId,
        RoutingStrategyType? strategyType = null,
        CancellationToken cancellationToken = default);

    Task<QueueEntryResponseDto> EnqueueCallAsync(
        EnqueueCallRequestDto request,
        CancellationToken cancellationToken = default);

    Task<RoutingResultDto> AssignCallAsync(
        Guid callId,
        AssignCallRequestDto request,
        CancellationToken cancellationToken = default);

    Task<RoutingResultDto> ReassignCallAsync(
        Guid callId,
        ReassignCallRequestDto request,
        CancellationToken cancellationToken = default);
}
