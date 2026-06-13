using System.Diagnostics;
using System.Text.Json;
using DiskHealthAdvisor.Models;

namespace DiskHealthAdvisor.Services.DiskProviders;

public sealed class SmartCtlProvider : IDiskInfoProvider
{
    private readonly AppLogger _logger;
    private readonly Func<Task<string?>> _pathProvider;

    public SmartCtlProvider(AppLogger logger, Func<Task<string?>> pathProvider)
    {
        _logger = logger;
        _pathProvider = pathProvider;
    }

    public async Task<IReadOnlyList<DiskInfo>> GetDisksAsync()
    {
        var smartCtl = await ResolveSmartCtlPathAsync();
        if (smartCtl is null)
        {
            return [];
        }

        var scanJson = await RunSmartCtlAsync(smartCtl, "--scan-open -j");
        if (string.IsNullOrWhiteSpace(scanJson))
        {
            return [];
        }

        try
        {
            using var scanDocument = JsonDocument.Parse(scanJson);
            if (!scanDocument.RootElement.TryGetProperty("devices", out var devices) ||
                devices.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var disks = new List<DiskInfo>();
            foreach (var device in devices.EnumerateArray())
            {
                var name = GetString(device, "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var detailJson = await RunSmartCtlAsync(smartCtl, $"-a -j {Quote(name)}");
                var parsed = ParseDetail(detailJson, name);
                if (parsed is not null)
                {
                    disks.Add(parsed);
                }
            }

            return disks;
        }
        catch (Exception ex)
        {
            await _logger.LogAsync("Не удалось разобрать JSON smartctl.", ex);
            return [];
        }
    }

    private async Task<string?> ResolveSmartCtlPathAsync()
    {
        var configured = await _pathProvider();
        return SmartCtlLocator.Find(configured);
    }

    private async Task<string?> RunSmartCtlAsync(string smartCtl, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = smartCtl,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var errors = await errorTask;
            if (!string.IsNullOrWhiteSpace(errors))
            {
                await _logger.LogAsync("smartctl сообщил: " + errors.Trim());
            }

            return await outputTask;
        }
        catch (Exception ex)
        {
            await _logger.LogAsync("smartctl недоступен или вернул ошибку.", ex);
            return null;
        }
    }

    private static DiskInfo? ParseDetail(string? json, string deviceName)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var disk = new DiskInfo
        {
            Id = deviceName,
            Model = GetString(root, "model_name"),
            Serial = GetString(root, "serial_number"),
            Firmware = GetString(root, "firmware_version"),
            SizeBytes = TryGet(root, "user_capacity", out var capacity) ? GetUlong(capacity, "bytes") : null,
            SmartPassed = TryGet(root, "smart_status", out var smart) ? GetBool(smart, "passed") : null
        };
        var logicalBlockSize = GetUlong(root, "logical_block_size") ?? 512UL;

        if (TryGet(root, "power_on_time", out var powerOn))
        {
            disk.PowerOnHours = GetUlong(powerOn, "hours");
        }

        if (TryGet(root, "temperature", out var temperature))
        {
            disk.TemperatureCelsius = GetInt(temperature, "current");
            disk.TemperatureSource = disk.TemperatureCelsius is null ? "" : "smartctl temperature.current";
        }

        disk.PowerCycleCount = GetUlong(root, "power_cycle_count");

        if (TryGet(root, "nvme_smart_health_information_log", out var nvme))
        {
            disk.MediaType = DiskMediaKind.NvmeSSD;
            if (disk.TemperatureCelsius is null)
            {
                disk.TemperatureCelsius = GetInt(nvme, "temperature");
                disk.TemperatureSource = disk.TemperatureCelsius is null ? "" : "smartctl NVMe health log";
            }
            disk.PowerOnHours ??= GetUlong(nvme, "power_on_hours");
            disk.PowerCycleCount ??= GetUlong(nvme, "power_cycles");
            disk.WearPercentage = GetInt(nvme, "percentage_used");
            disk.UnsafeShutdowns = GetUlong(nvme, "unsafe_shutdowns");
            disk.MediaErrors = GetUlong(nvme, "media_errors");
            disk.TotalBytesRead = DataUnitsToBytes(GetUlong(nvme, "data_units_read"));
            disk.TotalBytesWritten = DataUnitsToBytes(GetUlong(nvme, "data_units_written"));
            AddRaw(disk, "NVMe", "Temperature", disk.TemperatureCelsius?.ToString(), "Температура NVMe health log.");
            AddRaw(disk, "NVMe", "Percentage Used", disk.WearPercentage?.ToString(), "Процент использованного ресурса по данным NVMe.");
            AddRaw(disk, "NVMe", "Available Spare", GetUlong(nvme, "available_spare")?.ToString(), "Оставшийся резерв по данным NVMe.");
            AddRaw(disk, "NVMe", "Data Units Read", GetUlong(nvme, "data_units_read")?.ToString(), "NVMe data unit = 512000 байт.");
            AddRaw(disk, "NVMe", "Data Units Written", GetUlong(nvme, "data_units_written")?.ToString(), "NVMe data unit = 512000 байт.");
            AddRaw(disk, "NVMe", "Unsafe Shutdowns", disk.UnsafeShutdowns?.ToString(), "Некорректные выключения питания по данным NVMe.");
            AddRaw(disk, "NVMe", "Media Errors", disk.MediaErrors?.ToString(), "Ошибки носителя, сообщенные контроллером.");
            AddRaw(disk, "NVMe", "Error Log Entries", GetUlong(nvme, "num_err_log_entries")?.ToString(), "Записи журнала ошибок NVMe.");
        }

        if (TryGet(root, "ata_smart_attributes", out var ata) &&
            TryGet(ata, "table", out var table) &&
            table.ValueKind == JsonValueKind.Array)
        {
            disk.MediaType = disk.MediaType == DiskMediaKind.Unknown ? DiskMediaKind.SataSSD : disk.MediaType;
            foreach (var attribute in table.EnumerateArray())
            {
                var id = GetString(attribute, "id") ?? "";
                var name = GetString(attribute, "name") ?? "";
                var raw = TryGet(attribute, "raw", out var rawElement) ? GetString(rawElement, "string") : null;
                var item = new SmartAttributeInfo
                {
                    Id = id,
                    Name = name,
                    RawValue = raw,
                    NormalizedValue = GetInt(attribute, "value"),
                    Threshold = GetInt(attribute, "thresh"),
                    Comment = ExplainSmartAttribute(name)
                };
                disk.RawAttributes.Add(item);
                ApplyKnownAtaAttribute(disk, name, raw, logicalBlockSize);
            }
        }

        disk.DataSourceWarnings.Add("Данные получены через smartctl. Это read-only запрос.");
        return disk;
    }

