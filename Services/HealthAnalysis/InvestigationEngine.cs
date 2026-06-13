using System.Collections.ObjectModel;
using DiskHealthAdvisor.Helpers;
using DiskHealthAdvisor.Models;
using DiskHealthAdvisor.Services.Database;

namespace DiskHealthAdvisor.Services.HealthAnalysis;

public sealed class InvestigationEngine
{
    private readonly DiagnosticRuleRepository _rules;
    private readonly InvestigationRepository _repository;
    private readonly SsdTbwDatabaseService _tbwDatabase;
    private readonly InvestigationComparisonService _comparison;
    private readonly SimpleTextFormatter _formatter;

    public InvestigationEngine(
        DiagnosticRuleRepository rules,
        InvestigationRepository repository,
        SsdTbwDatabaseService tbwDatabase,
        InvestigationComparisonService comparison,
        SimpleTextFormatter formatter)
    {
        _rules = rules;
        _repository = repository;
        _tbwDatabase = tbwDatabase;
        _comparison = comparison;
        _formatter = formatter;
    }

    public async Task<List<DiskInvestigation>> RefreshAsync(IReadOnlyList<DiskInfo> disks, IReadOnlyList<DiskSnapshot> history)
    {
        var investigations = await _repository.LoadAsync();
        var rules = await _rules.LoadAsync();

        foreach (var disk in disks)
        {
            var previous = history
                .Where(s => s.DiskIdentity == disk.Identity)
                .OrderByDescending(s => s.Timestamp)
                .Skip(1)
                .FirstOrDefault();

            var current = history
                .Where(s => s.DiskIdentity == disk.Identity)
                .OrderByDescending(s => s.Timestamp)
                .FirstOrDefault();

            var tbw = await _tbwDatabase.FindForDiskAsync(disk);
            var tbwPercent = CalculateTbwPercent(disk, tbw);
            var freePercent = CalculateFreeSpacePercent(disk);
            var dailyWriteGb = CalculateDailyWriteGb(disk, previous);

            foreach (var rule in rules.Where(r => MatchesDiskType(r, disk)))
            {
                var existing = investigations.FirstOrDefault(i =>
                    i.DiskId == disk.Identity &&
                    i.RuleId == rule.Id &&
                    i.Status is not InvestigationStatus.ClosedByUser and not InvestigationStatus.Improved);

                var matches = RuleMatches(rule, disk, previous, tbwPercent, freePercent, dailyWriteGb);
                if (!matches)
                {
                    if (existing is not null)
                    {
                        UpdateInvestigationNoLongerMatches(existing, disk, rule, current, tbwPercent, freePercent, dailyWriteGb);
                    }

                    continue;
                }

                if (existing is null)
                {
                    investigations.Add(CreateInvestigation(disk, rule, current ?? HistoryService.CreateSnapshot(disk, new HealthReport()), tbwPercent, freePercent, dailyWriteGb));
                }
                else
                {
                    UpdateInvestigation(existing, disk, rule, current, tbwPercent, freePercent, dailyWriteGb);
                }
            }
        }

        await _repository.SaveAsync(investigations);
        return investigations.OrderByDescending(i => i.UpdatedAt).ToList();
    }

    public async Task<List<DiskInvestigation>> AddUserActionAsync(string investigationId, string actionTitle, string? comment)
    {
        var investigations = await _repository.LoadAsync();
        var investigation = investigations.FirstOrDefault(i => i.Id == investigationId);
        if (investigation is not null)
        {
            investigation.UserActions.Add(new InvestigationUserAction
            {
                Timestamp = DateTimeOffset.Now,
                ActionTitle = actionTitle,
                UserComment = comment
            });
            investigation.Status = InvestigationStatus.WaitingForRecheck;
            investigation.UpdatedAt = DateTimeOffset.Now;
            investigation.Conclusion = "Действие отмечено. Теперь повторите проверку, чтобы сравнить показатели до и после.";
            await _repository.SaveAsync(investigations);
        }

        return investigations.OrderByDescending(i => i.UpdatedAt).ToList();
    }

