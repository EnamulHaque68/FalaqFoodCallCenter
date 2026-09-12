namespace CallCenter.Domain.Entities;

public sealed class Customer
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = null!;
    public string PhoneNumber { get; set; } = null!;
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? CrmCustomerId { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public ICollection<Call> Calls { get; set; } = new List<Call>();
}
