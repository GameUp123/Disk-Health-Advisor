namespace DiskHealthAdvisor.Models;

public sealed class DiskSnapshot
{
    public DateTimeOffset Timestamp { get; set; }
    public string DiskIdentity { get; set; } = "";
    public string? Model { get; set; }
    public string? Serial { get; set; }
    public int? TemperatureCelsius { get; set; }
    public ulong? PowerOnHours { get; set; }
    public ulong? TotalBytesWritten { get; set; }
    public ulong? TotalBytesRead { get; set; }
    public int? WearPercentage { get; set; }
    public ulong? UnsafeShutdowns { get; set; }
    public ulong? MediaErrors { get; set; }
    public ulong? ReallocatedSectors { get; set; }
    public ulong? CurrentPendingSectors { get; set; }
    public ulong? UncorrectableErrors { get; set; }
    public ulong? CrcErrors { get; set; }
    public HealthLevel HealthLevel { get; set; }
}
