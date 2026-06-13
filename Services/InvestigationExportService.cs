using System.Text;
using DiskHealthAdvisor.Models;
using DiskHealthAdvisor.Services.HealthAnalysis;

namespace DiskHealthAdvisor.Services;

public sealed class InvestigationExportService
{
    private readonly SimpleTextFormatter _formatter = new();

    public async Task ExportMarkdownAsync(string path, DiskInvestigation investigation, DiskInfo? disk)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Расследование Disk Health Advisor");
        builder.AppendLine();
        builder.AppendLine($"Дата: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine($"Диск: {disk?.Model ?? investigation.DiskId}");
        builder.AppendLine($"Проблема: {investigation.SimpleTitle}");
        builder.AppendLine($"Уровень опасности: {investigation.RiskText}");
        builder.AppendLine($"Статус: {investigation.StatusText}");
        builder.AppendLine($"Уверенность: {investigation.ConfidenceText}");
        builder.AppendLine();

        builder.AppendLine("## Что обнаружено");
        builder.AppendLine(investigation.DetectedProblem);
        builder.AppendLine();

        builder.AppendLine("## Почему это важно");
        builder.AppendLine(investigation.UserExplanation);
        builder.AppendLine();

        builder.AppendLine("## Возможные причины");
        foreach (var cause in investigation.PossibleCauses)
        {
            builder.AppendLine($"- {cause}");
        }

        builder.AppendLine();
        builder.AppendLine("## Что пользователь сделал");
        if (investigation.UserActions.Count == 0)
        {
            builder.AppendLine("- Действий пока не отмечено.");
        }
        else
        {
            foreach (var action in investigation.UserActions)
            {
                builder.AppendLine($"- {action.Timestamp:yyyy-MM-dd HH:mm}: {action.ActionTitle} {action.UserComment}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Значения до");
        builder.AppendLine(_formatter.SnapshotSummary(investigation.BeforeSnapshot));
        builder.AppendLine();
        builder.AppendLine("## Значения после");
        builder.AppendLine(_formatter.SnapshotSummary(investigation.AfterSnapshot));
        builder.AppendLine();
        builder.AppendLine("## Вывод");
        builder.AppendLine(investigation.Conclusion);
        builder.AppendLine();

        builder.AppendLine("## Рекомендации");
        foreach (var action in investigation.NextActions)
        {
            builder.AppendLine($"- {action}");
        }

        builder.AppendLine();
        builder.AppendLine("## Технические подробности");
        builder.AppendLine(investigation.TechnicalExplanation);
        foreach (var detail in investigation.TechnicalDetails)
        {
            builder.AppendLine($"- {detail}");
        }

        await File.WriteAllTextAsync(path, builder.ToString(), Encoding.UTF8);
    }
}
