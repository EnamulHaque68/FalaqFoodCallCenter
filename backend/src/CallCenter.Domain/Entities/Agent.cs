using CallCenter.Domain.Enums;

namespace CallCenter.Domain.Entities;

public sealed class Agent
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string EmployeeCode { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string? Team { get; set; }
    public bool IsActive { get; set; } = true;
    public AgentStatus Status { get; set; } = AgentStatus.Offline;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public User User { get; set; } = null!;
    public ICollection<Call> AssignedCalls { get; set; } = new List<Call>();
    public ICollection<CallEvent> CallEvents { get; set; } = new List<CallEvent>();
}
