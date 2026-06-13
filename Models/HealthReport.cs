using System.Collections.ObjectModel;

namespace DiskHealthAdvisor.Models;

public sealed class HealthReport
{
    public HealthLevel Level { get; set; } = HealthLevel.Unknown;
    public int RiskScore { get; set; }
    public string Summary { get; set; } = "Данных недостаточно для точной оценки.";
    public ObservableCollection<string> Reasons { get; set; } = [];
    public ObservableCollection<string> Recommendations { get; set; } = [];
    public ObservableCollection<MetricDisplay> Details { get; set; } = [];
}
