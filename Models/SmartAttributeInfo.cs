namespace DiskHealthAdvisor.Models;

public sealed class SmartAttributeInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? RawValue { get; set; }
    public int? NormalizedValue { get; set; }
    public int? Threshold { get; set; }
    public string? Comment { get; set; }
}
