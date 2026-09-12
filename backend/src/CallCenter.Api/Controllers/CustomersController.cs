using CallCenter.Application.Calls;
using CallCenter.Application.Calls.DTOs;
using CallCenter.Application.Customers;
using CallCenter.Application.Customers.DTOs;
using CallCenter.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CallCenter.Api.Controllers;

[ApiController]
[Route("api/v1/customers")]
[Authorize]
public sealed class CustomersController(
    ICustomerService customerService,
    ICallService callService) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = AppPermissions.CustomersView)]
    [ProducesResponseType(typeof(CustomerPagedResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CustomerPagedResultDto>> GetPaged(
        [FromQuery] string? search,
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

        return Ok(await customerService.GetPagedAsync(
            search,
            page,
            pageSize,
            cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = AppPermissions.CustomersView)]
    [ProducesResponseType(typeof(CustomerResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerResponseDto>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var customer = await customerService.GetByIdAsync(id, cancellationToken);

        return customer is null ? NotFound() : Ok(customer);
    }

    [HttpGet("{id:guid}/calls")]
    [Authorize(Policy = AppPermissions.CallsView)]
    [ProducesResponseType(typeof(CallPagedResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CallPagedResultDto>> GetCustomerCalls(
        Guid id,
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

        var calls = await callService.GetHistoryAsync(
            search: null,
            customerId: id,
            agentId: null,
            direction: null,
            status: null,
            page: page,
            pageSize: pageSize,
            cancellationToken: cancellationToken);

        return Ok(calls);
    }

    [HttpGet("lookup/phone")]
    [Authorize(Policy = AppPermissions.CustomersView)]
    [ProducesResponseType(typeof(CustomerLookupDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CustomerLookupDto>> LookupByPhone(
        [FromQuery] string phone,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return BadRequest(new { message = "Phone is required." });
        }

        var customer = await customerService.LookupByPhoneAsync(
            phone,
            cancellationToken);

        return customer is null ? NotFound() : Ok(customer);
    }

    [HttpPost]
    [Authorize(Policy = AppPermissions.CustomersCreate)]
    [ProducesResponseType(typeof(CustomerResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerResponseDto>> Create(
        [FromBody] CreateCustomerRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var customer = await customerService.CreateAsync(
                request,
                GetUserId(),
                cancellationToken);

            return CreatedAtAction(
                nameof(GetById),
                new { id = customer.Id },
                customer);
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

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AppPermissions.CustomersEdit)]
    [ProducesResponseType(typeof(CustomerResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerResponseDto>> Update(
        Guid id,
        [FromBody] UpdateCustomerRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var customer = await customerService.UpdateAsync(
                id,
                request,
                GetUserId(),
                cancellationToken);

            return customer is null ? NotFound() : Ok(customer);
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

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AppPermissions.CustomersDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            var deleted = await customerService.DeleteAsync(
                id,
                GetUserId(),
                cancellationToken);

            return deleted ? NoContent() : NotFound();
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    private Guid? GetUserId()
    {
        var claim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var guid) ? guid : null;
    }
}
