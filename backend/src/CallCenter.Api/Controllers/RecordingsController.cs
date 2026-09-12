using System.Security.Claims;
using CallCenter.Application.Recordings;
using CallCenter.Application.Recordings.DTOs;
using CallCenter.Domain.Security;
using CallCenter.Infrastructure.RealTime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CallCenter.Api.Controllers;

[ApiController]
[Route("api/v1/recordings")]
public sealed class RecordingsController(
    IRecordingService recordingService,
    IRecordingRetentionService retentionService,
    IAgentConnectionResolver agentConnectionResolver) : ControllerBase
{
    private bool IsPrivileged =>
        User.IsInRole("Admin") || User.IsInRole("Supervisor");

    private async Task<Guid?> GetActingAgentIdAsync(CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(userIdClaim, out var userId)
            ? await agentConnectionResolver.ResolveAgentIdAsync(userId, cancellationToken)
            : null;
    }

    private Guid? GetActingUserId()
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userIdClaim, out var userId) ? userId : null;
    }

    [HttpGet]
    [Authorize(Policy = AppPermissions.RecordingsView)]
    [ProducesResponseType(typeof(RecordingPagedResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<RecordingPagedResultDto>> Search(
        [FromQuery] RecordingQueryDto query,
        CancellationToken cancellationToken)
    {
        var actingAgentId = await GetActingAgentIdAsync(cancellationToken);

        try
        {
            var result = await recordingService.SearchAsync(
                query,
                actingAgentId,
                IsPrivileged,
                cancellationToken);

            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = AppPermissions.RecordingsView)]
    [ProducesResponseType(typeof(CallRecordingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CallRecordingDto>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var actingAgentId = await GetActingAgentIdAsync(cancellationToken);

        try
        {
            var recording = await recordingService.GetByIdAsync(
                id,
                actingAgentId,
                IsPrivileged,
                cancellationToken);

            return recording is null ? NotFound(new { message = "Recording not found." }) : Ok(recording);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
    }

    [HttpGet("/api/v1/calls/{callId:guid}/recordings")]
    [Authorize(Policy = AppPermissions.RecordingsView)]
    [ProducesResponseType(typeof(IReadOnlyList<CallRecordingDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<CallRecordingDto>>> GetByCallId(
        Guid callId,
        CancellationToken cancellationToken)
    {
        var actingAgentId = await GetActingAgentIdAsync(cancellationToken);

        try
        {
            var recordings = await recordingService.GetByCallIdAsync(
                callId,
                actingAgentId,
                IsPrivileged,
                cancellationToken);

            return Ok(recordings);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/playback-token")]
    [Authorize(Policy = AppPermissions.RecordingsListen)]
    [ProducesResponseType(typeof(PlaybackTokenResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PlaybackTokenResponseDto>> CreatePlaybackToken(
        Guid id,
        CancellationToken cancellationToken)
    {
        var actingUserId = GetActingUserId() ?? Guid.Empty;
        var actingAgentId = await GetActingAgentIdAsync(cancellationToken);

        try
        {
            var result = await recordingService.CreatePlaybackTokenAsync(
                id,
                actingUserId,
                actingAgentId,
                IsPrivileged,
                cancellationToken);

            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpGet("{id:guid}/stream")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status206PartialContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Stream(
        Guid id,
        [FromQuery] string? token,
        CancellationToken cancellationToken)
    {
        Guid? actingAgentId = null;
        bool isPrivileged = false;

        // 1. If signed playback token is provided, validate it
        if (!string.IsNullOrWhiteSpace(token))
        {
            var (isValid, tokenUserId, errorReason) = recordingService.VerifyPlaybackToken(id, token);
            if (!isValid)
            {
                return Unauthorized(new { message = errorReason ?? "Invalid or expired playback token." });
            }

            if (tokenUserId.HasValue)
            {
                actingAgentId = await agentConnectionResolver.ResolveAgentIdAsync(tokenUserId.Value, cancellationToken);
            }
        }
        // 2. Otherwise require valid Bearer authentication with RecordingsListen permission
        else
        {
            if (User.Identity?.IsAuthenticated != true)
            {
                return Unauthorized(new { message = "Authentication required. Provide Bearer token or signed playback token." });
            }

            var hasListen = User.IsInRole("Admin") ||
                            User.HasClaim(c => c.Type == "permission" && c.Value == AppPermissions.RecordingsListen);
            if (!hasListen)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Permission to listen to call recordings is required." });
            }

            isPrivileged = IsPrivileged;
            actingAgentId = await GetActingAgentIdAsync(cancellationToken);
        }

        try
        {
            var (stream, contentType, length, fileName) = await recordingService.GetStreamAsync(
                id,
                actingAgentId,
                isPrivileged,
                cancellationToken);

            // enableRangeProcessing enables HTTP 206 Partial Content range requests for HTML5 audio seeking/scrubbing
            return File(
                stream,
                contentType,
                fileDownloadName: null,
                enableRangeProcessing: true);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
    }

    [HttpGet("{id:guid}/download")]
    [Authorize(Policy = AppPermissions.RecordingsDownload)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(
        Guid id,
        CancellationToken cancellationToken)
    {
        var actingAgentId = await GetActingAgentIdAsync(cancellationToken);

        try
        {
            var (stream, contentType, length, fileName) = await recordingService.DownloadAsync(
                id,
                actingAgentId,
                IsPrivileged,
                cancellationToken);

            return File(stream, contentType, fileName);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AppPermissions.RecordingsDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        var actingUserId = GetActingUserId() ?? Guid.Empty;

        try
        {
            await recordingService.DeleteAsync(
                id,
                actingUserId,
                IsPrivileged,
                cancellationToken);

            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
    }

    [HttpPost("retention/cleanup")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(RetentionCleanupResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<RetentionCleanupResultDto>> TriggerRetentionCleanup(
        CancellationToken cancellationToken)
    {
        var result = await retentionService.ProcessExpiredRecordingsAsync(cancellationToken);
        return Ok(result);
    }

    [HttpGet("retention/summary")]
    [Authorize(Roles = "Admin,Supervisor")]
    [ProducesResponseType(typeof(RetentionSummaryDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RetentionSummaryDto>> GetRetentionSummary(
        CancellationToken cancellationToken)
    {
        var result = await retentionService.GetRetentionSummaryAsync(cancellationToken);
        return Ok(result);
    }
}
