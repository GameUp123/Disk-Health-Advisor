using System.Collections.ObjectModel;
using System.Text.Json;
using DiskHealthAdvisor.Models;

namespace DiskHealthAdvisor.Services.DiskProviders;

public sealed class WindowsStorageProvider : IDiskInfoProvider
{
    private readonly PowerShellJsonRunner _runner;
    private readonly AppLogger _logger;

    public WindowsStorageProvider(PowerShellJsonRunner runner, AppLogger logger)
    {
        _runner = runner;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DiskInfo>> GetDisksAsync()
    {
        var output = await _runner.RunAsync(Script);
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(output);
            var result = new List<DiskInfo>();
            foreach (var item in EnumerateArray(document.RootElement))
            {
                result.Add(ParseDisk(item));
            }

            return result;
        }
        catch (Exception ex)
        {
            await _logger.LogAsync("Не удалось разобрать данные WindowsStorageProvider.", ex);
            return [];
        }
    }

    private static DiskInfo ParseDisk(JsonElement item)
    {
        var rawBusType = GetString(item, "BusType");
        var busType = NormalizeBusType(rawBusType);
        var media = GetString(item, "MediaType");
        var physicalMedia = GetString(item, "PhysicalMediaType");
        var model = GetString(item, "Model");
        var windowsTemperature = GetInt(item, "TemperatureCelsius");
        var ataTemperature = GetInt(item, "AtaTemperatureCelsius");

        var disk = new DiskInfo
        {
            Id = GetString(item, "Id") ?? Guid.NewGuid().ToString("N"),
            Model = model,
            Serial = GetString(item, "Serial"),
            Firmware = GetString(item, "Firmware"),
            BusType = busType ?? rawBusType,
            MediaType = DetectMediaKind(busType ?? rawBusType, media, physicalMedia, GetBool(item, "IsUsb"), model),
            SizeBytes = GetUlong(item, "SizeBytes"),
            TemperatureCelsius = windowsTemperature,
            TemperatureSource = windowsTemperature is null ? "" : "Windows Storage Reliability Counter",
            PowerOnHours = GetUlong(item, "PowerOnHours"),
            PowerCycleCount = GetUlong(item, "PowerCycleCount"),
            TotalBytesWritten = GetUlong(item, "TotalBytesWritten"),
            TotalBytesRead = GetUlong(item, "TotalBytesRead"),
            WearPercentage = GetInt(item, "WearPercentage"),
            MediaErrors = Sum(GetUlong(item, "ReadErrorsTotal"), GetUlong(item, "WriteErrorsTotal")),
            ReallocatedSectors = GetUlong(item, "ReallocatedSectors"),
            CurrentPendingSectors = GetUlong(item, "CurrentPendingSectors"),
            UncorrectableErrors = GetUlong(item, "UncorrectableErrors"),
            CrcErrors = GetUlong(item, "CrcErrors"),
            SmartPassed = GetBool(item, "SmartPassed")
        };

        if (disk.TemperatureCelsius is null && ataTemperature is not null)
        {
            disk.TemperatureCelsius = ataTemperature;
            disk.TemperatureSource = "ATA SMART attribute 194/190 через Windows WMI";
        }
        disk.PowerOnHours ??= GetUlong(item, "AtaPowerOnHours");
        disk.PowerCycleCount ??= GetUlong(item, "AtaPowerCycleCount");
        disk.SmartPassed ??= GetBool(item, "AtaSmartPassed");

        if (TryGet(item, "Volumes", out var volumes))
        {
            disk.LogicalVolumes = new ObservableCollection<LogicalVolumeInfo>(
                EnumerateArray(volumes).Select(v => new LogicalVolumeInfo
                {
                    Name = GetString(v, "Name"),
                    FileSystem = GetString(v, "FileSystem"),
                    SizeBytes = GetUlong(v, "SizeBytes"),
                    FreeBytes = GetUlong(v, "FreeBytes")
                })); 
        }

        if (TryGet(item, "SmartAttributes", out var smartAttributes))
        {
            foreach (var attribute in EnumerateArray(smartAttributes))
            {
                disk.RawAttributes.Add(new SmartAttributeInfo
                {
                    Id = GetString(attribute, "Id") ?? "",
                    Name = GetString(attribute, "Name") ?? "",
                    RawValue = GetString(attribute, "RawValue"),
                    NormalizedValue = GetInt(attribute, "NormalizedValue"),
                    Threshold = GetInt(attribute, "Threshold"),
                    Comment = GetString(attribute, "Comment")
                });
            }
        }

        if (disk.TemperatureCelsius is null)
        {
            disk.DataSourceWarnings.Add("Температура недоступна через Windows Storage API.");
        }

        if (disk.TotalBytesWritten is null)
        {
            disk.DataSourceWarnings.Add("Windows не отдал общий объём записи для этого диска.");
        }

        var reliabilityWarning = GetString(item, "ReliabilityWarning");
        if (!string.IsNullOrWhiteSpace(reliabilityWarning))
        {
            disk.DataSourceWarnings.Add("Windows Storage Reliability Counter недоступен: " + reliabilityWarning);
        }

        var smartWarning = GetString(item, "SmartWmiWarning");
        if (!string.IsNullOrWhiteSpace(smartWarning))
        {
            disk.DataSourceWarnings.Add("ATA SMART WMI недоступен: " + smartWarning);
        }

        if (disk.TemperatureCelsius is null &&
            disk.PowerOnHours is null &&
            disk.TotalBytesWritten is null &&
            disk.WearPercentage is null)
        {
            disk.DataSourceWarnings.Add("Глубокие SMART/NVMe-поля не получены через Windows. Обычно это исправляется запуском от администратора или подключением smartctl.exe.");
        }

        disk.DataSourceWarnings.Add("Часть SMART/NVMe-данных может быть недоступна без прав администратора или smartctl.");
        return disk;
    }

