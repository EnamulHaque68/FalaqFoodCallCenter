namespace CallCenter.Application.Customers.DTOs;

public sealed class CustomerResponseDto
{
    public Guid Id { get; init; }
    public string FullName { get; init; } = null!;
    public string Phone { get; init; } = null!;
    public string? Email { get; init; }
    public string? Address { get; init; }
    public string? CrmCustomerId { get; init; }
    public string? Notes { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}
