using DiskHealthAdvisor.Helpers;
using DiskHealthAdvisor.Models;

namespace DiskHealthAdvisor.Services.HealthAnalysis;

public sealed class SimpleTextFormatter
{
    public string DescribeDetectedProblem(DiskInfo disk, DiagnosticRule rule)
    {
        var diskName = string.IsNullOrWhiteSpace(disk.Model) ? "Выбранный диск" : disk.Model;
        return rule.Id switch
        {
            "nvme_overheat" or "hdd_overheat" => $"{diskName} нагрелся до {FormatHelper.Optional(disk.TemperatureCelsius, "°C")}. Это выше обычного уровня.",
            "crc_errors_growing" => "Обнаружен рост ошибок передачи данных. Часто причина в SATA-кабеле или порте.",
            "hdd_pending_sectors" => $"Диск нашёл {disk.CurrentPendingSectors} нестабильных участков, которые не смог нормально прочитать.",
            "hdd_reallocated_sectors" => $"Диск уже заменял повреждённые участки резервными. Сейчас таких участков: {disk.ReallocatedSectors}.",
            "uncorrectable_errors" => "Диск встречал ошибки, которые не смог исправить самостоятельно.",
            "nvme_media_errors" => $"NVMe-диск сообщил об ошибках данных. Количество: {disk.MediaErrors}.",
            "smart_critical_warning" => "Сам диск сообщает о критической проблеме или SMART-статус не пройден.",
            "ssd_wear_high" => $"SSD использовал значительную часть ресурса. Текущий показатель износа: {FormatHelper.Optional(disk.WearPercentage, "%")}.",
            "tbw_exceeded" => "Диск приблизился к заявленному ресурсу записи или превысил его.",
            "high_daily_writes" => "На диск за последнее время записывается необычно много данных.",
            "unsafe_shutdowns_growing" => "Количество некорректных выключений увеличилось.",
            "low_free_space_ssd" => "На SSD осталось мало свободного места.",
            "smart_not_available" => "Программа не смогла получить достаточно данных о здоровье диска.",
            _ => rule.UserExplanation
        };
    }

    public string SnapshotSummary(DiskSnapshot? snapshot)
    {
        if (snapshot is null) return "Нет снимка.";
        return $"Температура: {FormatHelper.Optional(snapshot.TemperatureCelsius, "°C")}; " +
               $"записано: {FormatHelper.Terabytes(snapshot.TotalBytesWritten)}; " +
               $"износ: {FormatHelper.Optional(snapshot.WearPercentage, "%")}; " +
               $"нестабильные участки: {FormatHelper.Optional(snapshot.CurrentPendingSectors)}; " +
               $"переназначенные участки: {FormatHelper.Optional(snapshot.ReallocatedSectors)}; " +
               $"CRC: {FormatHelper.Optional(snapshot.CrcErrors)}.";
    }
}
