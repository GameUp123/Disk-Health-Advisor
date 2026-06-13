namespace DiskHealthAdvisor.Models;

public sealed class InvestigationComparisonResult
{
    public InvestigationStatus Status { get; set; } = InvestigationStatus.NeedMoreData;
    public InvestigationConfidence Confidence { get; set; } = InvestigationConfidence.Low;
    public string ConfidenceReason { get; set; } = "";
    public string Conclusion { get; set; } = "";
    public List<string> TechnicalDetails { get; set; } = [];
}
