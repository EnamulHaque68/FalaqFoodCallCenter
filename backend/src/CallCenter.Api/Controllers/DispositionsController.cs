using CallCenter.Application.Dispositions;
using CallCenter.Application.Dispositions.DTOs;
using CallCenter.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CallCenter.Api.Controllers;

[ApiController]
[Route("api/v1/dispositions")]
[Authorize]
public sealed class DispositionsController(IDispositionService dispositionService) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = AppPermissions.DispositionsView)]
    [ProducesResponseType(typeof(IReadOnlyList<CallDispositionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CallDispositionDto>>> GetAll(
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var dispositions = await dispositionService.GetAllAsync(includeInactive, cancellationToken);
        return Ok(dispositions);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = AppPermissions.DispositionsView)]
    [ProducesResponseType(typeof(CallDispositionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CallDispositionDto>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var disposition = await dispositionService.GetByIdAsync(id, cancellationToken);
        return disposition is null ? NotFound(new { message = "Disposition not found." }) : Ok(disposition);
    }

    [HttpPost]
    [Authorize(Policy = AppPermissions.DispositionsManage)]
    [ProducesResponseType(typeof(CallDispositionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CallDispositionDto>> Create(
        [FromBody] CreateDispositionRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var created = await dispositionService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AppPermissions.DispositionsManage)]
    [ProducesResponseType(typeof(CallDispositionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CallDispositionDto>> Update(
        Guid id,
        [FromBody] UpdateDispositionRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await dispositionService.UpdateAsync(id, request, cancellationToken);
            return updated is null ? NotFound(new { message = "Disposition not found." }) : Ok(updated);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPatch("{id:guid}/toggle-status")]
    [Authorize(Policy = AppPermissions.DispositionsManage)]
    [ProducesResponseType(typeof(CallDispositionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CallDispositionDto>> ToggleStatus(
        Guid id,
        CancellationToken cancellationToken)
    {
        var updated = await dispositionService.ToggleStatusAsync(id, cancellationToken);
        return updated is null ? NotFound(new { message = "Disposition not found." }) : Ok(updated);
    }
}