    public async Task<List<DiskInvestigation>> RecheckAsync(string investigationId, DiskSnapshot afterSnapshot)
    {
        var investigations = await _repository.LoadAsync();
        var rules = await _rules.LoadAsync();
        var investigation = investigations.FirstOrDefault(i => i.Id == investigationId);
        if (investigation is null)
        {
            return investigations;
        }

        var rule = rules.FirstOrDefault(r => r.Id == investigation.RuleId);
        if (rule is null)
        {
            investigation.Status = InvestigationStatus.NeedMoreData;
            investigation.Conclusion = "Правило расследования не найдено. Данных недостаточно для вывода.";
        }
        else
        {
            investigation.AfterSnapshot = afterSnapshot;
            var result = _comparison.Compare(rule, investigation.BeforeSnapshot, afterSnapshot, investigation.UserActions);
            investigation.Status = result.Status;
            investigation.Confidence = result.Confidence;
            investigation.ConfidenceReason = result.ConfidenceReason;
            investigation.Conclusion = result.Conclusion;
            foreach (var detail in result.TechnicalDetails)
            {
                if (!investigation.TechnicalDetails.Contains(detail))
                {
                    investigation.TechnicalDetails.Add(detail);
                }
            }

            if (result.Status is InvestigationStatus.Improved or InvestigationStatus.NotChanged or InvestigationStatus.Worse or InvestigationStatus.PhysicalFailureSuspected)
            {
                investigation.FinishedAt = DateTimeOffset.Now;
            }
        }

        investigation.UpdatedAt = DateTimeOffset.Now;
        await _repository.SaveAsync(investigations);
        return investigations.OrderByDescending(i => i.UpdatedAt).ToList();
    }

    private DiskInvestigation CreateInvestigation(DiskInfo disk, DiagnosticRule rule, DiskSnapshot before, decimal? tbwPercent, decimal? freePercent, decimal? dailyWriteGb)
    {
        return new DiskInvestigation
        {
            DiskId = disk.Identity,
            DiskModel = DiskTitle(disk),
            DiskSummary = BuildDiskSummary(disk),
            RuleId = rule.Id,
            Type = string.Join(", ", rule.DiskTypes),
            Status = rule.RiskLevel == DiagnosticRiskLevel.NotEnoughData ? InvestigationStatus.NeedMoreData : InvestigationStatus.WaitingForUserAction,
            RiskLevel = rule.RiskLevel,
            Confidence = InitialConfidence(rule, disk),
            ConfidenceReason = InitialConfidenceReason(rule),
            SimpleTitle = rule.SimpleTitle,
            TriggerMetricText = BuildTriggerMetricText(rule, disk, tbwPercent, freePercent, dailyWriteGb),
            PrimaryActionText = BuildPrimaryActionText(rule),
            DetectedProblem = BuildDetectedProblem(disk, rule),
            UserExplanation = rule.UserExplanation,
            TechnicalExplanation = rule.TechnicalExplanation,
            PossibleCauses = new ObservableCollection<string>(rule.PossibleCauses),
            SuggestedChecks = new ObservableCollection<string>(rule.SuggestedChecks),
            BeforeSnapshot = before,
            Conclusion = "Расследование начато. Выполните безопасную рекомендацию и повторите проверку.",
            NextActions = new ObservableCollection<string>(rule.Recommendations),
            TechnicalDetails = new ObservableCollection<string>(BuildTechnicalDetails(rule, disk, before, tbwPercent, freePercent, dailyWriteGb)),
            CreatedAt = DateTimeOffset.Now,
            UpdatedAt = DateTimeOffset.Now
        };
    }

    private void UpdateInvestigation(DiskInvestigation investigation, DiskInfo disk, DiagnosticRule rule, DiskSnapshot? current, decimal? tbwPercent, decimal? freePercent, decimal? dailyWriteGb)
    {
        investigation.DiskModel = DiskTitle(disk);
        investigation.DiskSummary = BuildDiskSummary(disk);
        investigation.SimpleTitle = rule.SimpleTitle;
        investigation.RiskLevel = rule.RiskLevel;
        investigation.TriggerMetricText = BuildTriggerMetricText(rule, disk, tbwPercent, freePercent, dailyWriteGb);
        investigation.PrimaryActionText = BuildPrimaryActionText(rule);
        investigation.DetectedProblem = BuildDetectedProblem(disk, rule);
        investigation.UserExplanation = rule.UserExplanation;
        investigation.TechnicalExplanation = rule.TechnicalExplanation;
        investigation.PossibleCauses = new ObservableCollection<string>(rule.PossibleCauses);
        investigation.SuggestedChecks = new ObservableCollection<string>(rule.SuggestedChecks);
        investigation.NextActions = new ObservableCollection<string>(rule.Recommendations);
        investigation.UpdatedAt = DateTimeOffset.Now;
        investigation.TechnicalDetails = new ObservableCollection<string>(BuildTechnicalDetails(rule, disk, current, tbwPercent, freePercent, dailyWriteGb));
    }

