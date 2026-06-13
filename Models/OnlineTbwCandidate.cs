namespace DiskHealthAdvisor.Models;

public sealed class OnlineTbwCandidate
{
    public string Model { get; set; } = "";
    public int? CapacityGb { get; set; }
    public decimal Tbw { get; set; }
    public int? WarrantyYears { get; set; }
    public string? MemoryType { get; set; }
    public string Source { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string Evidence { get; set; } = "";
    public string Warning { get; set; } = "";

    public string TbwText => $"{Tbw:0.##} ТБ";
    public string CapacityText => CapacityGb is null ? "Ёмкость не уточнена" : $"{CapacityGb} ГБ";
    public string WarrantyText => WarrantyYears is null ? "Гарантия не указана" : $"{WarrantyYears} лет";
}
