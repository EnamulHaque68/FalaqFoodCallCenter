using System.ComponentModel.DataAnnotations;

namespace CallCenter.Application.Telephony.DTOs;

public sealed class SimulateIncomingCallRequestDto
{
    public Guid? CustomerId { get; set; }

    [Required]
    [StringLength(30, MinimumLength = 7)]
    public string PhoneNumber { get; set; } = null!;

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string CorrelationId { get; set; } = null!;
}
