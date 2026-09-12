using CallCenter.Application.CRM;
using CallCenter.Application.Customers.DTOs;
using CallCenter.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Infrastructure.CRM;

public sealed class MockCrmService(CallCenterDbContext dbContext) : ICrmService
{
    public async Task<CustomerLookupDto?> FindCustomerByPhoneAsync(string phone, CancellationToken cancellationToken = default)
    {
        var normalizedPhone = NormalizePhone(phone);
        if (string.IsNullOrWhiteSpace(normalizedPhone)) return null;
        return await dbContext.Customers.AsNoTracking().Where(x => x.PhoneNumber == normalizedPhone)
            .Select(x => new CustomerLookupDto { Id=x.Id, FullName=x.DisplayName, Phone=x.PhoneNumber, Email=x.Email, Address=null })
            .SingleOrDefaultAsync(cancellationToken);
    }
    private static string NormalizePhone(string phone) => new(phone.Where(char.IsDigit).ToArray());
}
