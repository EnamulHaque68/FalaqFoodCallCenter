using CallCenter.Application.CRM;
using CallCenter.Application.Customers.DTOs;
using CallCenter.Domain.Entities;
using CallCenter.Infrastructure.Customers;
using Moq;

namespace CallCenter.Tests;

public sealed class CustomerLookupTests
{
    [Fact]
    public async Task Lookup_by_phone_delegates_to_CRM_abstraction()
    {
        await using var db = TestDbContextFactory.Create();
        var expected = new CustomerLookupDto { Id=Guid.NewGuid(), FullName="Rahim", Phone="8801712345678" };
        var crm = new Mock<ICrmService>();
        crm.Setup(x => x.FindCustomerByPhoneAsync("8801712345678", It.IsAny<CancellationToken>())).ReturnsAsync(expected);

        var service = new CustomerService(db, crm.Object);
        var result = await service.LookupByPhoneAsync("+880 1712-345678");

        Assert.Equal(expected.Id, result!.Id);
        crm.Verify(x => x.FindCustomerByPhoneAsync("8801712345678", It.IsAny<CancellationToken>()), Times.Once);
    }
}
