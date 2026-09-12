namespace CallCenter.Domain.Entities;

public sealed class AuditLog
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public string Action { get; set; } = null!;
    public string EntityName { get; set; } = null!;
    public string? EntityId { get; set; }
    public string? DetailsJson { get; set; }
    public DateTime CreatedAt { get; set; }

    public User? User { get; set; }
}