    private static void ApplyKnownAtaAttribute(DiskInfo disk, string name, string? raw, ulong logicalBlockSize)
    {
        if (!TryParseLeadingUlong(raw, out var value))
        {
            return;
        }

        var normalized = name.Replace("_", " ", StringComparison.Ordinal).ToUpperInvariant();
        if (normalized.Contains("REALLOCATED"))
        {
            disk.ReallocatedSectors = value;
        }
        else if (normalized.Contains("CURRENT PENDING"))
        {
            disk.CurrentPendingSectors = value;
        }
        else if (normalized.Contains("UNCORRECTABLE"))
        {
            disk.UncorrectableErrors = value;
        }
        else if (normalized.Contains("UDMA CRC") || normalized.Contains("CRC ERROR"))
        {
            disk.CrcErrors = value;
        }
        else if (normalized.Contains("POWER ON HOURS"))
        {
            disk.PowerOnHours = value;
        }
        else if (normalized.Contains("POWER CYCLE"))
        {
            disk.PowerCycleCount = value;
        }
        else if (normalized.Contains("POWER OFF") || normalized.Contains("POWER LOSS") || normalized.Contains("UNEXPECT POWER"))
        {
            disk.UnsafeShutdowns ??= value;
        }
        else if (normalized.Contains("TOTAL LBAS WRITTEN") || normalized.Contains("HOST WRITES") || normalized.Contains("NAND WRITES"))
        {
            disk.TotalBytesWritten ??= SafeMultiply(value, logicalBlockSize);
        }
        else if (normalized.Contains("TOTAL LBAS READ") || normalized.Contains("HOST READS"))
        {
            disk.TotalBytesRead ??= SafeMultiply(value, logicalBlockSize);
        }
        else if (normalized.Contains("LIFETIME LEFT") || normalized.Contains("SSD LIFE LEFT") || normalized.Contains("PERCENT LIFETIME REMAIN"))
        {
            if (value <= 100)
            {
                disk.WearPercentage ??= (int)(100 - value);
            }
        }
        else if (normalized.Contains("WEAR LEVELING") && value <= 100)
        {
            disk.WearPercentage ??= (int)Math.Min(100, value);
        }
        else if (normalized.Contains("SATA PHY ERROR"))
        {
            disk.CrcErrors ??= value;
        }
    }

    private static string ExplainSmartAttribute(string name)
    {
        var normalized = name.Replace("_", " ", StringComparison.Ordinal).ToUpperInvariant();
        if (normalized.Contains("CRC"))
        {
            return "Рост может указывать на кабель, порт или питание, а не обязательно на сам диск.";
        }

        if (normalized.Contains("REALLOCATED") || normalized.Contains("PENDING") || normalized.Contains("UNCORRECTABLE"))
        {
            return "Показатель относится к проблемным секторам и важен для оценки риска.";
        }

        return "Сырой SMART-показатель.";
    }

    private static void AddRaw(DiskInfo disk, string id, string name, string? raw, string comment)
    {
        disk.RawAttributes.Add(new SmartAttributeInfo { Id = id, Name = name, RawValue = raw, Comment = comment });
    }

    private static ulong? DataUnitsToBytes(ulong? dataUnits)
    {
        return dataUnits is null ? null : dataUnits.Value * 512_000UL;
    }

    private static bool TryParseLeadingUlong(string? raw, out ulong value)
    {
        value = 0;
        var token = raw?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return ulong.TryParse(token, out value);
    }

    private static ulong? SafeMultiply(ulong left, ulong right)
    {
        try
        {
            checked
            {
                return left * right;
            }
        }
        catch
        {
            return null;
        }
    }

    private static string Quote(string value) => value.Contains(' ') ? $"\"{value}\"" : value;

    private static bool TryGet(JsonElement item, string name, out JsonElement value)
    {
        if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    private static string? GetString(JsonElement item, string name)
    {
        if (!TryGet(item, name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static int? GetInt(JsonElement item, string name)
    {
        var value = GetUlong(item, name);
        return value is null ? null : (int)value.Value;
    }

    private static ulong? GetUlong(JsonElement item, string name)
    {
        if (!TryGet(item, name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetUInt64(out var number))
        {
            return number;
        }

        return ulong.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static bool? GetBool(JsonElement item, string name)
    {
        if (!TryGet(item, name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }
}
