using System.Collections.ObjectModel;

namespace DiskHealthAdvisor.Models;

public sealed class DiskInfo
{
    public string Id { get; set; } = "";
    public string? Model { get; set; }
    public string? Serial { get; set; }
    public string? Firmware { get; set; }
    public string? BusType { get; set; }
    public DiskMediaKind MediaType { get; set; } = DiskMediaKind.Unknown;
    public ulong? SizeBytes { get; set; }
    public ObservableCollection<LogicalVolumeInfo> LogicalVolumes { get; set; } = [];
    public int? TemperatureCelsius { get; set; }
    public string TemperatureSource { get; set; } = "";
    public ulong? PowerOnHours { get; set; }
    public ulong? PowerCycleCount { get; set; }
    public ulong? TotalBytesWritten { get; set; }
    public ulong? TotalBytesRead { get; set; }
    public int? WearPercentage { get; set; }
    public ulong? UnsafeShutdowns { get; set; }
    public ulong? MediaErrors { get; set; }
    public ulong? ReallocatedSectors { get; set; }
    public ulong? CurrentPendingSectors { get; set; }
    public ulong? UncorrectableErrors { get; set; }
    public ulong? CrcErrors { get; set; }
    public bool? SmartPassed { get; set; }
    public ObservableCollection<SmartAttributeInfo> RawAttributes { get; set; } = [];
    public ObservableCollection<string> DataSourceWarnings { get; set; } = [];
    public string HealthBadge { get; set; } = "Нет данных";
    public string HealthBadgeBrush { get; set; } = "#6F7B8A";
    public string UserProfile { get; set; } = "Не задан";

    public string Identity => !string.IsNullOrWhiteSpace(Serial)
        ? $"{Model ?? "Unknown"}::{Serial}"
        : Id;

    public string MediaTypeDisplay => MediaType switch
    {
        DiskMediaKind.HDD => "HDD",
        DiskMediaKind.SSD => "SSD",
        DiskMediaKind.SataSSD => "SATA SSD",
        DiskMediaKind.NvmeSSD => "NVMe SSD",
        DiskMediaKind.USB => "USB",
        _ => "Unknown"
    };
}
