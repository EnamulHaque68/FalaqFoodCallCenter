using CallCenter.Domain.Enums;

namespace CallCenter.Application.Telephony.DTOs;

public sealed class TransferCallRequestDto
{
    public Guid? TargetAgentId { get; set; }
    public Guid? TargetQueueId { get; set; }
    public TransferType TransferType { get; set; } = TransferType.Blind;
    public string? Reason { get; set; }
}
