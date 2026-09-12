using CallCenter.Application.Customers.DTOs;

namespace CallCenter.Application.Customers;

public interface ICustomerService
{
    Task<CustomerPagedResultDto> GetPagedAsync(
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<CustomerResponseDto?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<CustomerLookupDto?> LookupByPhoneAsync(
        string phone,
        CancellationToken cancellationToken = default);

    Task<CustomerResponseDto> CreateAsync(
        CreateCustomerRequestDto request,
        Guid? actorUserId = null,
        CancellationToken cancellationToken = default);

    Task<CustomerResponseDto?> UpdateAsync(
        Guid id,
        UpdateCustomerRequestDto request,
        Guid? actorUserId = null,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        Guid id,
        Guid? actorUserId = null,
        CancellationToken cancellationToken = default);
}
