using CallCenter.Application.Audit;
using CallCenter.Application.Audit.DTOs;
using CallCenter.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CallCenter.Api.Controllers;

[ApiController]
[Route("api/v1/audit-logs")]
[Authorize]
public sealed class AuditLogsController(IAuditLogService auditLogService) : ControllerBase
{
    [HttpGet]
    [Authorize(Roles = RolePermissionMatrix.RoleAdmin, Policy = AppPermissions.AuditLogsView)]
    [ProducesResponseType(typeof(AuditLogPagedResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuditLogPagedResultDto>> GetPaged(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] string? action = null,
        [FromQuery] string? entityName = null,
        [FromQuery] Guid? userId = null,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize < 1 || pageSize > 100)
        {
            return BadRequest(new { message = "Page must be >= 1 and pageSize must be between 1 and 100." });
        }

        var filter = new AuditLogFilterDto
        {
            Page = page,
            PageSize = pageSize,
            Search = search,
            Action = action,
            EntityName = entityName,
            UserId = userId,
            FromUtc = fromUtc,
            ToUtc = toUtc
        };

        var result = await auditLogService.GetPagedAsync(filter, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = RolePermissionMatrix.RoleAdmin, Policy = AppPermissions.AuditLogsView)]
    [ProducesResponseType(typeof(AuditLogDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuditLogDto>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var log = await auditLogService.GetByIdAsync(id, cancellationToken);
        return log is not null ? Ok(log) : NotFound();
    }

    [HttpGet("actions")]
    [Authorize(Roles = RolePermissionMatrix.RoleAdmin, Policy = AppPermissions.AuditLogsView)]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<string>>> GetDistinctActions(
        CancellationToken cancellationToken)
    {
        var actions = await auditLogService.GetActionsAsync(cancellationToken);
        return Ok(actions);
    }

    [HttpGet("entities")]
    [Authorize(Roles = RolePermissionMatrix.RoleAdmin, Policy = AppPermissions.AuditLogsView)]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<string>>> GetDistinctEntities(
        CancellationToken cancellationToken)
    {
        var entities = await auditLogService.GetEntitiesAsync(cancellationToken);
        return Ok(entities);
    }
}
