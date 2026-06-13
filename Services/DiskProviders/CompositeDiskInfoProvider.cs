using DiskHealthAdvisor.Models;

namespace DiskHealthAdvisor.Services.DiskProviders;

public sealed class CompositeDiskInfoProvider : IDiskInfoProvider
{
    private readonly IDiskInfoProvider _windowsProvider;
    private readonly IDiskInfoProvider _smartCtlProvider;
    private readonly AppLogger _logger;

    public CompositeDiskInfoProvider(IDiskInfoProvider windowsProvider, IDiskInfoProvider smartCtlProvider, AppLogger logger)
    {
        _windowsProvider = windowsProvider;
        _smartCtlProvider = smartCtlProvider;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DiskInfo>> GetDisksAsync()
    {
        var windowsDisks = (await _windowsProvider.GetDisksAsync()).ToList();
        IReadOnlyList<DiskInfo> smartDisks = [];

        try
        {
            smartDisks = await _smartCtlProvider.GetDisksAsync();
        }
        catch (Exception ex)
        {
            await _logger.LogAsync("SmartCtlProvider не смог получить данные.", ex);
        }

        foreach (var smartDisk in smartDisks)
        {
            var target = windowsDisks.FirstOrDefault(d => SameDisk(d, smartDisk));
            if (target is null)
            {
                windowsDisks.Add(smartDisk);
            }
            else
            {
                Merge(target, smartDisk);
            }
        }

        return windowsDisks;
    }

    private static bool SameDisk(DiskInfo left, DiskInfo right)
    {
        if (!string.IsNullOrWhiteSpace(left.Serial) &&
            !string.IsNullOrWhiteSpace(right.Serial) &&
            string.Equals(left.Serial.Trim(), right.Serial.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(left.Model) &&
               !string.IsNullOrWhiteSpace(right.Model) &&
               left.Model.Contains(right.Model, StringComparison.OrdinalIgnoreCase);
    }

    private static void Merge(DiskInfo target, DiskInfo source)
    {
        target.Model ??= source.Model;
        target.Serial ??= source.Serial;
        target.Firmware ??= source.Firmware;
        target.SizeBytes ??= source.SizeBytes;
        if (source.TemperatureCelsius is not null)
        {
            if (target.TemperatureCelsius is not null &&
                Math.Abs(target.TemperatureCelsius.Value - source.TemperatureCelsius.Value) >= 2)
            {
                target.DataSourceWarnings.Add($"Температура Windows ({target.TemperatureCelsius}°C) отличается от smartctl ({source.TemperatureCelsius}°C). Используется smartctl.");
            }

            target.TemperatureCelsius = source.TemperatureCelsius;
            target.TemperatureSource = string.IsNullOrWhiteSpace(source.TemperatureSource) ? "smartctl" : source.TemperatureSource;
        }
        else if (string.IsNullOrWhiteSpace(target.TemperatureSource) && !string.IsNullOrWhiteSpace(source.TemperatureSource))
        {
            target.TemperatureSource = source.TemperatureSource;
        }
        target.PowerOnHours ??= source.PowerOnHours;
        target.PowerCycleCount ??= source.PowerCycleCount;
        target.TotalBytesWritten ??= source.TotalBytesWritten;
        target.TotalBytesRead ??= source.TotalBytesRead;
        target.WearPercentage ??= source.WearPercentage;
        target.UnsafeShutdowns ??= source.UnsafeShutdowns;
        target.MediaErrors ??= source.MediaErrors;
        target.ReallocatedSectors ??= source.ReallocatedSectors;
        target.CurrentPendingSectors ??= source.CurrentPendingSectors;
        target.UncorrectableErrors ??= source.UncorrectableErrors;
        target.CrcErrors ??= source.CrcErrors;
        target.SmartPassed ??= source.SmartPassed;

        if (target.MediaType == DiskMediaKind.Unknown && source.MediaType != DiskMediaKind.Unknown)
        {
            target.MediaType = source.MediaType;
        }

        foreach (var attribute in source.RawAttributes)
        {
            target.RawAttributes.Add(attribute);
        }

        foreach (var warning in source.DataSourceWarnings)
        {
            target.DataSourceWarnings.Add(warning);
        }

        if (!string.IsNullOrWhiteSpace(target.TemperatureSource))
        {
            var note = "Источник температуры: " + target.TemperatureSource + ".";
            if (!target.DataSourceWarnings.Contains(note))
            {
                target.DataSourceWarnings.Add(note);
            }
        }
    }
}
