namespace DiskHealthAdvisor.Models;

public sealed class DiagnosticRule
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string SimpleTitle { get; set; } = "";
    public List<string> DiskTypes { get; set; } = [];
    public List<DiagnosticSymptomCondition> Symptoms { get; set; } = [];
    public DiagnosticRiskLevel RiskLevel { get; set; } = DiagnosticRiskLevel.NotEnoughData;
    public string UserExplanation { get; set; } = "";
    public string TechnicalExplanation { get; set; } = "";
    public List<string> PossibleCauses { get; set; } = [];
    public List<string> SuggestedChecks { get; set; } = [];
    public string WhatMeansImprovement { get; set; } = "";
    public string WhatMeansWorse { get; set; } = "";
    public string ConclusionIfImproved { get; set; } = "";
    public string ConclusionIfNotChanged { get; set; } = "";
    public string ConclusionIfWorse { get; set; } = "";
    public List<string> Recommendations { get; set; } = [];
    public List<string> TechnicalDetails { get; set; } = [];
    public bool RequiresBackupFirst { get; set; }
    public bool CanRunAutomaticAction { get; set; }
    public List<string> UserActionExamples { get; set; } = [];
}