    private void UpdateInvestigationNoLongerMatches(DiskInvestigation investigation, DiskInfo disk, DiagnosticRule rule, DiskSnapshot? current, decimal? tbwPercent, decimal? freePercent, decimal? dailyWriteGb)
    {
        investigation.DiskModel = DiskTitle(disk);
        investigation.DiskSummary = BuildDiskSummary(disk);
        investigation.SimpleTitle = rule.SimpleTitle;
        investigation.RiskLevel = DiagnosticRiskLevel.Normal;
        investigation.TriggerMetricText = BuildTriggerMetricText(rule, disk, tbwPercent, freePercent, dailyWriteGb);
        investigation.PrimaryActionText = rule.Id == "high_daily_writes"
            ? "Сейчас высокая запись не подтверждается. Оставьте наблюдение в трее: если процесс снова начнет заметно писать, он появится в журнале «Кто писал сегодня»."
            : "Сейчас правило не подтверждается свежими данными. Продолжайте наблюдение и повторите проверку, если симптом вернется.";
        investigation.DetectedProblem = $"{DiskTitle(disk)}: свежая проверка больше не подтверждает это расследование.";
        investigation.Confidence = InvestigationConfidence.Medium;
        investigation.ConfidenceReason = "На свежей проверке условие правила уже не выполняется.";
        investigation.Status = dailyWriteGb is null && rule.Id == "high_daily_writes"
            ? InvestigationStatus.NeedMoreData
            : InvestigationStatus.Improved;
        investigation.UserExplanation = rule.UserExplanation;
        investigation.TechnicalExplanation = rule.TechnicalExplanation;
        investigation.SuggestedChecks = new ObservableCollection<string>
        {
            "Оставьте наблюдение включенным в трее.",
            "Посмотрите журнал «Кто писал сегодня», если запись повторится.",
            "Нажмите «Обновить» позже, чтобы сравнить свежий снимок диска."
        };
        investigation.NextActions = new ObservableCollection<string>
        {
            "Наблюдать без действий, если запись не повторяется.",
            "Если в журнале появится процесс с большой записью, закрыть его или перенести его кэш/загрузки на другой диск."
        };
        investigation.AfterSnapshot = current;
        investigation.FinishedAt = investigation.Status == InvestigationStatus.Improved ? DateTimeOffset.Now : null;
        investigation.Conclusion = investigation.Status == InvestigationStatus.Improved
            ? "Сейчас проблема не подтверждается: свежие данные не показывают превышение правила. Историю заметной записи процессов смотрите в блоке «Кто писал сегодня»."
            : "Сейчас не хватает свежих данных, чтобы подтвердить или снять проблему. Наблюдение будет записывать заметные процессы в журнал.";
        investigation.UpdatedAt = DateTimeOffset.Now;
        investigation.TechnicalDetails = new ObservableCollection<string>(BuildTechnicalDetails(rule, disk, current, tbwPercent, freePercent, dailyWriteGb));
    }

    private string BuildDetectedProblem(DiskInfo disk, DiagnosticRule rule)
    {
        return $"{DiskTitle(disk)}: {_formatter.DescribeDetectedProblem(disk, rule)}";
    }

    private static string DiskTitle(DiskInfo disk)
    {
        return string.IsNullOrWhiteSpace(disk.Model) ? disk.Identity : disk.Model.Trim();
    }

    private static string BuildDiskSummary(DiskInfo disk)
    {
        var volumes = disk.LogicalVolumes.Count == 0
            ? "разделы не определены"
            : string.Join(", ", disk.LogicalVolumes.Select(v => string.IsNullOrWhiteSpace(v.Name) ? "без буквы" : v.Name));

        return $"{DiskTitle(disk)} • {disk.MediaTypeDisplay} • {FormatHelper.Bytes(disk.SizeBytes)} • {volumes}";
    }

    private static string BuildTriggerMetricText(DiagnosticRule rule, DiskInfo disk, decimal? tbwPercent, decimal? freePercent, decimal? dailyWriteGb)
    {
        if (rule.Id == "high_daily_writes")
        {
            return dailyWriteGb is null
                ? "Темп записи: пока нет точного расчета. Нужны два снимка диска, чтобы сравнить рост записанных данных."
                : $"Темп записи: примерно {dailyWriteGb:0.#} ГБ/день. Порог расследования: 100 ГБ/день.";
        }

        var symptom = rule.Symptoms.FirstOrDefault();
        if (symptom is null)
        {
            return "Показатель: нет данных.";
        }

        var current = DiskMetricAccessor.GetCurrent(disk, symptom.Metric, tbwPercent, freePercent, dailyWriteGb);
        var expected = symptom.Value is null ? "предыдущее значение" : $"{symptom.Value:0.##}";
        return $"{MetricName(symptom.Metric)}: {FormatMetricValue(symptom.Metric, current)}. Условие: {symptom.Operator} {expected}.";
    }

