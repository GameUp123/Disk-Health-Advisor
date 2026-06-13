namespace DiskHealthAdvisor.Models;

public sealed class MetricDisplay
{
    public MetricDisplay()
    {
    }

    public MetricDisplay(string name, string value)
    {
        Name = name;
        Value = value;
    }

    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
}
