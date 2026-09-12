using System.Security.Claims;
using CallCenter.Application.Users;
using CallCenter.Application.Users.DTOs;
using CallCenter.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CallCenter.Api.Controllers;

[ApiController]
[Route("api/v1/users")]
[Authorize]
public sealed class UsersController(IUserService userService) : ControllerBase
{
    [HttpGet]
    [Authorize(Roles = RolePermissionMatrix.RoleAdmin, Policy = AppPermissions.UsersView)]
    public async Task<ActionResult<IReadOnlyList<UserListItemDto>>> GetAll(
        [FromQuery] string? search,
        [FromQuery] string? role,
        [FromQuery] bool? isActive,
        CancellationToken cancellationToken)
    {
        var users = await userService.GetAllAsync(search, role, isActive, cancellationToken);
        return Ok(users);
    }

    [HttpGet("roles")]
    [Authorize(Roles = RolePermissionMatrix.RoleAdmin, Policy = AppPermissions.UsersView)]
    public async Task<ActionResult<IReadOnlyList<RoleDto>>> GetRoles(CancellationToken cancellationToken)
    {
        var roles = await userService.GetRolesAsync(cancellationToken);
        return Ok(roles);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = RolePermissionMatrix.RoleAdmin, Policy = AppPermissions.UsersView)]
    public async Task<ActionResult<UserListItemDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var user = await userService.GetByIdAsync(id, cancellationToken);
        return user is not null ? Ok(user) : NotFound();
    }

    [HttpPost]
    [Authorize(Roles = RolePermissionMatrix.RoleAdmin, Policy = AppPermissions.UsersManage)]
    public async Task<ActionResult<UserListItemDto>> Create(
        [FromBody] CreateUserRequestDto request,
        CancellationToken cancellationToken)
    {
        var currentUserId = TryGetCurrentUserId();
        if (currentUserId is null)
        {
            return Unauthorized();
        }

        try
        {
            var user = await userService.CreateAsync(request, currentUserId.Value, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = user.Id }, user);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = RolePermissionMatrix.RoleAdmin, Policy = AppPermissions.UsersManage)]
    public async Task<ActionResult<UserListItemDto>> Update(
        Guid id,
        [FromBody] UpdateUserRequestDto request,
        CancellationToken cancellationToken)
    {
        var currentUserId = TryGetCurrentUserId();
        if (currentUserId is null)
        {
            return Unauthorized();
        }

        try
        {
            var user = await userService.UpdateAsync(id, request, currentUserId.Value, cancellationToken);
            return user is not null ? Ok(user) : NotFound();
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    [HttpPut("{id:guid}/deactivate")]
    [Authorize(Roles = RolePermissionMatrix.RoleAdmin, Policy = AppPermissions.UsersManage)]
    public async Task<ActionResult<UserListItemDto>> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        var currentUserId = TryGetCurrentUserId();
        if (currentUserId is null)
        {
            return Unauthorized();
        }

        try
        {
            var user = await userService.DeactivateAsync(id, currentUserId.Value, cancellationToken);
            return user is not null ? Ok(user) : NotFound();
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPut("{id:guid}/reactivate")]
    [Authorize(Roles = RolePermissionMatrix.RoleAdmin, Policy = AppPermissions.UsersManage)]
    public async Task<ActionResult<UserListItemDto>> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        var currentUserId = TryGetCurrentUserId();
        if (currentUserId is null)
        {
            return Unauthorized();
        }

        var user = await userService.ReactivateAsync(id, currentUserId.Value, cancellationToken);
        return user is not null ? Ok(user) : NotFound();
    }

    [HttpPost("{id:guid}/reset-password")]
    [Authorize(Roles = RolePermissionMatrix.RoleAdmin, Policy = AppPermissions.UsersManage)]
    public async Task<ActionResult<UserListItemDto>> ResetPassword(
        Guid id,
        [FromBody] ResetPasswordRequestDto request,
        CancellationToken cancellationToken)
    {
        var currentUserId = TryGetCurrentUserId();
        if (currentUserId is null)
        {
            return Unauthorized();
        }

        var user = await userService.ResetPasswordAsync(id, request, currentUserId.Value, cancellationToken);
        return user is not null ? Ok(user) : NotFound();
    }

    [HttpPost("{id:guid}/change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(
        Guid id,
        [FromBody] ChangePasswordRequestDto request,
        CancellationToken cancellationToken)
    {
        var currentUserId = TryGetCurrentUserId();
        if (currentUserId is null)
        {
            return Unauthorized();
        }

        if (currentUserId.Value != id && !User.IsInRole(RolePermissionMatrix.RoleAdmin))
        {
            return Forbid();
        }

        var succeeded = await userService.ChangePasswordAsync(id, request, currentUserId.Value, cancellationToken);
        if (!succeeded)
        {
            return BadRequest(new { message = "The current password entered is incorrect or user does not exist." });
        }

        return Ok(new { message = "Password changed successfully." });
    }

    private Guid? TryGetCurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var id) ? id : null;
    }
}
