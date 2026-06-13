using DiskHealthAdvisor.Models;

namespace DiskHealthAdvisor.Helpers;

public static class FormatHelper
{
    public static string Bytes(ulong? bytes)
    {
        if (bytes is null)
        {
            return "Нет данных";
        }

        string[] units = ["Б", "КБ", "МБ", "ГБ", "ТБ", "ПБ"];
        var value = (double)bytes.Value;
        var index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }

        return $"{value:0.##} {units[index]}";
    }

    public static string Terabytes(ulong? bytes)
    {
        if (bytes is null)
        {
            return "Нет данных";
        }

        return $"{bytes.Value / 1_000_000_000_000d:0.##} ТБ";
    }

    public static string Optional<T>(T? value, string suffix = "") where T : struct
    {
        return value is null ? "Нет данных" : $"{value}{suffix}";
    }

    public static string OptionalString(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "Нет данных" : value;
    }

    public static string BoolSmart(bool? passed)
    {
        return passed switch
        {
            true => "SMART сообщает: ошибок не найдено",
            false => "SMART сообщает о проблеме",
            _ => "Нет данных"
        };
    }

    public static string MaskSerial(string? serial)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            return "Нет данных";
        }

        var clean = serial.Trim();
        if (clean.Length <= 4)
        {
            return new string('*', clean.Length);
        }

        return $"{clean[..Math.Min(3, clean.Length)]}***{clean[^Math.Min(4, clean.Length)..]}";
    }

    public static string LevelText(HealthLevel level)
    {
        return level switch
        {
            HealthLevel.Good => "Норма",
            HealthLevel.Caution => "Внимание",
            HealthLevel.Warning => "Повышенный риск",
            HealthLevel.Critical => "Срочно скопировать данные",
            _ => "Нет данных для точной оценки"
        };
    }
}
