using CallCenter.Application.Queues;
using CallCenter.Application.Queues.DTOs;
using CallCenter.Application.Routing.DTOs;
using CallCenter.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CallCenter.Api.Controllers;

[ApiController]
[Route("api/v1/queues")]
[Authorize(Roles = "Admin,Supervisor,Agent")]
public sealed class QueuesController(IQueueService queueService) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = AppPermissions.RoutingView)]
    [ProducesResponseType(typeof(IReadOnlyList<CallQueueDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CallQueueDto>>> GetQueues(CancellationToken cancellationToken)
    {
        var queues = await queueService.GetQueuesAsync(cancellationToken);
        return Ok(queues);
    }

    [HttpGet("summary")]
    [Authorize(Policy = AppPermissions.RoutingView)]
    [ProducesResponseType(typeof(QueueSummaryDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<QueueSummaryDto>> GetQueueSummary(CancellationToken cancellationToken)
    {
        var summary = await queueService.GetQueueSummaryAsync(cancellationToken);
        return Ok(summary);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = AppPermissions.RoutingView)]
    [ProducesResponseType(typeof(CallQueueDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CallQueueDto>> GetQueueById(Guid id, CancellationToken cancellationToken)
    {
        var queue = await queueService.GetQueueByIdAsync(id, cancellationToken);
        if (queue is null)
        {
            return NotFound(new { message = $"Queue with ID '{id}' was not found." });
        }
        return Ok(queue);
    }

    [HttpGet("{id:guid}/entries")]
    [Authorize(Policy = AppPermissions.RoutingView)]
    [ProducesResponseType(typeof(IReadOnlyList<CallQueueEntryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CallQueueEntryDto>>> GetQueueEntries(Guid id, CancellationToken cancellationToken)
    {
        var entries = await queueService.GetQueueEntriesAsync(id, cancellationToken);
        return Ok(entries);
    }

    [HttpPost]
    [Authorize(Policy = AppPermissions.RoutingManage)]
    [ProducesResponseType(typeof(CallQueueDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CallQueueDto>> CreateQueue(
        [FromBody] CreateQueueRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var created = await queueService.CreateQueueAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetQueueById), new { id = created.Id }, created);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AppPermissions.RoutingManage)]
    [ProducesResponseType(typeof(CallQueueDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CallQueueDto>> UpdateQueue(
        Guid id,
        [FromBody] UpdateQueueRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await queueService.UpdateQueueAsync(id, request, cancellationToken);
            return Ok(updated);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AppPermissions.RoutingManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteQueue(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var success = await queueService.DeleteQueueAsync(id, cancellationToken);
            if (!success)
            {
                return NotFound(new { message = $"Queue with ID '{id}' was not found." });
            }
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPost("entries/{entryId:guid}/priority")]
    [Authorize(Policy = AppPermissions.RoutingManage)]
    [ProducesResponseType(typeof(CallQueueEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CallQueueEntryDto>> PrioritizeEntry(
        Guid entryId,
        [FromBody] SetEntryPriorityRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await queueService.PrioritizeEntryAsync(entryId, request.Priority, cancellationToken);
            return Ok(updated);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost("calls/{callId:guid}/cancel")]
    [Authorize(Policy = AppPermissions.RoutingManage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelQueueEntry(
        Guid callId,
        [FromQuery] string? reason,
        CancellationToken cancellationToken)
    {
        var success = await queueService.CancelQueueEntryAsync(callId, reason, cancellationToken);
        if (!success)
        {
            return NotFound(new { message = $"Active queue entry for call '{callId}' was not found." });
        }
        return Ok(new { message = "Queue entry cancelled successfully." });
    }

    [HttpPost("calls/{callId:guid}/complete")]
    [Authorize(Policy = AppPermissions.RoutingManage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CompleteQueueEntry(
        Guid callId,
        CancellationToken cancellationToken)
    {
        var success = await queueService.CompleteQueueEntryAsync(callId, cancellationToken);
        if (!success)
        {
            return NotFound(new { message = $"Active queue entry for call '{callId}' was not found." });
        }
        return Ok(new { message = "Queue entry completed successfully." });
    }

    [HttpPost("auto-assign")]
    [Authorize(Policy = AppPermissions.RoutingManage)]
    [ProducesResponseType(typeof(RoutingResultDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RoutingResultDto?>> AutoAssign(
        [FromQuery] Guid? agentId,
        CancellationToken cancellationToken)
    {
        var result = await queueService.TryAutoAssignNextCallAsync(agentId, cancellationToken);
        if (result is null)
        {
            return Ok(new { message = "No eligible queued call or available agent for automatic assignment." });
        }
        return Ok(result);
    }
}
