namespace DiskHealthAdvisor.Services;

public interface IDialogService
{
    string? PickSmartCtlPath();
    string? PickLocalUpdateSourceDirectory(string? initialDirectory);
    string? PickMarkdownReportPath();
    string? PickInvestigationReportPath();
    void ShowMessage(string message, string title);
}
