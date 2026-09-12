using CallCenter.Application.Audit;
using CallCenter.Application.CRM;
using CallCenter.Application.Customers;
using CallCenter.Application.Customers.DTOs;
using CallCenter.Domain.Entities;
using CallCenter.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Infrastructure.Customers;

public sealed class CustomerService(
    CallCenterDbContext dbContext,
    ICrmService crmService,
    IAuditLogService? auditLogService = null) : ICustomerService
{
    public async Task<CustomerPagedResultDto> GetPagedAsync(
        string? search, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page));
        if (pageSize < 1 || pageSize > 100) throw new ArgumentOutOfRangeException(nameof(pageSize));

        var query = dbContext.Customers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.DisplayName.Contains(term) || x.PhoneNumber.Contains(term) ||
                (x.Email != null && x.Email.Contains(term)) ||
                (x.CrmCustomerId != null && x.CrmCustomerId.Contains(term)) ||
                (x.Notes != null && x.Notes.Contains(term)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
        var customers = await query
            .OrderBy(x => x.DisplayName)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = customers
            .Select(ToDto)
            .ToList();

        return new CustomerPagedResultDto { Items = items, Page = page, PageSize = pageSize, TotalCount = totalCount, TotalPages = totalPages };
    }

    public async Task<CustomerResponseDto?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var customer = await dbContext.Customers
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        return customer is null ? null : ToDto(customer);
    }

    public async Task<CustomerLookupDto?> LookupByPhoneAsync(string phone, CancellationToken cancellationToken = default)
    {
        var normalizedPhone = NormalizePhone(phone);
        if (string.IsNullOrWhiteSpace(normalizedPhone)) throw new ArgumentException("A valid phone number is required.", nameof(phone));
        return await crmService.FindCustomerByPhoneAsync(normalizedPhone, cancellationToken);
    }

    public async Task<CustomerResponseDto> CreateAsync(
        CreateCustomerRequestDto request,
        Guid? actorUserId = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedPhone = NormalizePhone(request.Phone);
        if (string.IsNullOrWhiteSpace(normalizedPhone)) throw new ArgumentException("A valid phone number is required.", nameof(request.Phone));
        if (await dbContext.Customers.AnyAsync(x => x.PhoneNumber == normalizedPhone, cancellationToken))
            throw new InvalidOperationException("A customer with the specified phone number already exists.");

        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            DisplayName = request.FullName.Trim(),
            PhoneNumber = normalizedPhone,
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim(),
            CrmCustomerId = string.IsNullOrWhiteSpace(request.CrmCustomerId) ? null : request.CrmCustomerId.Trim(),
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            CreatedAt = DateTime.UtcNow
        };
        dbContext.Customers.Add(customer);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (auditLogService is not null)
        {
            await auditLogService.LogAsync(
                actorUserId,
                "CustomerCreated",
                "Customer",
                customer.Id.ToString(),
                new { customer.DisplayName, customer.PhoneNumber, customer.Email },
                cancellationToken);
        }

        return ToDto(customer);
    }

    public async Task<CustomerResponseDto?> UpdateAsync(
        Guid id,
        UpdateCustomerRequestDto request,
        Guid? actorUserId = null,
        CancellationToken cancellationToken = default)
    {
        var customer = await dbContext.Customers.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (customer is null) return null;
        var normalizedPhone = NormalizePhone(request.Phone);
        if (string.IsNullOrWhiteSpace(normalizedPhone)) throw new ArgumentException("A valid phone number is required.", nameof(request.Phone));
        if (await dbContext.Customers.AnyAsync(x => x.Id != id && x.PhoneNumber == normalizedPhone, cancellationToken))
            throw new InvalidOperationException("A customer with the specified phone number already exists.");

        customer.DisplayName = request.FullName.Trim();
        customer.PhoneNumber = normalizedPhone;
        customer.Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        customer.Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim();
        customer.CrmCustomerId = string.IsNullOrWhiteSpace(request.CrmCustomerId) ? null : request.CrmCustomerId.Trim();
        customer.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        customer.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        if (auditLogService is not null)
        {
            await auditLogService.LogAsync(
                actorUserId,
                "CustomerUpdated",
                "Customer",
                customer.Id.ToString(),
                new { customer.DisplayName, customer.PhoneNumber, customer.Email },
                cancellationToken);
        }

        return ToDto(customer);
    }

    public async Task<bool> DeleteAsync(
        Guid id,
        Guid? actorUserId = null,
        CancellationToken cancellationToken = default)
    {
        var customer = await dbContext.Customers.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (customer is null) return false;
        if (await dbContext.Calls.AnyAsync(x => x.CustomerId == id, cancellationToken))
            throw new InvalidOperationException("The customer cannot be deleted while call records reference the customer.");
        dbContext.Customers.Remove(customer);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (auditLogService is not null)
        {
            await auditLogService.LogAsync(
                actorUserId,
                "CustomerDeleted",
                "Customer",
                id.ToString(),
                new { customer.DisplayName, customer.PhoneNumber },
                cancellationToken);
        }

        return true;
    }

    private static string NormalizePhone(string phone) => new(phone.Where(char.IsDigit).ToArray());

    private static CustomerResponseDto ToDto(Customer x) => new()
    {
        Id = x.Id,
        FullName = x.DisplayName,
        Phone = x.PhoneNumber,
        Email = x.Email,
        Address = x.Address,
        CrmCustomerId = x.CrmCustomerId,
        Notes = x.Notes,
        CreatedAt = x.CreatedAt,
        UpdatedAt = x.UpdatedAt
    };
}