    private static string BuildPrimaryActionText(DiagnosticRule rule)
    {
        return rule.Id switch
        {
            "high_daily_writes" => "Сначала посмотрите блок «Кто сейчас пишет». Если сверху торрент, игра, браузер, запись видео или System с большой записью, остановите задачу или перенесите кэш/загрузки на другой диск. Затем подождите 1-2 минуты и нажмите «Повторить проверку».",
            "nvme_overheat" or "hdd_overheat" => "Снизьте нагрузку, проверьте обдув и место установки диска. После охлаждения нажмите «Повторить проверку».",
            "hdd_pending_sectors" or "hdd_reallocated_sectors" or "uncorrectable_errors" or "nvme_media_errors" => "Сначала сохраните важные данные. Потом проверьте кабель/порт или состояние диска и повторите проверку.",
            "low_free_space" => "Освободите место или перенесите крупные файлы, затем повторите проверку.",
            _ => rule.Recommendations.FirstOrDefault() ?? "Выполните безопасную рекомендацию и нажмите «Повторить проверку»."
        };
    }

    private static string MetricName(string metric)
    {
        return metric switch
        {
            nameof(DiskInfo.TemperatureCelsius) => "Температура",
            nameof(DiskInfo.TotalBytesWritten) => "Всего записано",
            nameof(DiskInfo.TotalBytesRead) => "Всего прочитано",
            nameof(DiskInfo.WearPercentage) => "Износ SSD",
            nameof(DiskInfo.MediaErrors) => "Ошибки носителя",
            nameof(DiskInfo.ReallocatedSectors) => "Переназначенные сектора",
            nameof(DiskInfo.CurrentPendingSectors) => "Нестабильные сектора",
            nameof(DiskInfo.UncorrectableErrors) => "Неисправимые ошибки",
            nameof(DiskInfo.CrcErrors) => "CRC-ошибки",
            nameof(DiskInfo.SmartPassed) => "SMART",
            "TbwUsedPercent" => "Использовано TBW",
            "FreeSpacePercent" => "Свободное место",
            "DailyWriteGb" => "Запись в день",
            _ => metric
        };
    }

    private static string FormatMetricValue(string metric, decimal? value)
    {
        if (value is null)
        {
            return "нет данных";
        }

        return metric switch
        {
            nameof(DiskInfo.TemperatureCelsius) => $"{value:0.#} °C",
            nameof(DiskInfo.TotalBytesWritten) or nameof(DiskInfo.TotalBytesRead) => $"{value / 1_000_000_000_000m:0.##} ТБ",
            "TbwUsedPercent" or "FreeSpacePercent" => $"{value:0.#}%",
            "DailyWriteGb" => $"{value:0.#} ГБ/день",
            nameof(DiskInfo.SmartPassed) => value == 1 ? "SMART без ошибки" : "SMART сообщил о проблеме",
            _ => $"{value:0.##}"
        };
    }

    private static List<string> BuildTechnicalDetails(DiagnosticRule rule, DiskInfo disk, DiskSnapshot? snapshot, decimal? tbwPercent, decimal? freePercent, decimal? dailyWriteGb)
    {
        var details = new List<string>
        {
            $"Правило: {rule.Id}",
            $"Диск: {DiskTitle(disk)}",
            $"Тип/объем: {disk.MediaTypeDisplay}, {FormatHelper.Bytes(disk.SizeBytes)}",
            $"Записано всего: {FormatHelper.Terabytes(disk.TotalBytesWritten)}",
            $"Прочитано всего: {FormatHelper.Terabytes(disk.TotalBytesRead)}",
            dailyWriteGb is null ? "Оценка записи в день: нет данных" : $"Оценка записи в день: {dailyWriteGb:0.#} ГБ/день"
        };
        foreach (var symptom in rule.Symptoms)
        {
            var current = DiskMetricAccessor.GetCurrent(disk, symptom.Metric, tbwPercent, freePercent, dailyWriteGb);
            var previous = DiskMetricAccessor.GetSnapshot(snapshot, symptom.Metric, freePercent);
            details.Add($"{symptom.Metric}: текущее значение {current?.ToString() ?? "нет данных"}, условие {symptom.Operator} {symptom.Value?.ToString() ?? "предыдущее"}.");
            if (previous is not null)
            {
                details.Add($"{symptom.Metric}: значение в снимке {previous}.");
            }
        }

        return details;
    }

