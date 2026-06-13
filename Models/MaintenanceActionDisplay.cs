namespace DiskHealthAdvisor.Models;

public sealed class MaintenanceActionDisplay
{
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    public string Status { get; set; } = "";
    public string Details { get; set; } = "";
    public string Safety { get; set; } = "";
    public string ButtonText { get; set; } = "";
    public string ActionKind { get; set; } = "";
    public string AccentBrush { get; set; } = "#5E9EFF";
    public bool HasAction => !string.IsNullOrWhiteSpace(ActionKind) && !string.IsNullOrWhiteSpace(ButtonText);
}
