namespace CallCenter.Application.Dispositions.DTOs;

public sealed class CallDispositionDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = null!;
    public string Name { get; init; } = null!;
    public string? Description { get; init; }
    public bool RequiresFollowUp { get; init; }
    public bool RequiresNotes { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}
