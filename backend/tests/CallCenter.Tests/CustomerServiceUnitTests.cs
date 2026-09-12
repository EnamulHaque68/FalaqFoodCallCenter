using CallCenter.Application.Audit;
using CallCenter.Application.CRM;
using CallCenter.Application.Customers.DTOs;
using CallCenter.Domain.Entities;
using CallCenter.Infrastructure.Customers;
using Moq;

namespace CallCenter.Tests;

public sealed class CustomerServiceUnitTests
{
    [Fact]
    public async Task GetPagedAsync_PaginationAndSearch_ReturnsFilteredItems()
    {
        await using var db = TestDbContextFactory.Create();
        db.Customers.AddRange(
            new Customer { Id = Guid.NewGuid(), DisplayName = "Alice Smith", PhoneNumber = "8801711111111", Email = "alice@example.com", CreatedAt = DateTime.UtcNow },
            new Customer { Id = Guid.NewGuid(), DisplayName = "Bob Jones", PhoneNumber = "8801722222222", Email = "bob@example.com", Notes = "VIP Customer", CreatedAt = DateTime.UtcNow },
            new Customer { Id = Guid.NewGuid(), DisplayName = "Charlie Brown", PhoneNumber = "8801733333333", Email = "charlie@example.com", CreatedAt = DateTime.UtcNow }
        );
        await db.SaveChangesAsync();

        var service = new CustomerService(db, new Mock<ICrmService>().Object);

        // Test search filter
        var searchResult = await service.GetPagedAsync("Bob", 1, 10);
        Assert.Single(searchResult.Items);
        Assert.Equal("Bob Jones", searchResult.Items[0].FullName);

        // Test paging
        var pagedResult = await service.GetPagedAsync(null, 1, 2);
        Assert.Equal(2, pagedResult.Items.Count);
        Assert.Equal(3, pagedResult.TotalCount);
        Assert.Equal(2, pagedResult.TotalPages);

        // Invalid page arguments
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.GetPagedAsync(null, 0, 10));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.GetPagedAsync(null, 1, 0));
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsCustomerOrNull()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        db.Customers.Add(new Customer
        {
            Id = customerId,
            DisplayName = "Diana Prince",
            PhoneNumber = "8801744444444",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new CustomerService(db, new Mock<ICrmService>().Object);

        var found = await service.GetByIdAsync(customerId);
        Assert.NotNull(found);
        Assert.Equal("Diana Prince", found.FullName);

        var notFound = await service.GetByIdAsync(Guid.NewGuid());
        Assert.Null(notFound);
    }

    [Fact]
    public async Task CreateAsync_ValidCustomer_CreatesAndAudits()
    {
        await using var db = TestDbContextFactory.Create();
        var mockAudit = new Mock<IAuditLogService>();
        var service = new CustomerService(db, new Mock<ICrmService>().Object, mockAudit.Object);

        var request = new CreateCustomerRequestDto
        {
            FullName = "Evan Wright",
            Phone = "+880 1755-555555",
            Email = "evan@example.com",
            Address = "Dhaka, Bangladesh",
            Notes = "Regular customer"
        };

        var result = await service.CreateAsync(request, Guid.NewGuid());
        Assert.NotNull(result);
        Assert.Equal("Evan Wright", result.FullName);
        Assert.Equal("8801755555555", result.Phone);

        // Verify entity persisted in DB
        Assert.True(db.Customers.Any(c => c.PhoneNumber == "8801755555555"));
    }

    [Fact]
    public async Task CreateAsync_DuplicatePhone_ThrowsInvalidOperationException()
    {
        await using var db = TestDbContextFactory.Create();
        db.Customers.Add(new Customer
        {
            Id = Guid.NewGuid(),
            DisplayName = "Existing Customer",
            PhoneNumber = "8801766666666",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new CustomerService(db, new Mock<ICrmService>().Object);

        var request = new CreateCustomerRequestDto
        {
            FullName = "Duplicate Customer",
            Phone = "8801766666666"
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(request));
    }

    [Fact]
    public async Task UpdateAsync_UpdatesExistingCustomerSuccessfully()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        db.Customers.Add(new Customer
        {
            Id = customerId,
            DisplayName = "Fiona Gallagher",
            PhoneNumber = "8801777777777",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new CustomerService(db, new Mock<ICrmService>().Object);

        var updateRequest = new UpdateCustomerRequestDto
        {
            FullName = "Fiona Updated",
            Phone = "8801777777778",
            Notes = "Updated notes"
        };

        var updated = await service.UpdateAsync(customerId, updateRequest);
        Assert.NotNull(updated);
        Assert.Equal("Fiona Updated", updated.FullName);
        Assert.Equal("8801777777778", updated.Phone);
        Assert.Equal("Updated notes", updated.Notes);
    }

    [Fact]
    public async Task DeleteAsync_WhenNoCallsReferenced_DeletesSuccessfully()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        db.Customers.Add(new Customer
        {
            Id = customerId,
            DisplayName = "George Weasley",
            PhoneNumber = "8801788888888",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new CustomerService(db, new Mock<ICrmService>().Object);

        var deleted = await service.DeleteAsync(customerId);
        Assert.True(deleted);
        Assert.False(db.Customers.Any(c => c.Id == customerId));

        var deleteNonExistent = await service.DeleteAsync(Guid.NewGuid());
        Assert.False(deleteNonExistent);
    }

    [Fact]
    public async Task DeleteAsync_WhenCallsReferenced_ThrowsInvalidOperationException()
    {
        await using var db = TestDbContextFactory.Create();
        var customerId = Guid.NewGuid();
        db.Customers.Add(new Customer
        {
            Id = customerId,
            DisplayName = "Harry Potter",
            PhoneNumber = "8801799999999",
            CreatedAt = DateTime.UtcNow
        });
        db.Calls.Add(new Call
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            PhoneNumber = "8801799999999",
            CorrelationId = "call-1",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new CustomerService(db, new Mock<ICrmService>().Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(customerId));
    }
}
