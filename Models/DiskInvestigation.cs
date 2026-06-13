using System.Collections.ObjectModel;

namespace DiskHealthAdvisor.Models;

public sealed class DiskInvestigation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DiskId { get; set; } = "";
    public string DiskModel { get; set; } = "";
    public string DiskSummary { get; set; } = "";
    public string RuleId { get; set; } = "";
    public string Type { get; set; } = "";
    public InvestigationStatus Status { get; set; } = InvestigationStatus.New;
    public DiagnosticRiskLevel RiskLevel { get; set; } = DiagnosticRiskLevel.NotEnoughData;
    public InvestigationConfidence Confidence { get; set; } = InvestigationConfidence.Low;
    public string ConfidenceReason { get; set; } = "Данных пока недостаточно для уверенного вывода.";
    public string SimpleTitle { get; set; } = "";
    public string TriggerMetricText { get; set; } = "";
    public string PrimaryActionText { get; set; } = "";
    public string DetectedProblem { get; set; } = "";
    public string UserExplanation { get; set; } = "";
    public string TechnicalExplanation { get; set; } = "";
    public ObservableCollection<string> PossibleCauses { get; set; } = [];
    public ObservableCollection<string> SuggestedChecks { get; set; } = [];
    public DiskSnapshot? BeforeSnapshot { get; set; }
    public DiskSnapshot? AfterSnapshot { get; set; }
    public ObservableCollection<InvestigationUserAction> UserActions { get; set; } = [];
    public string Conclusion { get; set; } = "";
    public ObservableCollection<string> NextActions { get; set; } = [];
    public ObservableCollection<string> TechnicalDetails { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? FinishedAt { get; set; }

    public string StatusText => Status switch
    {
        InvestigationStatus.New => "Новое",
        InvestigationStatus.WaitingForUserAction => "Ожидает действия",
        InvestigationStatus.WaitingForRecheck => "Ожидает повторной проверки",
        InvestigationStatus.Improved => "Стало лучше",
        InvestigationStatus.NotChanged => "Без изменений",
        InvestigationStatus.Worse => "Стало хуже",
        InvestigationStatus.PhysicalFailureSuspected => "Возможна физическая проблема",
        InvestigationStatus.NeedMoreData => "Недостаточно данных",
        InvestigationStatus.ClosedByUser => "Закрыто",
        _ => "Неизвестно"
    };

    public string RiskText => RiskLevel switch
    {
        DiagnosticRiskLevel.Normal => "Норма",
        DiagnosticRiskLevel.Observation => "Нужно наблюдение",
        DiagnosticRiskLevel.Attention => "Внимание",
        DiagnosticRiskLevel.HighRisk => "Высокий риск",
        DiagnosticRiskLevel.BackupNow => "Срочно сохранить данные",
        _ => "Недостаточно данных"
    };

    public string ConfidenceText => Confidence switch
    {
        InvestigationConfidence.High => "высокая",
        InvestigationConfidence.Medium => "средняя",
        _ => "низкая"
    };
}
