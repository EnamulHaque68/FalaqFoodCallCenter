using System.Security.Claims;
using CallCenter.Application.Settings;
using CallCenter.Application.Settings.DTOs;
using CallCenter.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CallCenter.Api.Controllers;

[ApiController]
[Route("api/v1/settings")]
[Authorize]
public sealed class SettingsController(ISettingsService settingsService) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = AppPermissions.SettingsView)]
    public async Task<ActionResult<SystemSettingsDto>> GetAll(CancellationToken cancellationToken)
        => Ok(await settingsService.GetAllSettingsAsync(cancellationToken));

    [HttpPut]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<SystemSettingsDto>> UpdateAll(
        [FromBody] SystemSettingsDto settings,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await settingsService.UpdateAllSettingsAsync(settings, GetUserId(), cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPost("reset")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<SystemSettingsDto>> Reset(CancellationToken cancellationToken)
        => Ok(await settingsService.ResetToDefaultsAsync(GetUserId(), cancellationToken));

    private Guid? GetUserId()
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;
}
