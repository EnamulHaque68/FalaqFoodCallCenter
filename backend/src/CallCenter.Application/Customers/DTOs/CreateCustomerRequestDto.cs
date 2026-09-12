using System.ComponentModel.DataAnnotations;

namespace CallCenter.Application.Customers.DTOs;

public sealed class CreateCustomerRequestDto
{
    [Required]
    [StringLength(150, MinimumLength = 2)]
    public string FullName { get; set; } = null!;

    [Required]
    [Phone]
    [StringLength(30, MinimumLength = 7)]
    public string Phone { get; set; } = null!;

    [EmailAddress]
    [StringLength(254)]
    public string? Email { get; set; }

    [StringLength(500)]
    public string? Address { get; set; }

    [StringLength(100)]
    public string? CrmCustomerId { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }
}
