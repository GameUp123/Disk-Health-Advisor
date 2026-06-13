using DiskHealthAdvisor.Helpers;
using DiskHealthAdvisor.Models;

namespace DiskHealthAdvisor.Services.HealthAnalysis;

public sealed class DiskHealthAnalyzer
{
    public HealthReport Analyze(DiskInfo disk, IReadOnlyList<DiskSnapshot> history, SsdTbwRecord? tbwRecord)
    {
        var report = new HealthReport();
        var risk = 0;
        var hasData = false;
        var previous = history.Where(s => s.DiskIdentity == disk.Identity).OrderByDescending(s => s.Timestamp).FirstOrDefault();

        AddTemperature(disk, report, ref risk, ref hasData);
        AddWear(disk, tbwRecord, report, ref risk, ref hasData);
        AddErrors(disk, previous, report, ref risk, ref hasData);
        AddDetails(disk, tbwRecord, report);

        report.RiskScore = Math.Clamp(risk, 0, 100);
        report.Level = hasData ? LevelFromRisk(report.RiskScore) : HealthLevel.Unknown;
        report.Summary = BuildSummary(report.Level, report.RiskScore, hasData);

        if (report.Reasons.Count == 0)
        {
            report.Reasons.Add(hasData
                ? "По доступным данным явных опасных признаков не найдено."
                : "Данных недостаточно для точной оценки.");
        }

        if (report.Recommendations.Count == 0)
        {
            report.Recommendations.Add("Держите резервные копии важных файлов и периодически повторяйте проверку.");
        }

        return report;
    }

    public SsdResourceSummary BuildSsdResourceSummary(DiskInfo disk, SsdTbwRecord? record, IReadOnlyList<DiskSnapshot> history)
    {
        if (record is null || record.Tbw <= 0)
        {
            return new SsdResourceSummary();
        }

        var tbwBytes = record.Tbw * 1_000_000_000_000m;
        decimal? used = disk.TotalBytesWritten is null ? null : (decimal)disk.TotalBytesWritten.Value / tbwBytes * 100m;
        var average = CalculateAverageWrittenGbPerDay(disk, history);
        DateTimeOffset? estimated = null;

        if (disk.TotalBytesWritten is not null && average is > 0)
        {
            var remaining = Math.Max(0, tbwBytes - disk.TotalBytesWritten.Value);
            estimated = DateTimeOffset.Now.AddDays((double)Math.Min(remaining / (average.Value * 1_000_000_000m), 36500));
        }

        return new SsdResourceSummary
        {
            HasTbwData = true,
            DeclaredTbw = record.Tbw,
            TotalBytesWritten = disk.TotalBytesWritten,
            UsedPercent = used,
            AverageWrittenGbPerDay = average,
            EstimatedTbwDate = estimated,
            DeclaredTbwText = $"{record.Tbw:0.##} ТБ",
            TotalBytesWrittenText = FormatHelper.Terabytes(disk.TotalBytesWritten),
            UsedPercentText = used is null ? "Нет данных" : $"{used:0.#}%",
            AverageWrittenText = average is null ? "Нет данных" : $"{average:0.##} ГБ/день",
            EstimatedTbwDateText = estimated is null ? "Нет данных" : estimated.Value.ToString("yyyy-MM-dd"),
            Message = "TBW — это гарантийный ориентир, а не точная дата смерти диска. При приближении к TBW риск повышается."
        };
    }

    private static void AddTemperature(DiskInfo disk, HealthReport report, ref int risk, ref bool hasData)
    {
        if (disk.TemperatureCelsius is null) return;
        hasData = true;
        var limit = disk.MediaType == DiskMediaKind.HDD ? 55 : 70;
        if (disk.TemperatureCelsius >= limit)
        {
            risk += 20;
            report.Reasons.Add($"Температура повышена: {disk.TemperatureCelsius}°C.");
            report.Recommendations.Add("Проверьте охлаждение, пыль, нагрузку и повторите проверку.");
        }
    }

    private static void AddWear(DiskInfo disk, SsdTbwRecord? tbw, HealthReport report, ref int risk, ref bool hasData)
    {
        if (disk.WearPercentage is >= 80)
        {
            hasData = true;
            risk += disk.WearPercentage >= 90 ? 35 : 25;
            report.Reasons.Add("SSD использовал большую часть ресурса. Это не точная дата поломки, но риск выше.");
            report.Recommendations.Add("Сделайте резервную копию и планируйте замену.");
        }

        if (tbw is not null && disk.TotalBytesWritten is not null && tbw.Tbw > 0)
        {
            hasData = true;
            var used = (decimal)disk.TotalBytesWritten.Value / (tbw.Tbw * 1_000_000_000_000m) * 100m;
            if (used >= 100)
            {
                risk += 30;
                report.Reasons.Add($"Записано около {used:0.#}% заявленного TBW. Гарантийный ресурс уже исчерпан.");
            }
            else if (used >= 80)
            {
                risk += 20;
                report.Reasons.Add($"Использовано около {used:0.#}% заявленного TBW.");
            }
        }
    }

