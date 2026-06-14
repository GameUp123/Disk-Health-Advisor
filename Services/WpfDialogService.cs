using System.Windows;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace DiskHealthAdvisor.Services;

public sealed class WpfDialogService : IDialogService
{
    public string? PickSmartCtlPath()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Выберите smartctl.exe",
            Filter = "smartctl.exe|smartctl.exe|EXE files (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickLocalUpdateSourceDirectory(string? initialDirectory)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Выберите папку свежей сборки Disk Health Advisor",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            SelectedPath = Directory.Exists(initialDirectory) ? initialDirectory : ""
        };

        return dialog.ShowDialog() == Forms.DialogResult.OK ? dialog.SelectedPath : null;
    }

    public string? PickMarkdownReportPath()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Сохранить отчёт",
            Filter = "Markdown (*.md)|*.md|Text (*.txt)|*.txt",
            FileName = $"disk-health-report-{DateTime.Now:yyyyMMdd-HHmm}.md",
            AddExtension = true
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickInvestigationReportPath()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Сохранить расследование",
            Filter = "Markdown (*.md)|*.md|Text (*.txt)|*.txt",
            FileName = $"disk-investigation-{DateTime.Now:yyyyMMdd-HHmm}.md",
            AddExtension = true
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public void ShowMessage(string message, string title)
    {
        System.Windows.MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
