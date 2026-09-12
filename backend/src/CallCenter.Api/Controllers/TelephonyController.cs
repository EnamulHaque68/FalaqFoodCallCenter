using CallCenter.Application.Calls.DTOs;
using CallCenter.Application.Telephony;
using CallCenter.Application.Telephony.DTOs;
using CallCenter.Application.Telephony.Providers;
using CallCenter.Domain.Enums;
using CallCenter.Infrastructure.RealTime;
using CallCenter.Infrastructure.Telephony;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace CallCenter.Api.Controllers;

[ApiController]
[Route("api/v1/telephony")]
[Authorize(Roles = "Admin,Supervisor,Agent")]
public sealed class TelephonyController(
    ITelephonyService telephonyService,
    IAgentConnectionResolver agentConnectionResolver,
    IOptions<TelephonyOptions> telephonyOptions) : ControllerBase
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

    [HttpPost("incoming/simulate")]
    [ProducesResponseType(typeof(TelephonyCallResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TelephonyCallResponseDto>> SimulateIncoming(
        [FromBody] SimulateIncomingCallRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await telephonyService.SimulateIncomingCallAsync(
                request,
                cancellationToken);

            return CreatedAtAction(
                nameof(SimulateIncoming),
                new { id = result.CallId },
                result);
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

    [HttpPost("outgoing")]
    [ProducesResponseType(typeof(TelephonyCallResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TelephonyCallResponseDto>> InitiateOutbound(
        [FromBody] InitiateOutboundCallRequestDto request,
        CancellationToken cancellationToken)
    {
        var actingAgentId = await GetActingAgentIdAsync(cancellationToken);
        var isPrivileged = IsPrivileged;

        if (actingAgentId is null && !isPrivileged)
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            if (Request.Headers.TryGetValue("Idempotency-Key", out var ik) ||
                Request.Headers.TryGetValue("X-Idempotency-Key", out ik))
            {
                request.IdempotencyKey = ik.ToString();
            }
        }

        try
        {
            var result = await telephonyService.InitiateOutboundCallAsync(
                request,
                actingAgentId ?? Guid.Empty,
                isPrivileged,
                cancellationToken);


            return CreatedAtAction(
                nameof(InitiateOutbound),
                new { id = result.CallId },
                result);
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
        catch (UnauthorizedAccessException exception)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = exception.Message });
        }
    }

    [HttpPost("calls/{callId:guid}/accept")]
    [ProducesResponseType(typeof(TelephonyCallResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TelephonyCallResponseDto>> Accept(
        Guid callId,
        CancellationToken cancellationToken)
    {
        var actingAgentId = await GetActingAgentIdAsync(cancellationToken);

        try
        {
            return Ok(await telephonyService.AcceptCallAsync(
                callId,
                actingAgentId,
                IsPrivileged,
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
        catch (UnauthorizedAccessException exception)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = exception.Message });
        }
    }

    [HttpPost("calls/{callId:guid}/reject")]
    [ProducesResponseType(typeof(TelephonyCallResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TelephonyCallResponseDto>> Reject(
        Guid callId,
        CancellationToken cancellationToken)
    {
        var actingAgentId = await GetActingAgentIdAsync(cancellationToken);

        try
        {
            return Ok(await telephonyService.RejectCallAsync(
                callId,
                actingAgentId,
                IsPrivileged,
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
        catch (UnauthorizedAccessException exception)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = exception.Message });
        }
    }

    [HttpPost("calls/{callId:guid}/end")]
    [ProducesResponseType(typeof(TelephonyCallResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TelephonyCallResponseDto>> End(
        Guid callId,
        CancellationToken cancellationToken)
    {
        var actingAgentId = await GetActingAgentIdAsync(cancellationToken);

        try
        {
            return Ok(await telephonyService.EndCallAsync(
                callId,
                actingAgentId,
                IsPrivileged,
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
        catch (UnauthorizedAccessException exception)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = exception.Message });
        }
    }

    [HttpPost("calls/{callId:guid}/complete")]
    [ProducesResponseType(typeof(TelephonyCallResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TelephonyCallResponseDto>> Complete(
        Guid callId,
        [FromBody] CompleteCallRequestDto request,
        CancellationToken cancellationToken)
    {
        var actingAgentId = await GetActingAgentIdAsync(cancellationToken);

        try
        {
            return Ok(await telephonyService.CompleteCallAsync(
                callId,
                request,
                actingAgentId,
                IsPrivileged,
                cancellationToken));
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
        catch (UnauthorizedAccessException exception)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = exception.Message });
        }
    }

    [HttpPost("calls/{callId:guid}/hold")]
    [ProducesResponseType(typeof(TelephonyCallResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TelephonyCallResponseDto>> Hold(
        Guid callId,
        CancellationToken cancellationToken)
    {
        var actingAgentId = await GetActingAgentIdAsync(cancellationToken);

        try
        {
            return Ok(await telephonyService.HoldCallAsync(
                callId,
                actingAgentId,
                IsPrivileged,
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
        catch (UnauthorizedAccessException exception)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = exception.Message });
        }
    }

    [HttpPost("calls/{callId:guid}/resume")]
    [ProducesResponseType(typeof(TelephonyCallResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TelephonyCallResponseDto>> Resume(
        Guid callId,
        CancellationToken cancellationToken)
    {
        var actingAgentId = await GetActingAgentIdAsync(cancellationToken);

        try
        {
            return Ok(await telephonyService.ResumeCallAsync(
                callId,
                actingAgentId,
                IsPrivileged,
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
        catch (UnauthorizedAccessException exception)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = exception.Message });
        }
    }

    [HttpPost("calls/{callId:guid}/transfer")]
    [ProducesResponseType(typeof(TelephonyCallResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TelephonyCallResponseDto>> Transfer(
        Guid callId,
        [FromBody] TransferCallRequestDto request,
        CancellationToken cancellationToken)
    {
        var actingAgentId = await GetActingAgentIdAsync(cancellationToken);

        try
        {
            return Ok(await telephonyService.TransferCallAsync(
                callId,
                request,
                actingAgentId,
                IsPrivileged,
                cancellationToken));
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
        catch (UnauthorizedAccessException exception)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = exception.Message });
        }
    }

    [HttpGet("calls/{callId:guid}/transfer/eligible-agents")]
    [ProducesResponseType(typeof(IReadOnlyList<EligibleAgentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<EligibleAgentDto>>> GetEligibleTransferAgents(
        Guid callId,
        [FromQuery] TransferType transferType = TransferType.Blind,
        CancellationToken cancellationToken = default)
    {
        var actingAgentId = await GetActingAgentIdAsync(cancellationToken);

        try
        {
            return Ok(await telephonyService.GetEligibleTransferAgentsAsync(
                callId,
                transferType,
                actingAgentId,
                IsPrivileged,
                cancellationToken));
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (UnauthorizedAccessException exception)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = exception.Message });
        }
    }

    [HttpGet("provider")]
    [ProducesResponseType(typeof(TelephonyProviderInfoResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<TelephonyProviderInfoResponseDto>> GetProviderInfo(
        CancellationToken cancellationToken)
    {
        return Ok(await telephonyService.GetProviderInfoAsync(cancellationToken));
    }

    [HttpGet("token")]
    [ProducesResponseType(typeof(TelephonyTokenResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<TelephonyTokenResponseDto>> GetVoiceToken(
        CancellationToken cancellationToken)
    {
        var actingAgentId = await GetActingAgentIdAsync(cancellationToken);
        if (actingAgentId is null && !IsPrivileged)
        {
            return Forbid();
        }

        var identity = actingAgentId.HasValue
            ? $"agent_{actingAgentId.Value:N}"
            : (User.FindFirstValue(ClaimTypes.Name) ?? User.FindFirstValue(ClaimTypes.Email) ?? "agent");

        var result = await telephonyService.GenerateVoiceTokenAsync(
            actingAgentId ?? Guid.Empty,
            identity,
            cancellationToken);

        return Ok(result);
    }

    [HttpPost("calls/{callId:guid}/recording/start")]
    [ProducesResponseType(typeof(TelephonyProviderResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<TelephonyProviderResult>> StartRecording(
        Guid callId,
        CancellationToken cancellationToken)
    {
        var actingAgentId = await GetActingAgentIdAsync(cancellationToken);

        try
        {
            var result = await telephonyService.StartRecordingAsync(
                callId,
                actingAgentId,
                IsPrivileged,
                cancellationToken);

            return Ok(result);
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
        catch (UnauthorizedAccessException exception)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = exception.Message });
        }
    }

    [HttpPost("calls/{callId:guid}/recording/stop")]
    [ProducesResponseType(typeof(TelephonyProviderResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<TelephonyProviderResult>> StopRecording(
        Guid callId,
        CancellationToken cancellationToken)
    {
        var actingAgentId = await GetActingAgentIdAsync(cancellationToken);

        try
        {
            var result = await telephonyService.StopRecordingAsync(
                callId,
                actingAgentId,
                IsPrivileged,
                cancellationToken);

            return Ok(result);
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
        catch (UnauthorizedAccessException exception)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = exception.Message });
        }
    }

    [HttpPost("webhooks/inbound")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(TelephonyCallResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> IngestInboundWebhook(CancellationToken cancellationToken)
    {
        string rawBody;
        var headers = Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        var requestUrl = $"{Request.Scheme}://{Request.Host}{Request.Path}{Request.QueryString}";
        headers["X-Request-Url"] = requestUrl;

        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync(cancellationToken);
            var sb = new System.Text.StringBuilder();
            foreach (var kvp in form)
            {
                if (sb.Length > 0) sb.Append('&');
                sb.Append(Uri.EscapeDataString(kvp.Key)).Append('=').Append(Uri.EscapeDataString(kvp.Value.ToString()));
            }
            rawBody = sb.ToString();
        }
        else
        {
            using var reader = new StreamReader(Request.Body);
            rawBody = await reader.ReadToEndAsync(cancellationToken);
        }

        try
        {
            var payload = new TelephonyWebhookPayload("Inbound", rawBody, headers);
            var call = await telephonyService.ProcessInboundWebhookAsync(payload, cancellationToken);

            var isTwilioOrXml = Request.Headers.Accept.Any(a => a != null && a.Contains("xml", StringComparison.OrdinalIgnoreCase)) ||
                headers.ContainsKey("X-Twilio-Signature") || headers.ContainsKey("x-twilio-signature") ||
                (Request.ContentType != null && Request.ContentType.Contains("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase));

            if (isTwilioOrXml)
            {
                var options = telephonyOptions.Value;
                var callbackBase = !string.IsNullOrWhiteSpace(options.CallbackBaseUrl)
                    ? options.CallbackBaseUrl
                    : $"{Request.Scheme}://{Request.Host}";
                var statusCallbackUrl = $"{callbackBase.TrimEnd('/')}/api/v1/telephony/webhooks/status";
                var callerId = options.VoiceNumber ?? options.DefaultCallerId ?? "+15005550006";

                string twiml;
                if (call.AssignedAgentId.HasValue)
                {
                    var agentIdentity = $"agent_{call.AssignedAgentId.Value:N}";
                    twiml = $"""
                        <?xml version="1.0" encoding="UTF-8"?>
                        <Response>
                            <Say voice="Polly.Joanna">Thank you for calling Falaq Food. Connecting you to an agent.</Say>
                            <Dial callerId="{callerId}" record="record-from-answer" statusCallback="{statusCallbackUrl}" statusCallbackEvent="initiated ringing answered completed">
                                <Client>{agentIdentity}</Client>
                            </Dial>
                        </Response>
                        """;
                }
                else
                {
                    twiml = $"""
                        <?xml version="1.0" encoding="UTF-8"?>
                        <Response>
                            <Say voice="Polly.Joanna">Thank you for calling Falaq Food. All of our agents are currently assisting other customers. Please hold while we connect your call.</Say>
                            <Play>{callbackBase.TrimEnd('/')}/audio/hold-music.mp3</Play>
                        </Response>
                        """;
                }

                return Content(twiml, "application/xml", System.Text.Encoding.UTF8);
            }

            return Ok(call);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("webhooks/outbound")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> IngestOutboundWebhook(CancellationToken cancellationToken)
    {
        var headers = Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        var requestUrl = $"{Request.Scheme}://{Request.Host}{Request.Path}{Request.QueryString}";
        headers["X-Request-Url"] = requestUrl;

        try
        {
            var queryParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync(cancellationToken);
            foreach (var kvp in form)
            {
                queryParams[kvp.Key] = kvp.Value.ToString();
            }
        }
        else
        {
            using var reader = new StreamReader(Request.Body);
            var rawBody = await reader.ReadToEndAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(rawBody))
            {
                foreach (var part in rawBody.Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = part.Split('=', 2);
                    if (kv.Length == 2)
                    {
                        queryParams[Uri.UnescapeDataString(kv[0])] = Uri.UnescapeDataString(kv[1]);
                    }
                }
            }
        }

        foreach (var q in Request.Query)
        {
            queryParams[q.Key] = q.Value.ToString();
        }

            var to = queryParams.GetValueOrDefault("To") ?? "";
            var from = queryParams.GetValueOrDefault("From") ?? "";

            var options = telephonyOptions.Value;
            var callbackBase = !string.IsNullOrWhiteSpace(options.CallbackBaseUrl)
                ? options.CallbackBaseUrl
                : $"{Request.Scheme}://{Request.Host}";
            var statusCallbackUrl = $"{callbackBase.TrimEnd('/')}/api/v1/telephony/webhooks/status";
            var callerId = options.VoiceNumber ?? options.DefaultCallerId ?? "+15005550006";

            string twiml;
            if (to.StartsWith("client:", StringComparison.OrdinalIgnoreCase) || to.StartsWith("agent_", StringComparison.OrdinalIgnoreCase))
            {
                var targetClient = to.Replace("client:", "", StringComparison.OrdinalIgnoreCase);
                twiml = $"""
                    <?xml version="1.0" encoding="UTF-8"?>
                    <Response>
                        <Dial callerId="{callerId}" record="record-from-answer" statusCallback="{statusCallbackUrl}" statusCallbackEvent="initiated ringing answered completed">
                            <Client>{targetClient}</Client>
                        </Dial>
                    </Response>
                    """;
            }
            else
            {
                twiml = $"""
                    <?xml version="1.0" encoding="UTF-8"?>
                    <Response>
                        <Dial callerId="{callerId}" record="record-from-answer" statusCallback="{statusCallbackUrl}" statusCallbackEvent="initiated ringing answered completed">
                            <Number>{to}</Number>
                        </Dial>
                    </Response>
                    """;
            }

            return Content(twiml, "application/xml", System.Text.Encoding.UTF8);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private async Task<string> ReadRequestBodyAsync(CancellationToken cancellationToken)
    {
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync(cancellationToken);
            var sb = new System.Text.StringBuilder();
            foreach (var kvp in form)
            {
                if (sb.Length > 0) sb.Append('&');
                sb.Append(Uri.EscapeDataString(kvp.Key)).Append('=').Append(Uri.EscapeDataString(kvp.Value.ToString()));
            }
            return sb.ToString();
        }

        using var reader = new StreamReader(Request.Body);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    [HttpPost("webhooks/status")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> IngestStatusWebhook(CancellationToken cancellationToken)
    {
        var rawBody = await ReadRequestBodyAsync(cancellationToken);
        var headers = Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);

        var requestUrl = $"{Request.Scheme}://{Request.Host}{Request.Path}{Request.QueryString}";
        headers["X-Request-Url"] = requestUrl;

        try
        {
            var payload = new TelephonyWebhookPayload("Status", rawBody, headers);
            var result = await telephonyService.ProcessCallStatusWebhookAsync(payload, cancellationToken);
            return Ok(new { processed = result is not null, call = result });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }

    [HttpPost("webhooks/recording")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> IngestRecordingWebhook(CancellationToken cancellationToken)
    {
        var rawBody = await ReadRequestBodyAsync(cancellationToken);
        var headers = Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);

        var requestUrl = $"{Request.Scheme}://{Request.Host}{Request.Path}{Request.QueryString}";
        headers["X-Request-Url"] = requestUrl;

        try
        {
            var payload = new TelephonyWebhookPayload("Recording", rawBody, headers);
            var handled = await telephonyService.ProcessRecordingWebhookAsync(payload, cancellationToken);

            return Ok(new { processed = handled });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }
}

