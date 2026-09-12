using System.ComponentModel.DataAnnotations;
using CallCenter.Domain.Enums;

namespace CallCenter.Application.Calls.DTOs;

public sealed class TransitionCallRequestDto
{
    [Required]
    [EnumDataType(typeof(CallStatus))]
    public CallStatus Status { get; set; }
}
