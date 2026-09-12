namespace CallCenter.Domain.Entities;

public sealed class SystemSetting
{
    public Guid Id { get; set; }
    public string Key { get; set; } = null!;
    public string Value { get; set; } = null!;
    public string Category { get; set; } = "General";
    public string? Description { get; set; }
    public string DataType { get; set; } = "String";
    public DateTime UpdatedAt { get; set; }
}
