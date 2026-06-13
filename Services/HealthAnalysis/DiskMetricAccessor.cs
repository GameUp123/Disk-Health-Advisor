using DiskHealthAdvisor.Models;

namespace DiskHealthAdvisor.Services.HealthAnalysis;

public static class DiskMetricAccessor
{
    public static decimal? GetCurrent(DiskInfo disk, string metric, decimal? tbwUsedPercent = null, decimal? freeSpacePercent = null, decimal? dailyWriteGb = null)
    {
        return metric switch
        {
            nameof(DiskInfo.TemperatureCelsius) => disk.TemperatureCelsius,
            nameof(DiskInfo.PowerOnHours) => disk.PowerOnHours,
            nameof(DiskInfo.PowerCycleCount) => disk.PowerCycleCount,
            nameof(DiskInfo.TotalBytesWritten) => disk.TotalBytesWritten,
            nameof(DiskInfo.TotalBytesRead) => disk.TotalBytesRead,
            nameof(DiskInfo.WearPercentage) => disk.WearPercentage,
            nameof(DiskInfo.UnsafeShutdowns) => disk.UnsafeShutdowns,
            nameof(DiskInfo.MediaErrors) => disk.MediaErrors,
            nameof(DiskInfo.ReallocatedSectors) => disk.ReallocatedSectors,
            nameof(DiskInfo.CurrentPendingSectors) => disk.CurrentPendingSectors,
            nameof(DiskInfo.UncorrectableErrors) => disk.UncorrectableErrors,
            nameof(DiskInfo.CrcErrors) => disk.CrcErrors,
            nameof(DiskInfo.SmartPassed) => disk.SmartPassed is null ? null : disk.SmartPassed.Value ? 1 : 0,
            "TbwUsedPercent" => tbwUsedPercent,
            "FreeSpacePercent" => freeSpacePercent,
            "DailyWriteGb" => dailyWriteGb,
            _ => null
        };
    }

    public static decimal? GetSnapshot(DiskSnapshot? snapshot, string metric, decimal? freeSpacePercent = null)
    {
        if (snapshot is null)
        {
            return null;
        }

        return metric switch
        {
            nameof(DiskSnapshot.TemperatureCelsius) => snapshot.TemperatureCelsius,
            nameof(DiskSnapshot.PowerOnHours) => snapshot.PowerOnHours,
            nameof(DiskSnapshot.TotalBytesWritten) => snapshot.TotalBytesWritten,
            nameof(DiskSnapshot.TotalBytesRead) => snapshot.TotalBytesRead,
            nameof(DiskSnapshot.WearPercentage) => snapshot.WearPercentage,
            nameof(DiskSnapshot.UnsafeShutdowns) => snapshot.UnsafeShutdowns,
            nameof(DiskSnapshot.MediaErrors) => snapshot.MediaErrors,
            nameof(DiskSnapshot.ReallocatedSectors) => snapshot.ReallocatedSectors,
            nameof(DiskSnapshot.CurrentPendingSectors) => snapshot.CurrentPendingSectors,
            nameof(DiskSnapshot.UncorrectableErrors) => snapshot.UncorrectableErrors,
            nameof(DiskSnapshot.CrcErrors) => snapshot.CrcErrors,
            "FreeSpacePercent" => freeSpacePercent,
            _ => null
        };
    }

    public static bool Compare(decimal? current, string op, decimal? expected, decimal? previous = null)
    {
        if (current is null)
        {
            return false;
        }

        var right = expected ?? previous;
        if (right is null && op is not "exists" and not "missing")
        {
            return false;
        }

        return op switch
        {
            ">" => current > right,
            ">=" => current >= right,
            "<" => current < right,
            "<=" => current <= right,
            "==" => current == right,
            "!=" => current != right,
            "increased" => previous is not null && current > previous,
            "decreased" => previous is not null && current < previous,
            "exists" => current is not null,
            "missing" => current is null,
            _ => false
        };
    }
}