    private static bool RuleMatches(DiagnosticRule rule, DiskInfo disk, DiskSnapshot? previous, decimal? tbwPercent, decimal? freePercent, decimal? dailyWriteGb)
    {
        if (rule.Symptoms.Count == 0)
        {
            return false;
        }

        return rule.Symptoms.All(symptom =>
        {
            var current = DiskMetricAccessor.GetCurrent(disk, symptom.Metric, tbwPercent, freePercent, dailyWriteGb);
            if (symptom.Operator == "missing")
            {
                return current is null;
            }

            var prev = symptom.CompareWithPreviousSnapshot
                ? DiskMetricAccessor.GetSnapshot(previous, symptom.Metric, freePercent)
                : null;
            return DiskMetricAccessor.Compare(current, symptom.Operator, symptom.Value, prev);
        });
    }

    private static bool MatchesDiskType(DiagnosticRule rule, DiskInfo disk)
    {
        if (rule.DiskTypes.Count == 0)
        {
            return true;
        }

        return rule.DiskTypes.Any(t =>
            string.Equals(t, disk.MediaType.ToString(), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t, disk.MediaTypeDisplay.Replace(" ", ""), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t, "SSD", StringComparison.OrdinalIgnoreCase) && disk.MediaType is DiskMediaKind.SSD or DiskMediaKind.SataSSD or DiskMediaKind.NvmeSSD);
    }

    private static decimal? CalculateTbwPercent(DiskInfo disk, SsdTbwRecord? tbw)
    {
        if (tbw is null || tbw.Tbw <= 0 || disk.TotalBytesWritten is null)
        {
            return null;
        }

        return (decimal)disk.TotalBytesWritten.Value / (tbw.Tbw * 1_000_000_000_000m) * 100m;
    }

    private static decimal? CalculateFreeSpacePercent(DiskInfo disk)
    {
        var size = disk.LogicalVolumes.Aggregate(0UL, (sum, v) => sum + (v.SizeBytes ?? 0));
        var free = disk.LogicalVolumes.Aggregate(0UL, (sum, v) => sum + (v.FreeBytes ?? 0));
        if (size == 0)
        {
            return null;
        }

        return (decimal)free / size * 100m;
    }

    private static decimal? CalculateDailyWriteGb(DiskInfo disk, DiskSnapshot? previous)
    {
        if (previous?.TotalBytesWritten is null || disk.TotalBytesWritten is null)
        {
            return null;
        }

        var delta = disk.TotalBytesWritten.Value >= previous.TotalBytesWritten.Value
            ? disk.TotalBytesWritten.Value - previous.TotalBytesWritten.Value
            : 0;
        var days = Math.Max(1m, (decimal)(DateTimeOffset.Now - previous.Timestamp).TotalDays);
        return delta / 1_000_000_000m / days;
    }

    private static InvestigationConfidence InitialConfidence(DiagnosticRule rule, DiskInfo disk)
    {
        return rule.Id switch
        {
            "hdd_pending_sectors" or "hdd_reallocated_sectors" or "uncorrectable_errors" or "nvme_media_errors" => InvestigationConfidence.High,
            "crc_errors_growing" or "nvme_overheat" or "hdd_overheat" => InvestigationConfidence.Medium,
            "smart_not_available" => InvestigationConfidence.Low,
            _ => disk.RawAttributes.Count > 0 ? InvestigationConfidence.Medium : InvestigationConfidence.Low
        };
    }

    private static string InitialConfidenceReason(DiagnosticRule rule)
    {
        return rule.Id switch
        {
            "crc_errors_growing" => "Растут именно ошибки передачи данных, а повреждённые участки не обязательно увеличиваются.",
            "hdd_pending_sectors" => "Нестабильные участки напрямую связаны с риском чтения данных.",
            "hdd_reallocated_sectors" => "Переназначенные участки показывают, что диск уже сталкивался с повреждёнными областями.",
            "nvme_overheat" or "hdd_overheat" => "Температура напрямую измеряется диском.",
            _ => "Вывод основан на доступных SMART/NVMe-показателях."
        };
    }
}
