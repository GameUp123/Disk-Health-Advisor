using DiskHealthAdvisor.Helpers;

namespace DiskHealthAdvisor.Models;

public sealed class ProcessDiskActivity
{
    public string ProcessName { get; set; } = "";
    public int ProcessId { get; set; }
    public ulong? WrittenBytesPerSecond { get; set; }
    public ulong? ReadBytesPerSecond { get; set; }
    public string Comment { get; set; } = "";

    public string WrittenRateText => $"{FormatHelper.Bytes(WrittenBytesPerSecond)}/с";
    public string ReadRateText => $"{FormatHelper.Bytes(ReadBytesPerSecond)}/с";
    public string ProjectedDailyWriteText => WrittenBytesPerSecond is null
        ? "нет оценки"
        : $"{WrittenBytesPerSecond.Value / 1_000_000_000m * 86400m:0.#} ГБ/день";
}
