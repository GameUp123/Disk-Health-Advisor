namespace DiskHealthAdvisor.Models;

public sealed class SsdResourceSummary
{
    public bool HasTbwData { get; set; }
    public decimal? DeclaredTbw { get; set; }
    public ulong? TotalBytesWritten { get; set; }
    public decimal? UsedPercent { get; set; }
    public decimal? AverageWrittenGbPerDay { get; set; }
    public DateTimeOffset? EstimatedTbwDate { get; set; }
    public string DeclaredTbwText { get; set; } = "Нет данных";
    public string TotalBytesWrittenText { get; set; } = "Нет данных";
    public string UsedPercentText { get; set; } = "Нет данных";
    public string AverageWrittenText { get; set; } = "Нет данных";
    public string EstimatedTbwDateText { get; set; } = "Нет данных";
    public string Message { get; set; } = "Для этой модели нет данных о заявленном TBW. Можно добавить его вручную.";
}
