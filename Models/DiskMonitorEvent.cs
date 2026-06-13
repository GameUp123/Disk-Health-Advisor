using DiskHealthAdvisor.Helpers;

namespace DiskHealthAdvisor.Models;

public sealed class DiskMonitorEvent
{
    public DateTimeOffset Timestamp { get; set; }
    public string DiskIdentity { get; set; } = "";
    public string DiskModel { get; set; } = "";
    public string Severity { get; set; } = "Info";
    public string Title { get; set; } = "";
    public string Details { get; set; } = "";
    public string PossibleCause { get; set; } = "";
    public int? TemperatureCelsius { get; set; }
    public HealthLevel HealthLevel { get; set; } = HealthLevel.Unknown;
    public ulong? WrittenBytesPerSecond { get; set; }
    public ulong? ReadBytesPerSecond { get; set; }
    public string ProcessName { get; set; } = "";
    public int? ProcessId { get; set; }
    public decimal? ProjectedDailyWriteGb { get; set; }

    public string TimeText => Timestamp.ToLocalTime().ToString("HH:mm:ss");
    public string DateText => Timestamp.ToLocalTime().ToString("yyyy-MM-dd");
    public string TemperatureText => FormatHelper.Optional(TemperatureCelsius, "°C");
    public string WriteRateText => FormatHelper.Bytes(WrittenBytesPerSecond) + "/с";
    public string ReadRateText => FormatHelper.Bytes(ReadBytesPerSecond) + "/с";
    public string ProcessTitle => string.IsNullOrWhiteSpace(ProcessName) ? Title : $"{ProcessName} ({ProcessId})";
    public string ProjectedDailyWriteText => ProjectedDailyWriteGb is null ? "нет оценки" : $"{ProjectedDailyWriteGb:0.#} ГБ/день";
    public string SeverityBrush => Severity switch
    {
        "Critical" => "#D94B4B",
        "Warning" => "#D17A22",
        "Caution" => "#C9A227",
        _ => "#5E9EFF"
    };
}
