namespace CallCenter.Application.Customers.DTOs;

public sealed class CustomerLookupDto
{
    public Guid Id { get; init; }
    public string FullName { get; init; } = null!;
    public string Phone { get; init; } = null!;
    public string? Email { get; init; }
    public string? Address { get; init; }
}