    private static DiskMediaKind DetectMediaKind(string? busType, string? media, string? physicalMedia, bool? isUsb, string? model)
    {
        if (isUsb == true || IsBus(busType, "USB", "7"))
        {
            return DiskMediaKind.USB;
        }

        if (IsBus(busType, "NVMe", "17"))
        {
            return DiskMediaKind.NvmeSSD;
        }

        var physical = NormalizeToken(physicalMedia);
        if (physical is "4" or "SSD" or "SOLIDSTATE" or "SOLID STATE")
        {
            return IsBus(busType, "SATA", "11") ? DiskMediaKind.SataSSD : DiskMediaKind.SSD;
        }

        if (physical is "3" or "HDD")
        {
            return DiskMediaKind.HDD;
        }

        var combined = $"{media} {physicalMedia}".ToUpperInvariant();
        if (combined.Contains("SSD") || combined.Contains("SOLID"))
        {
            return IsBus(busType, "SATA", "11")
                ? DiskMediaKind.SataSSD
                : DiskMediaKind.SSD;
        }

        var modelText = (model ?? "").ToUpperInvariant();
        if (modelText.Contains("NVME") || modelText.Contains("M.2"))
        {
            return DiskMediaKind.NvmeSSD;
        }

        if (modelText.Contains("SSD"))
        {
            return IsBus(busType, "SATA", "11") ? DiskMediaKind.SataSSD : DiskMediaKind.SSD;
        }

        if (combined.Contains("HDD"))
        {
            return DiskMediaKind.HDD;
        }

        return DiskMediaKind.Unknown;
    }

    private static string? NormalizeBusType(string? busType)
    {
        return NormalizeToken(busType) switch
        {
            "0" => "Unknown",
            "1" => "SCSI",
            "2" => "ATAPI",
            "3" => "ATA",
            "4" => "IEEE 1394",
            "5" => "SSA",
            "6" => "Fibre Channel",
            "7" => "USB",
            "8" => "RAID",
            "9" => "iSCSI",
            "10" => "SAS",
            "11" => "SATA",
            "12" => "SD",
            "13" => "MMC",
            "14" => "Virtual",
            "15" => "File-backed virtual",
            "16" => "Storage Spaces",
            "17" => "NVMe",
            "18" => "SCM",
            "19" => "UFS",
            "" => null,
            _ => busType
        };
    }

    private static bool IsBus(string? busType, string name, string numericValue)
    {
        var normalized = NormalizeToken(busType);
        return normalized == NormalizeToken(name) || normalized == numericValue;
    }

    private static string NormalizeToken(string? value)
    {
        return (value ?? "").Replace("_", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal).Trim().ToUpperInvariant();
    }

