using DiskHealthAdvisor.Models;

namespace DiskHealthAdvisor.Services.Database;

public sealed class DiagnosticRuleRepository
{
    private readonly JsonFileStore<List<DiagnosticRule>> _store;
    private readonly AppLogger _logger;

    public DiagnosticRuleRepository(ApplicationPaths paths, AppLogger logger)
    {
        _logger = logger;
        _store = new JsonFileStore<List<DiagnosticRule>>(logger);
    }

    public async Task<IReadOnlyList<DiagnosticRule>> LoadAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "diagnostic_rules.json");
        var rules = await _store.LoadAsync(path);
        if (rules.Count == 0)
        {
            rules = CreateDefaultRules();
            try
            {
                await _store.SaveAsync(path, rules);
            }
            catch (Exception ex)
            {
                await _logger.LogAsync("Не удалось сохранить базовые diagnostic_rules.json.", ex);
            }
        }

        return rules.Where(r => !string.IsNullOrWhiteSpace(r.Id)).ToList();
    }

    private static List<DiagnosticRule> CreateDefaultRules()
    {
        return
        [
            Rule("nvme_overheat", "Диск перегревается", ["NvmeSSD"], Condition("TemperatureCelsius", ">=", 70), DiagnosticRiskLevel.Attention,
                "SSD сильно нагрелся. При высокой температуре он может снижать скорость и быстрее изнашиваться.",
                ["Нет радиатора", "Слабый обдув корпуса", "Диск рядом с видеокартой", "Высокая запись"],
                ["Закройте тяжёлые задачи", "Улучшите обдув", "Поставьте радиатор", "Повторите проверку"]),

            Rule("hdd_overheat", "HDD работает при высокой температуре", ["HDD"], Condition("TemperatureCelsius", ">=", 50), DiagnosticRiskLevel.Attention,
                "HDD работает при высокой температуре. Это может ускорять износ механики.",
                ["Слабый обдув", "Пыль", "Плотное расположение дисков"],
                ["Улучшите обдув", "Проверьте пыль", "Повторите проверку"]),

            Rule("crc_errors_growing", "Возможна проблема с SATA-кабелем", ["HDD", "SataSSD", "SSD"], Condition("CrcErrors", "increased", null, true), DiagnosticRiskLevel.Attention,
                "Данные между диском и материнской платой передавались с ошибками. Часто причина не в диске, а в SATA-кабеле или порте.",
                ["Плохой SATA-кабель", "Плохой контакт", "SATA-порт", "Питание"],
                ["Замените SATA-кабель", "Подключите диск в другой порт", "Проверьте питание", "Повторите проверку"]),

            Rule("hdd_pending_sectors", "Есть нестабильные участки на HDD", ["HDD"], Condition("CurrentPendingSectors", ">", 0), DiagnosticRiskLevel.BackupNow,
                "Диск нашёл участки, которые не смог нормально прочитать. Это может быть признаком физической проблемы.",
                ["Физический износ диска", "Повреждённые участки поверхности HDD", "Проблемы с питанием"],
                ["Сначала сделайте резервную копию важных файлов", "Повторите проверку", "Не запускайте тяжёлую проверку до резервного копирования"], true),

            Rule("hdd_reallocated_sectors", "Диск заменял повреждённые участки", ["HDD", "SataSSD", "SSD"], Condition("ReallocatedSectors", ">", 0), DiagnosticRiskLevel.HighRisk,
                "Диск уже заменял повреждённые участки резервными. Если число растёт, это плохой признак.",
                ["Физический износ", "Повреждение поверхности HDD", "Проблемы NAND-блоков у SSD"],
                ["Сделайте резервную копию", "Повторите проверку", "Смотрите, растёт ли число"], true),

            Rule("uncorrectable_errors", "Есть неисправимые ошибки", ["HDD", "SataSSD", "SSD"], Condition("UncorrectableErrors", ">", 0), DiagnosticRiskLevel.BackupNow,
                "Диск встречал ошибки, которые не смог исправить. Это серьёзный признак.",
                ["Физическое повреждение", "Проблемы поверхности или памяти", "Сбой питания"],
                ["Срочно сохраните данные", "Повторите проверку после резервной копии"], true),

            Rule("nvme_media_errors", "NVMe зафиксировал ошибки данных", ["NvmeSSD"], Condition("MediaErrors", ">", 0), DiagnosticRiskLevel.BackupNow,
                "NVMe-диск зафиксировал ошибки данных. Это серьёзный признак.",
                ["Проблема памяти SSD", "Сбой контроллера", "Проблемы питания"],
                ["Сделайте резервную копию", "Повторите проверку", "Смотрите, растёт ли число ошибок"], true),

            Rule("smart_critical_warning", "Диск сообщает о критической проблеме", ["HDD", "SataSSD", "SSD", "NvmeSSD", "USB", "Unknown"], Condition("SmartPassed", "==", 0), DiagnosticRiskLevel.BackupNow,
                "Сам диск сообщает о критической проблеме.",
                ["Физическая проблема", "Критическое состояние SSD/HDD", "Ошибка контроллера"],
                ["Срочно сохраните данные", "Проверьте диск smartctl", "Не откладывайте замену"], true),

            Rule("ssd_wear_high", "SSD близок к заявленному ресурсу", ["SataSSD", "SSD", "NvmeSSD"], Condition("WearPercentage", ">=", 80), DiagnosticRiskLevel.HighRisk,
                "SSD уже использовал большую часть заявленного ресурса. Это не точная дата поломки, но риск становится выше.",
                ["Большой объём записи", "Торренты", "Видео, кэш или логи", "Диск давно используется"],
                ["Посмотрите темп записи", "Найдите программы, которые много пишут", "Планируйте замену"], true),

            Rule("tbw_exceeded", "TBW превышен", ["SataSSD", "SSD", "NvmeSSD"], Condition("TbwUsedPercent", ">=", 100), DiagnosticRiskLevel.HighRisk,
                "Диск превысил заявленный производителем ресурс записи. Он может работать дальше, но гарантийный ресурс уже исчерпан.",
                ["Большой объём записи", "Диск долго использовался"],
                ["Сделайте резервную копию", "Планируйте замену", "Снизьте лишнюю запись"], true),

            Rule("high_daily_writes", "На диск слишком много записывается", ["SataSSD", "SSD", "NvmeSSD", "HDD"], Condition("DailyWriteGb", ">=", 100), DiagnosticRiskLevel.Observation,
                "На диск записывается необычно много данных. Это может быстрее расходовать ресурс SSD.",
                ["Торренты", "Запись видео", "Игры", "Обновления", "Кэш браузера", "Файл подкачки", "Логи программы"],
                ["Посмотрите вкладку процессов", "Закройте подозрительную программу", "Повторите проверку"]),

            Rule("unsafe_shutdowns_growing", "Компьютер часто выключался некорректно", ["SataSSD", "SSD", "NvmeSSD"], Condition("UnsafeShutdowns", "increased", null, true), DiagnosticRiskLevel.Attention,
                "Компьютер или диск выключался некорректно.",
                ["Отключение электричества", "Зависание Windows", "Удержание кнопки питания", "Проблема с БП", "Сбой драйвера"],
                ["Проверьте питание", "Проверьте стабильность системы", "Посмотрите журнал событий Windows", "Повторите проверку"]),

            Rule("low_free_space_ssd", "На SSD мало свободного места", ["SataSSD", "SSD", "NvmeSSD"], Condition("FreeSpacePercent", "<", 10), DiagnosticRiskLevel.Observation,
                "На SSD осталось мало свободного места. Это может снижать скорость и мешать нормальному распределению записи.",
                ["Диск заполнен файлами", "Кэш", "Игры", "Временные файлы"],
                ["Освободите место", "Перенесите крупные файлы", "Повторите проверку"]),

            Rule("smart_not_available", "Данных о состоянии диска недостаточно", ["HDD", "SataSSD", "SSD", "NvmeSSD", "USB", "Unknown"], Condition("TemperatureCelsius", "missing", null), DiagnosticRiskLevel.NotEnoughData,
                "Программа не смогла получить полную информацию о здоровье диска. Это не значит, что диск плохой или хороший — данных недостаточно.",
                ["Нет smartctl", "Недостаточно прав", "USB-переходник скрывает SMART", "Диск не поддерживает часть данных"],
                ["Запустите от имени администратора", "Укажите smartctl", "Проверьте поддержку USB-переходника"])
        ];
    }

    private static DiagnosticRule Rule(string id, string simpleTitle, List<string> diskTypes, DiagnosticSymptomCondition symptom, DiagnosticRiskLevel risk, string explanation, List<string> causes, List<string> checks, bool backup = false)
    {
        return new DiagnosticRule
        {
            Id = id,
            Title = id,
            SimpleTitle = simpleTitle,
            DiskTypes = diskTypes,
            Symptoms = [symptom],
            RiskLevel = risk,
            UserExplanation = explanation,
            TechnicalExplanation = $"{symptom.Metric} {symptom.Operator} {symptom.Value?.ToString() ?? "предыдущее значение"}",
            PossibleCauses = causes,
            SuggestedChecks = checks,
            WhatMeansImprovement = "Показатель улучшился или перестал ухудшаться.",
            WhatMeansWorse = "Показатель продолжает ухудшаться.",
            ConclusionIfImproved = "Похоже, ситуация стала лучше. Нужно продолжить наблюдение.",
            ConclusionIfNotChanged = "Состояние не изменилось. Нужно продолжить наблюдение.",
            ConclusionIfWorse = "Состояние ухудшилось. Риск повышен.",
            Recommendations = checks,
            TechnicalDetails = [$"{symptom.Metric} {symptom.Operator} {symptom.Value}"],
            RequiresBackupFirst = backup,
            CanRunAutomaticAction = false,
            UserActionExamples = ["Я сделал резервную копию", "Я улучшил охлаждение", "Я заменил SATA-кабель", "Другое"]
        };
    }

    private static DiagnosticSymptomCondition Condition(string metric, string op, decimal? value, bool previous = false)
    {
        return new DiagnosticSymptomCondition
        {
            Metric = metric,
            Operator = op,
            Value = value,
            CompareWithPreviousSnapshot = previous
        };
    }
}
