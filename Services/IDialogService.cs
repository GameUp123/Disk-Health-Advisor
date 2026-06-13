namespace DiskHealthAdvisor.Services;

public interface IDialogService
{
    string? PickSmartCtlPath();
    string? PickMarkdownReportPath();
    string? PickInvestigationReportPath();
    void ShowMessage(string message, string title);
}
