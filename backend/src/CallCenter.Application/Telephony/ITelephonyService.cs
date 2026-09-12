using CallCenter.Application.Calls.DTOs;
using CallCenter.Application.Telephony.DTOs;
using CallCenter.Application.Telephony.Providers;
using CallCenter.Domain.Enums;

namespace CallCenter.Application.Telephony;

public interface ITelephonyService
{
    Task<TelephonyCallResponseDto> SimulateIncomingCallAsync(
        SimulateIncomingCallRequestDto request,
        CancellationToken cancellationToken = default);

    Task<TelephonyCallResponseDto> InitiateOutboundCallAsync(
        InitiateOutboundCallRequestDto request,
        Guid actingAgentId,
        bool isPrivilegedCaller,
        CancellationToken cancellationToken = default);

    Task<TelephonyCallResponseDto> AcceptCallAsync(
        Guid callId,
        Guid? actingAgentId,
        bool isPrivilegedCaller,
        CancellationToken cancellationToken = default);

    Task<TelephonyCallResponseDto> RejectCallAsync(
        Guid callId,
        Guid? actingAgentId,
        bool isPrivilegedCaller,
        CancellationToken cancellationToken = default);

    Task<TelephonyCallResponseDto> EndCallAsync(
        Guid callId,
        Guid? actingAgentId,
        bool isPrivilegedCaller,
        CancellationToken cancellationToken = default);

    Task<TelephonyCallResponseDto> CompleteCallAsync(
        Guid callId,
        CompleteCallRequestDto request,
        Guid? actingAgentId,
        bool isPrivilegedCaller,
        CancellationToken cancellationToken = default);

    Task<TelephonyCallResponseDto> HoldCallAsync(
        Guid callId,
        Guid? actingAgentId,
        bool isPrivilegedCaller,
        CancellationToken cancellationToken = default);

    Task<TelephonyCallResponseDto> ResumeCallAsync(
        Guid callId,
        Guid? actingAgentId,
        bool isPrivilegedCaller,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EligibleAgentDto>> GetEligibleTransferAgentsAsync(
        Guid callId,
        TransferType transferType,
        Guid? actingAgentId,
        bool isPrivilegedCaller,
        CancellationToken cancellationToken = default);

    Task<TelephonyCallResponseDto> TransferCallAsync(
        Guid callId,
        TransferCallRequestDto request,
        Guid? actingAgentId,
        bool isPrivilegedCaller,
        CancellationToken cancellationToken = default);

    Task<TelephonyProviderResult> StartRecordingAsync(
        Guid callId,
        Guid? actingAgentId,
        bool isPrivilegedCaller,
        CancellationToken cancellationToken = default);

    Task<TelephonyProviderResult> StopRecordingAsync(
        Guid callId,
        Guid? actingAgentId,
        bool isPrivilegedCaller,
        CancellationToken cancellationToken = default);

    Task<bool> ProcessRecordingWebhookAsync(
        TelephonyWebhookPayload payload,
        CancellationToken cancellationToken = default);

    Task<TelephonyCallResponseDto> ProcessInboundWebhookAsync(
        TelephonyWebhookPayload payload,
        CancellationToken cancellationToken = default);

    Task<TelephonyCallResponseDto?> ProcessCallStatusWebhookAsync(
        TelephonyWebhookPayload payload,
        CancellationToken cancellationToken = default);

    Task<TelephonyProviderInfoResponseDto> GetProviderInfoAsync(
        CancellationToken cancellationToken = default);

    Task<TelephonyTokenResponseDto> GenerateVoiceTokenAsync(
        Guid actingAgentId,
        string agentIdentity,
        CancellationToken cancellationToken = default);
}