    private static IEnumerable<JsonElement> EnumerateArray(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Array => element.EnumerateArray(),
            JsonValueKind.Undefined or JsonValueKind.Null => [],
            _ => [element]
        };
    }

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
        var value = GetDecimal(item, name);
        return value is null ? null : (int)value.Value;
    }

    private static ulong? GetUlong(JsonElement item, string name)
    {
        var value = GetDecimal(item, name);
        return value is null || value < 0 ? null : (ulong)value.Value;
    }

    private static decimal? GetDecimal(JsonElement item, string name)
    {
        if (!TryGet(item, name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }

        return decimal.TryParse(value.ToString(), out var parsed) ? parsed : null;
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
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static ulong? Sum(ulong? left, ulong? right)
    {
        if (left is null && right is null)
        {
            return null;
        }

        return (left ?? 0) + (right ?? 0);
    }

    private const string Script = """
$ErrorActionPreference = 'SilentlyContinue'
$drives = @(Get-CimInstance -ClassName Win32_DiskDrive)
$physicalDisks = @(Get-CimInstance -Namespace root/Microsoft/Windows/Storage -ClassName MSFT_PhysicalDisk)
$reliabilityWarning = $null
try {
    $reliability = @(Get-CimInstance -Namespace root/Microsoft/Windows/Storage -ClassName MSFT_StorageReliabilityCounter -ErrorAction Stop)
} catch {
    $reliability = @()
    $reliabilityWarning = $_.Exception.Message
}
$smartWmiWarning = $null
try {
    $smartDataAll = @(Get-CimInstance -Namespace root/wmi -ClassName MSStorageDriver_FailurePredictData -ErrorAction Stop)
    $smartThresholdsAll = @(Get-CimInstance -Namespace root/wmi -ClassName MSStorageDriver_FailurePredictThresholds -ErrorAction Stop)
    $smartStatusAll = @(Get-CimInstance -Namespace root/wmi -ClassName MSStorageDriver_FailurePredictStatus -ErrorAction Stop)
} catch {
    $smartDataAll = @()
    $smartThresholdsAll = @()
    $smartStatusAll = @()
    $smartWmiWarning = $_.Exception.Message
}

function Normalize-Key($value) {
    if ($null -eq $value) { return '' }
    return ([regex]::Replace("$value", '[^A-Za-z0-9]', '')).ToUpperInvariant()
}

function Get-SmartName($id) {
    switch ($id) {
        5 { 'Reallocated Sectors Count' }
        9 { 'Power-On Hours' }
        12 { 'Power Cycle Count' }
        190 { 'Airflow Temperature' }
        194 { 'Temperature Celsius' }
        197 { 'Current Pending Sector Count' }
        198 { 'Uncorrectable Sector Count' }
        199 { 'UDMA CRC Error Count' }
        default { "SMART Attribute $id" }
    }
}

function Get-SmartComment($id) {
    switch ($id) {
        5 { 'Диск уже заменял плохие сектора резервными.' }
        9 { 'Общее время работы диска.' }
        12 { 'Количество циклов включения питания.' }
        190 { 'Температура по ATA SMART.' }
        194 { 'Температура по ATA SMART.' }
        197 { 'Нестабильные сектора. Рост этого числа является плохим признаком.' }
        198 { 'Неисправимые ошибки чтения/записи.' }
        199 { 'Рост может указывать на SATA-кабель, порт или питание.' }
        default { 'Сырой ATA SMART-показатель.' }
    }
}

function Read-RawValue($bytes, $offset) {
    $value = [UInt64]0
    for ($i = 0; $i -lt 6; $i++) {
        $value = $value -bor ([UInt64]$bytes[$offset + 5 + $i] -shl (8 * $i))
    }
    return $value
}

function Parse-SmartAttributes($dataBytes, $thresholdBytes) {
    $rows = @()
    if ($null -eq $dataBytes -or $dataBytes.Count -lt 362) { return @($rows) }

    for ($i = 0; $i -lt 30; $i++) {
        $offset = 2 + ($i * 12)
        $id = [int]$dataBytes[$offset]
        if ($id -eq 0) { continue }

        $threshold = $null
        if ($thresholdBytes -and $thresholdBytes.Count -gt ($offset + 3) -and [int]$thresholdBytes[$offset] -eq $id) {
            $threshold = [int]$thresholdBytes[$offset + 3]
        }

        $raw = Read-RawValue $dataBytes $offset
        $rows += [pscustomobject]@{
            Id = "$id"
            Name = Get-SmartName $id
            RawValue = "$raw"
            NormalizedValue = [int]$dataBytes[$offset + 3]
            Threshold = $threshold
            Comment = Get-SmartComment $id
        }
    }

    return @($rows)
}

function Find-SmartItem($items, $drive) {
    $pnp = Normalize-Key $drive.PNPDeviceID
    $serial = Normalize-Key $drive.SerialNumber
    foreach ($item in $items) {
        $instance = Normalize-Key $item.InstanceName
        if (($pnp.Length -gt 0 -and $instance.Contains($pnp)) -or ($serial.Length -gt 0 -and $instance.Contains($serial))) {
            return $item
        }
    }

    return $null
}

function Get-AttrRaw($attrs, $id) {
    $attr = $attrs | Where-Object { $_.Id -eq "$id" } | Select-Object -First 1
    if ($attr -and $attr.RawValue -match '^\d+$') { return [UInt64]$attr.RawValue }
    return $null
}

$result = foreach ($drive in $drives) {
    $phys = $physicalDisks | Where-Object { "$($_.DeviceId)" -eq "$($drive.Index)" } | Select-Object -First 1
    $rel = $null
    if ($phys) {
        $rel = $reliability | Where-Object { "$($_.DeviceId)" -eq "$($phys.DeviceId)" -or $_.UniqueId -eq $phys.UniqueId } | Select-Object -First 1
    }
    $smartData = Find-SmartItem $smartDataAll $drive
    $smartThresholds = Find-SmartItem $smartThresholdsAll $drive
    $smartStatus = Find-SmartItem $smartStatusAll $drive
    $smartAttributes = @()
    if ($smartData) {
        $smartAttributes = @(Parse-SmartAttributes $smartData.VendorSpecific $smartThresholds.VendorSpecific)
    }

    $volumes = @()
    $parts = @(Get-CimAssociatedInstance -InputObject $drive -Association Win32_DiskDriveToDiskPartition)
    foreach ($part in $parts) {
        $logical = @(Get-CimAssociatedInstance -InputObject $part -Association Win32_LogicalDiskToPartition)
        foreach ($volume in $logical) {
            $volumes += [pscustomobject]@{
                Name = $volume.DeviceID
                FileSystem = $volume.FileSystem
                SizeBytes = $volume.Size
                FreeBytes = $volume.FreeSpace
            }
        }
    }

    [pscustomobject]@{
        Id = $drive.DeviceID
        Index = $drive.Index
        Model = $drive.Model
        Serial = $drive.SerialNumber
        Firmware = $drive.FirmwareRevision
        BusType = if ($phys.BusType) { "$($phys.BusType)" } else { $drive.InterfaceType }
        MediaType = $drive.MediaType
        PhysicalMediaType = if ($phys.MediaType) { "$($phys.MediaType)" } else { $null }
        SizeBytes = $drive.Size
        IsUsb = ($drive.InterfaceType -eq 'USB')
        TemperatureCelsius = $rel.Temperature
        PowerOnHours = $rel.PowerOnHours
        PowerCycleCount = $rel.StartStopCycleCount
        TotalBytesWritten = $rel.WriteBytesTotal
        TotalBytesRead = $rel.ReadBytesTotal
        WearPercentage = $rel.Wear
        ReadErrorsTotal = $rel.ReadErrorsTotal
        WriteErrorsTotal = $rel.WriteErrorsTotal
        AtaTemperatureCelsius = $(if ((Get-AttrRaw $smartAttributes 194) -ne $null) { (Get-AttrRaw $smartAttributes 194) -band 255 } elseif ((Get-AttrRaw $smartAttributes 190) -ne $null) { (Get-AttrRaw $smartAttributes 190) -band 255 } else { $null })
        AtaPowerOnHours = Get-AttrRaw $smartAttributes 9
        AtaPowerCycleCount = Get-AttrRaw $smartAttributes 12
        ReallocatedSectors = Get-AttrRaw $smartAttributes 5
        CurrentPendingSectors = Get-AttrRaw $smartAttributes 197
        UncorrectableErrors = Get-AttrRaw $smartAttributes 198
        CrcErrors = Get-AttrRaw $smartAttributes 199
        AtaSmartPassed = $(if ($smartStatus) { -not [bool]$smartStatus.PredictFailure } else { $null })
        SmartAttributes = @($smartAttributes)
        ReliabilityWarning = $reliabilityWarning
        SmartWmiWarning = $smartWmiWarning
        SmartPassed = $drive.Status -eq 'OK'
        Volumes = @($volumes)
    }
}

@($result) | ConvertTo-Json -Depth 8 -Compress
""";
}
