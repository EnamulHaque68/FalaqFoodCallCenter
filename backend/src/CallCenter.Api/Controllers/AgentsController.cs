using System.Security.Claims;
using CallCenter.Application.Agents;
using CallCenter.Application.Agents.DTOs;
using CallCenter.Application.Calls.DTOs;
using CallCenter.Domain.Enums;
using CallCenter.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CallCenter.Api.Controllers;

[ApiController]
[Route("api/v1/agents")]
[Authorize]
public sealed class AgentsController(IAgentService agentService) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = AppPermissions.AgentsView)]
    [ProducesResponseType(typeof(IReadOnlyList<AgentResponseDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AgentResponseDto>>> GetAll(
        [FromQuery] string? search,
        [FromQuery] AgentStatus? status,
        [FromQuery] string? team,
        [FromQuery] bool? isActive,
        CancellationToken cancellationToken)
    {
        return Ok(await agentService.GetAllAsync(search, status, team, isActive, cancellationToken));
    }

    [HttpGet("me/dashboard")]
    [Authorize(Roles = "Agent,Supervisor,Admin")]
    [ProducesResponseType(typeof(AgentDashboardResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AgentDashboardResponseDto>> GetMyDashboard(
        CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userIdClaim, out var userId))
            return Forbid();

        var dashboard = await agentService.GetDashboardByUserIdAsync(userId, cancellationToken);

        return dashboard is null ? NotFound() : Ok(dashboard);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = AppPermissions.AgentsView)]
    [ProducesResponseType(typeof(AgentResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AgentResponseDto>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var agent = await agentService.GetByIdAsync(id, cancellationToken);

        return agent is null ? NotFound() : Ok(agent);
    }

    [HttpGet("{id:guid}/details")]
    [Authorize(Policy = AppPermissions.AgentsView)]
    [ProducesResponseType(typeof(AgentDetailsResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AgentDetailsResponseDto>> GetDetails(
        Guid id,
        CancellationToken cancellationToken)
    {
        var details = await agentService.GetDetailsAsync(id, cancellationToken);

        return details is null ? NotFound() : Ok(details);
    }

    [HttpGet("{id:guid}/calls")]
    [Authorize(Policy = AppPermissions.AgentsView)]
    [ProducesResponseType(typeof(IReadOnlyList<CallResponseDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CallResponseDto>>> GetCalls(
        Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize < 1 || pageSize > 100)
            return BadRequest(new { message = "Page must be >= 1 and pageSize must be between 1 and 100." });

        return Ok(await agentService.GetAgentCallsAsync(id, page, pageSize, cancellationToken));
    }

    [HttpPost]
    [Authorize(Policy = AppPermissions.AgentsManage)]
    [ProducesResponseType(typeof(AgentResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AgentResponseDto>> Create(
        [FromBody] CreateAgentRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var agent = await agentService.CreateAsync(request, GetUserId(), cancellationToken);

            return CreatedAtAction(
                nameof(GetById),
                new { id = agent.Id },
                agent);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (KeyNotFoundException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AppPermissions.AgentsManage)]
    [ProducesResponseType(typeof(AgentResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AgentResponseDto>> Update(
        Guid id,
        [FromBody] UpdateAgentRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var agent = await agentService.UpdateAsync(id, request, GetUserId(), cancellationToken);

            return agent is null ? NotFound() : Ok(agent);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    [HttpPut("{id:guid}/deactivate")]
    [Authorize(Policy = AppPermissions.AgentsManage)]
    [ProducesResponseType(typeof(AgentResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AgentResponseDto>> Deactivate(
        Guid id,
        CancellationToken cancellationToken)
    {
        var agent = await agentService.DeactivateAsync(id, GetUserId(), cancellationToken);

        return agent is null ? NotFound() : Ok(agent);
    }

    [HttpPut("{id:guid}/reactivate")]
    [Authorize(Policy = AppPermissions.AgentsManage)]
    [ProducesResponseType(typeof(AgentResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AgentResponseDto>> Reactivate(
        Guid id,
        CancellationToken cancellationToken)
    {
        var agent = await agentService.ReactivateAsync(id, GetUserId(), cancellationToken);

        return agent is null ? NotFound() : Ok(agent);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AppPermissions.AgentsDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            var deleted = await agentService.DeleteAsync(id, GetUserId(), cancellationToken);

            return deleted ? NoContent() : NotFound();
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    [HttpPut("{id:guid}/status")]
    [Authorize(Policy = AppPermissions.AgentsStatus)]
    [ProducesResponseType(typeof(AgentResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AgentResponseDto>> UpdateStatus(
        Guid id,
        [FromBody] UpdateAgentStatusRequestDto request,
        CancellationToken cancellationToken)
    {
        if (User.IsInRole("Agent"))
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!Guid.TryParse(userIdClaim, out var userId))
            {
                return Forbid();
            }

            var currentAgent = await agentService.GetByIdAsync(id, cancellationToken);

            if (currentAgent is null || currentAgent.UserId != userId)
            {
                return Forbid();
            }
        }

        try
        {
            var agent = await agentService.UpdateStatusAsync(
                id,
                request.Status,
                GetUserId(),
                cancellationToken);

            return agent is null ? NotFound() : Ok(agent);
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

    private Guid? GetUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim, out var guid) ? guid : null;
    }
}
