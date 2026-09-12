namespace CallCenter.Domain.Entities;

public sealed class CallDisposition
{
    public Guid Id { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public bool RequiresFollowUp { get; set; } = false;
    public bool RequiresNotes { get; set; } = false;
    public int SortOrder { get; set; } = 0;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public ICollection<Call> Calls { get; set; } = new List<Call>();
}
