namespace DiskHealthAdvisor.Models;

public sealed class DiagnosticSymptomCondition
{
    public string Metric { get; set; } = "";
    public string Operator { get; set; } = ">";
    public decimal? Value { get; set; }
    public bool CompareWithPreviousSnapshot { get; set; }
}
