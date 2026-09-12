using CallCenter.Application.Calls;
using CallCenter.Application.Calls.DTOs;
using CallCenter.Domain.Enums;
using CallCenter.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CallCenter.Api.Controllers;

[ApiController]
[Route("api/v1/calls")]
[Authorize]
public sealed class CallsController(ICallService callService) : ControllerBase
{
    [HttpPost("incoming")]
    [Authorize(Policy = AppPermissions.CallsManage)]
    [ProducesResponseType(typeof(CallResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CallResponseDto>> CreateIncoming(
        [FromBody] CreateIncomingCallRequestDto request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var call = await callService.CreateIncomingAsync(
                request,
                idempotencyKey,
                cancellationToken);

            return CreatedAtAction(nameof(GetById), new { id = call.Id }, call);
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPost("outgoing")]
    [Authorize(Policy = AppPermissions.CallsManage)]
    [ProducesResponseType(typeof(CallResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CallResponseDto>> CreateOutgoing(
        [FromBody] CreateOutgoingCallRequestDto request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var call = await callService.CreateOutgoingAsync(
                request,
                idempotencyKey,
                cancellationToken);

            return CreatedAtAction(nameof(GetById), new { id = call.Id }, call);
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = AppPermissions.CallsView)]
    [ProducesResponseType(typeof(CallResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CallResponseDto>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var call = await callService.GetByIdAsync(id, cancellationToken);
        return call is null ? NotFound() : Ok(call);
    }

    [HttpGet("history")]
    [Authorize(Policy = AppPermissions.CallsView)]
    [ProducesResponseType(typeof(CallPagedResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CallPagedResultDto>> GetHistory(
        [FromQuery] string? search,
        [FromQuery] Guid? customerId,
        [FromQuery] Guid? agentId,
        [FromQuery] CallDirection? direction,
        [FromQuery] CallStatus? status,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] Guid? dispositionId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize < 1 || pageSize > 100)
        {
            return BadRequest(new
            {
                message = "Page must be >= 1 and pageSize must be between 1 and 100."
            });
        }

        return Ok(await callService.GetHistoryAsync(
            search,
            customerId,
            agentId,
            direction,
            status,
            fromUtc,
            toUtc,
            dispositionId,
            page,
            pageSize,
            cancellationToken));
    }

    [HttpPut("{id:guid}/transition")]
    [Authorize(Policy = AppPermissions.CallsManage)]
    [ProducesResponseType(typeof(CallResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CallResponseDto>> Transition(
        Guid id,
        [FromBody] TransitionCallRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var call = await callService.TransitionAsync(
                id,
                request.Status,
                cancellationToken);

            return call is null ? NotFound() : Ok(call);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPost("{id:guid}/complete")]
    [Authorize(Policy = AppPermissions.CallsManage)]
    [ProducesResponseType(typeof(CallResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CallResponseDto>> Complete(
        Guid id,
        [FromBody] CompleteCallRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var call = await callService.CompleteAsync(
                id,
                request.DispositionId,
                request.Notes,
                request.FollowUpAt,
                request.FollowUpNotes,
                cancellationToken);

            return call is null ? NotFound() : Ok(call);
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpGet("{id:guid}/timeline")]
    [Authorize(Policy = AppPermissions.CallsView)]
    [ProducesResponseType(typeof(IReadOnlyList<CallTimelineEventDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CallTimelineEventDto>>> GetTimeline(
        Guid id,
        CancellationToken cancellationToken)
    {
        var timeline = await callService.GetTimelineAsync(id, cancellationToken);
        return Ok(timeline);
    }

    [HttpPut("{id:guid}/notes")]
    [Authorize(Policy = AppPermissions.CallsManage)]
    [ProducesResponseType(typeof(CallResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CallResponseDto>> UpdateNotes(
        Guid id,
        [FromBody] UpdateCallNotesRequestDto request,
        CancellationToken cancellationToken)
    {
        var call = await callService.UpdateNotesAsync(id, request.Notes, cancellationToken);
        return call is null ? NotFound(new { message = "Call not found." }) : Ok(call);
    }
}
