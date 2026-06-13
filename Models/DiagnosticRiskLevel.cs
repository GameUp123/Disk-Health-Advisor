namespace DiskHealthAdvisor.Models;

public enum DiagnosticRiskLevel
{
    Normal,
    Observation,
    Attention,
    HighRisk,
    BackupNow,
    NotEnoughData
}
