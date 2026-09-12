namespace CallCenter.Domain.Enums;

public enum CallStatus
{
    Queued = 1,
    Ringing = 2,
    Connected = 3,
    Completed = 4,
    Abandoned = 5,
    Rejected = 6,
    Failed = 7,
    OnHold = 8
}
