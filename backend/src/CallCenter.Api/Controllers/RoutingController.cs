using CallCenter.Application.Routing;
using CallCenter.Application.Routing.DTOs;
using CallCenter.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CallCenter.Api.Controllers;

[ApiController]
[Route("api/v1/routing")]
[Authorize(Roles = "Admin,Supervisor,Agent")]
public sealed class RoutingController(IRoutingService routingService) : ControllerBase
{
    [HttpPost("calls/{callId:guid}/route")]
    [Authorize(Policy = AppPermissions.RoutingManage)]
    [ProducesResponseType(typeof(RoutingResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RoutingResultDto>> Route(
        Guid callId,
        [FromQuery] RoutingStrategyType? strategy,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await routingService.RouteCallAsync(
                callId,
                strategy,
                cancellationToken));
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    [HttpPost("queue")]
    [Authorize(Policy = AppPermissions.RoutingManage)]
    [ProducesResponseType(typeof(QueueEntryResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<QueueEntryResponseDto>> Enqueue(
        [FromBody] EnqueueCallRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await routingService.EnqueueCallAsync(
                request,
                cancellationToken);

            return CreatedAtAction(
                nameof(Enqueue),
                new { id = result.Id },
                result);
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    [HttpPost("calls/{callId:guid}/assign")]
    [Authorize(Policy = AppPermissions.RoutingManage)]
    [ProducesResponseType(typeof(RoutingResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RoutingResultDto>> Assign(
        Guid callId,
        [FromBody] AssignCallRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await routingService.AssignCallAsync(
                callId,
                request,
                cancellationToken));
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    [HttpPost("calls/{callId:guid}/reassign")]
    [Authorize(Policy = AppPermissions.RoutingManage)]
    [ProducesResponseType(typeof(RoutingResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RoutingResultDto>> Reassign(
        Guid callId,
        [FromBody] ReassignCallRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await routingService.ReassignCallAsync(
                callId,
                request,
                cancellationToken));
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }
}
