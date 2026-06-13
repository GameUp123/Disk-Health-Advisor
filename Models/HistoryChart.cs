using System.Collections.ObjectModel;

namespace DiskHealthAdvisor.Models;

public sealed class HistoryChart
{
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "Нет данных";
    public ObservableCollection<HistoryChartPoint> Points { get; set; } = [];
}

public sealed class HistoryChartPoint
{
    public double Height { get; set; }
    public string Label { get; set; } = "";
}
