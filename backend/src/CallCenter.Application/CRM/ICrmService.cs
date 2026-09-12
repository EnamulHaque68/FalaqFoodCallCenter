using CallCenter.Application.Customers.DTOs;

namespace CallCenter.Application.CRM;

public interface ICrmService
{
    Task<CustomerLookupDto?> FindCustomerByPhoneAsync(
        string phone,
        CancellationToken cancellationToken = default);
}
