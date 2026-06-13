namespace DiskHealthAdvisor.Models;

public sealed class SsdTbwRecord
{
    public string Model { get; set; } = "";
    public int? CapacityGb { get; set; }
    public decimal Tbw { get; set; }
    public int? WarrantyYears { get; set; }
    public string? MemoryType { get; set; }
    public string Source { get; set; } = "manual";
    public string? Comment { get; set; }
}
