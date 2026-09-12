namespace CallCenter.Domain.Entities;

public sealed class User
{
    public Guid Id { get; set; }
    public Guid RoleId { get; set; }
    public string UserName { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public int FailedLoginAttempts { get; set; }
    public DateTime? LockoutEndUtc { get; set; }

    public Role Role { get; set; } = null!;
    public Agent? Agent { get; set; }
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();

    public bool IsLockedOut(DateTime utcNow) =>
        LockoutEndUtc.HasValue && LockoutEndUtc.Value > utcNow;
}