    private static void AddErrors(DiskInfo disk, DiskSnapshot? previous, HealthReport report, ref int risk, ref bool hasData)
    {
        if (disk.SmartPassed is not null) hasData = true;
        if (disk.SmartPassed == false)
        {
            risk += 40;
            report.Reasons.Add("SMART-статус сообщает о проблеме.");
            report.Recommendations.Add("Срочно сохраните важные данные.");
        }

        AddCounter(disk.CurrentPendingSectors, "Есть нестабильные участки. Сначала сохраните важные файлы.", 40, report, ref risk, ref hasData);
        AddCounter(disk.UncorrectableErrors, "Есть ошибки, которые диск не смог исправить.", 40, report, ref risk, ref hasData);
        AddCounter(disk.ReallocatedSectors, "Диск уже заменял повреждённые участки резервными.", 20, report, ref risk, ref hasData);
        AddCounter(disk.MediaErrors, "Диск сообщил об ошибках данных.", 35, report, ref risk, ref hasData);

        if (previous?.CrcErrors is not null && disk.CrcErrors is not null && disk.CrcErrors > previous.CrcErrors)
        {
            risk += 15;
            report.Reasons.Add("Выросло число ошибок передачи данных. Часто причина в SATA-кабеле или порте.");
            report.Recommendations.Add("Проверьте SATA-кабель, порт и питание.");
        }
    }

    private static void AddCounter(ulong? value, string text, int points, HealthReport report, ref int risk, ref bool hasData)
    {
        if (value is > 0)
        {
            hasData = true;
            risk += points;
            report.Reasons.Add(text);
            report.Recommendations.Add("Рекомендуется сделать резервную копию и продолжить наблюдение.");
        }
    }

    private static void AddDetails(DiskInfo disk, SsdTbwRecord? tbwRecord, HealthReport report)
    {
        report.Details.Add(new MetricDisplay("Модель", FormatHelper.OptionalString(disk.Model)));
        report.Details.Add(new MetricDisplay("Тип", disk.MediaTypeDisplay));
        report.Details.Add(new MetricDisplay("Температура", FormatHelper.Optional(disk.TemperatureCelsius, "°C")));
        report.Details.Add(new MetricDisplay("Время работы", FormatHelper.Optional(disk.PowerOnHours, " ч")));
        report.Details.Add(new MetricDisplay("Записано", FormatHelper.Terabytes(disk.TotalBytesWritten)));
        report.Details.Add(new MetricDisplay("Прочитано", FormatHelper.Terabytes(disk.TotalBytesRead)));
        report.Details.Add(new MetricDisplay("Износ SSD", FormatHelper.Optional(disk.WearPercentage, "%")));
        report.Details.Add(new MetricDisplay("TBW в базе", tbwRecord is null ? "Нет данных" : $"{tbwRecord.Tbw:0.##} ТБ"));
        report.Details.Add(new MetricDisplay("SMART", FormatHelper.BoolSmart(disk.SmartPassed)));
    }

    private static decimal? CalculateAverageWrittenGbPerDay(DiskInfo disk, IReadOnlyList<DiskSnapshot> history)
    {
        var first = history.Where(s => s.DiskIdentity == disk.Identity && s.TotalBytesWritten is not null).OrderBy(s => s.Timestamp).FirstOrDefault();
        if (first?.TotalBytesWritten is null || disk.TotalBytesWritten is null) return null;
        var days = Math.Max(1, (DateTimeOffset.Now - first.Timestamp).TotalDays);
        var delta = disk.TotalBytesWritten.Value > first.TotalBytesWritten.Value ? disk.TotalBytesWritten.Value - first.TotalBytesWritten.Value : 0;
        return (decimal)(delta / days / 1_000_000_000d);
    }

    private static HealthLevel LevelFromRisk(int risk) => risk switch
    {
        <= 19 => HealthLevel.Good,
        <= 39 => HealthLevel.Caution,
        <= 69 => HealthLevel.Warning,
        _ => HealthLevel.Critical
    };

    private static string BuildSummary(HealthLevel level, int risk, bool hasData)
    {
        if (!hasData) return "Данных недостаточно для точной оценки.";
        return level switch
        {
            HealthLevel.Good => $"Состояние: Норма. По доступным данным явных признаков деградации не найдено. Индекс риска: {risk}/100.",
            HealthLevel.Caution => $"Состояние: Внимание. Есть показатели, за которыми стоит наблюдать. Индекс риска: {risk}/100.",
            HealthLevel.Warning => $"Состояние: Повышенный риск. Диск ещё может работать, но риск отказа выше обычного. Индекс риска: {risk}/100.",
            HealthLevel.Critical => $"Состояние: Срочно скопировать данные. Есть серьёзные признаки риска. Индекс риска: {risk}/100.",
            _ => "Данных недостаточно для точной оценки."
        };
    }
}
