using DiskHealthAdvisor.Models;

namespace DiskHealthAdvisor.Services.HealthAnalysis;

public sealed class InvestigationComparisonService
{
    public InvestigationComparisonResult Compare(DiagnosticRule rule, DiskSnapshot? before, DiskSnapshot? after, IReadOnlyList<InvestigationUserAction> actions)
    {
        if (before is null || after is null)
        {
            return NeedMoreData("Нет одного из снимков состояния.");
        }

        return rule.Id switch
        {
            "nvme_overheat" or "hdd_overheat" => CompareTemperature(rule, before, after),
            "crc_errors_growing" => CompareNoGrowth(rule, before.CrcErrors, after.CrcErrors, "ошибок передачи", actions),
            "hdd_pending_sectors" => ComparePending(rule, before, after),
            "hdd_reallocated_sectors" => CompareGrowth(rule, before.ReallocatedSectors, after.ReallocatedSectors, "переназначенных участков"),
            "uncorrectable_errors" => CompareGrowth(rule, before.UncorrectableErrors, after.UncorrectableErrors, "неисправимых ошибок"),
            "nvme_media_errors" => CompareGrowth(rule, before.MediaErrors, after.MediaErrors, "ошибок данных NVMe"),
            "unsafe_shutdowns_growing" => CompareNoGrowth(rule, before.UnsafeShutdowns, after.UnsafeShutdowns, "некорректных выключений", actions),
            _ => Result(InvestigationStatus.NotChanged, InvestigationConfidence.Low, rule.ConclusionIfNotChanged, "Выполнено базовое сравнение снимков.")
        };
    }

    private static InvestigationComparisonResult CompareTemperature(DiagnosticRule rule, DiskSnapshot before, DiskSnapshot after)
    {
        if (before.TemperatureCelsius is null || after.TemperatureCelsius is null) return NeedMoreData("Нет температуры в одном из снимков.");
        if (before.TemperatureCelsius.Value - after.TemperatureCelsius.Value >= 10)
        {
            return Result(InvestigationStatus.Improved, InvestigationConfidence.Medium,
                $"Температура снизилась с {before.TemperatureCelsius}°C до {after.TemperatureCelsius}°C. {rule.ConclusionIfImproved}",
                $"Температура: {before.TemperatureCelsius} -> {after.TemperatureCelsius}°C.");
        }

        if (after.TemperatureCelsius > before.TemperatureCelsius)
        {
            return Result(InvestigationStatus.Worse, InvestigationConfidence.Medium,
                $"Температура выросла с {before.TemperatureCelsius}°C до {after.TemperatureCelsius}°C. {rule.ConclusionIfWorse}",
                $"Температура выросла на {after.TemperatureCelsius - before.TemperatureCelsius}°C.");
        }

        return Result(InvestigationStatus.NotChanged, InvestigationConfidence.Low,
            $"Температура не снизилась заметно. {rule.ConclusionIfNotChanged}",
            $"Температура: {before.TemperatureCelsius} -> {after.TemperatureCelsius}°C.");
    }

    private static InvestigationComparisonResult CompareNoGrowth(DiagnosticRule rule, ulong? before, ulong? after, string metricName, IReadOnlyList<InvestigationUserAction> actions)
    {
        if (before is null || after is null) return NeedMoreData($"Нет данных для сравнения {metricName}.");
        if (after == before)
        {
            var action = actions.LastOrDefault()?.ActionTitle;
            var prefix = string.IsNullOrWhiteSpace(action) ? "" : $"После действия «{action}» ";
            return Result(InvestigationStatus.Improved, InvestigationConfidence.Medium,
                $"{prefix}новых {metricName} не появилось. {rule.ConclusionIfImproved}",
                $"{metricName}: {before} -> {after}.");
        }

        if (after > before)
        {
            return Result(InvestigationStatus.Worse, InvestigationConfidence.Medium,
                $"Количество {metricName} выросло с {before} до {after}. {rule.ConclusionIfWorse}",
                $"{metricName}: +{after - before}.");
        }

        return Result(InvestigationStatus.Improved, InvestigationConfidence.Low,
            $"Количество {metricName} стало меньше. {rule.ConclusionIfImproved}",
            $"{metricName}: {before} -> {after}.");
    }

    private static InvestigationComparisonResult ComparePending(DiagnosticRule rule, DiskSnapshot before, DiskSnapshot after)
    {
        if (before.CurrentPendingSectors is null || after.CurrentPendingSectors is null) return NeedMoreData("Нет данных о нестабильных участках.");
        var reallocGrew = before.ReallocatedSectors is not null && after.ReallocatedSectors is not null && after.ReallocatedSectors > before.ReallocatedSectors;
        if (after.CurrentPendingSectors > before.CurrentPendingSectors || reallocGrew)
        {
            return Result(InvestigationStatus.PhysicalFailureSuspected, InvestigationConfidence.High,
                "Количество проблемных участков увеличилось. Вероятна физическая деградация диска. Срочно сохраните данные.",
                $"Pending: {before.CurrentPendingSectors} -> {after.CurrentPendingSectors}; Reallocated: {before.ReallocatedSectors} -> {after.ReallocatedSectors}.");
        }

        if (after.CurrentPendingSectors == 0)
        {
            return Result(InvestigationStatus.Improved, InvestigationConfidence.Medium, rule.ConclusionIfImproved, $"Pending: {before.CurrentPendingSectors} -> 0.");
        }

        return Result(InvestigationStatus.NotChanged, InvestigationConfidence.Medium, rule.ConclusionIfNotChanged, $"Pending: {before.CurrentPendingSectors} -> {after.CurrentPendingSectors}.");
    }

    private static InvestigationComparisonResult CompareGrowth(DiagnosticRule rule, ulong? before, ulong? after, string metricName)
    {
        if (before is null || after is null) return NeedMoreData($"Нет данных для сравнения {metricName}.");
        if (after > before)
        {
            return Result(InvestigationStatus.PhysicalFailureSuspected, InvestigationConfidence.High,
                $"Количество {metricName} увеличилось с {before} до {after}. {rule.ConclusionIfWorse}",
                $"{metricName}: +{after - before}.");
        }

        return Result(InvestigationStatus.NotChanged, InvestigationConfidence.Medium,
            $"Количество {metricName} не выросло. {rule.ConclusionIfNotChanged}",
            $"{metricName}: {before} -> {after}.");
    }

    private static InvestigationComparisonResult NeedMoreData(string reason)
    {
        return new InvestigationComparisonResult
        {
            Status = InvestigationStatus.NeedMoreData,
            Confidence = InvestigationConfidence.Low,
            ConfidenceReason = reason,
            Conclusion = "Данных недостаточно для уверенного вывода. Нужно продолжить наблюдение или подключить smartctl.",
            TechnicalDetails = [reason]
        };
    }

    private static InvestigationComparisonResult Result(InvestigationStatus status, InvestigationConfidence confidence, string conclusion, string detail)
    {
        return new InvestigationComparisonResult
        {
            Status = status,
            Confidence = confidence,
            ConfidenceReason = confidence switch
            {
                InvestigationConfidence.High => "Изменились показатели, которые напрямую связаны с этой проблемой.",
                InvestigationConfidence.Medium => "Динамика показателей совпадает с ожидаемым результатом проверки.",
                _ => "Данных достаточно только для осторожного вывода."
            },
            Conclusion = conclusion,
            TechnicalDetails = [detail]
        };
    }
}
