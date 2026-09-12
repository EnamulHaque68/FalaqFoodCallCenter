using System.ComponentModel.DataAnnotations;

namespace CallCenter.Application.Telephony.DTOs;

public sealed class InitiateOutboundCallRequestDto
{
    public Guid? CustomerId { get; set; }
    public Guid? AgentId { get; set; }

    [Required]
    [StringLength(30, MinimumLength = 7)]
    public string PhoneNumber { get; set; } = null!;

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string CorrelationId { get; set; } = null!;

    [StringLength(150)]
    public string? IdempotencyKey { get; set; }
}

