namespace DiskHealthAdvisor.Models;

public sealed class LogicalVolumeInfo
{
    public string? Name { get; set; }
    public string? FileSystem { get; set; }
    public ulong? SizeBytes { get; set; }
    public ulong? FreeBytes { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Нет данных" : Name;
}
