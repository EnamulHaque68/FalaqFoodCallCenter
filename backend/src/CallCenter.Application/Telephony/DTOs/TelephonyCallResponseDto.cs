using CallCenter.Domain.Enums;

namespace CallCenter.Application.Telephony.DTOs;

public sealed class TelephonyCallResponseDto
{
    public Guid CallId { get; init; }
    public string ProviderCallId { get; init; } = null!;
    public CallDirection Direction { get; init; }
    public CallStatus Status { get; init; }
    public string PhoneNumber { get; init; } = null!;
    public string CorrelationId { get; init; } = null!;
    public Guid? CustomerId { get; init; }
    public Guid? AssignedAgentId { get; init; }
}
