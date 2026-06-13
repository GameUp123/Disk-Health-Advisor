using System.Text;
using DiskHealthAdvisor.Helpers;
using DiskHealthAdvisor.Models;

namespace DiskHealthAdvisor.Services;

public sealed class ReportExportService
{
    public async Task ExportMarkdownAsync(string path, IReadOnlyList<DiskInfo> disks, DiskInfo? selectedDisk, HealthReport? report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Disk Health Advisor");
        builder.AppendLine();
        builder.AppendLine($"Дата: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine();
        builder.AppendLine("## Диски");
        foreach (var disk in disks)
        {
            builder.AppendLine($"- {FormatHelper.OptionalString(disk.Model)} ({disk.MediaType}, {FormatHelper.Bytes(disk.SizeBytes)})");
        }

        if (selectedDisk is not null && report is not null)
        {
            builder.AppendLine();
            builder.AppendLine("## Общий диагноз");
            builder.AppendLine(report.Summary);
            builder.AppendLine();
            builder.AppendLine("## Причины");
            foreach (var reason in report.Reasons)
            {
                builder.AppendLine($"- {reason}");
            }

            builder.AppendLine();
            builder.AppendLine("## Рекомендации");
            foreach (var recommendation in report.Recommendations)
            {
                builder.AppendLine($"- {recommendation}");
            }

            builder.AppendLine();
            builder.AppendLine("## Ключевые параметры");
            foreach (var detail in report.Details)
            {
                builder.AppendLine($"- **{detail.Name}:** {detail.Value}");
            }

            builder.AppendLine();
            builder.AppendLine("## Сырые критичные SMART-атрибуты");
            var critical = selectedDisk.RawAttributes
                .Where(a => IsCritical(a.Name))
                .DefaultIfEmpty(new SmartAttributeInfo { Name = "Нет данных", RawValue = "Нет данных" });

            foreach (var attribute in critical)
            {
                builder.AppendLine($"- {attribute.Name}: raw={attribute.RawValue ?? "Нет данных"}, value={attribute.NormalizedValue?.ToString() ?? "Нет данных"}, threshold={attribute.Threshold?.ToString() ?? "Нет данных"}");
            }
        }

        await File.WriteAllTextAsync(path, builder.ToString(), Encoding.UTF8);
    }

    private static bool IsCritical(string name)
    {
        var normalized = name.Replace("_", " ", StringComparison.Ordinal).ToUpperInvariant();
        return normalized.Contains("REALLOCATED") ||
               normalized.Contains("PENDING") ||
               normalized.Contains("UNCORRECTABLE") ||
               normalized.Contains("CRC") ||
               normalized.Contains("MEDIA ERROR");
    }
}
