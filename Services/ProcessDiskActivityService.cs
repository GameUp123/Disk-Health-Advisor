using System.Text.Json;
using DiskHealthAdvisor.Models;

namespace DiskHealthAdvisor.Services;

public sealed class ProcessDiskActivityService
{
    private readonly PowerShellJsonRunner _runner;
    private readonly AppLogger _logger;

    public ProcessDiskActivityService(PowerShellJsonRunner runner, AppLogger logger)
    {
        _runner = runner;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ProcessDiskActivity>> GetActivityAsync()
    {
        var output = await _runner.RunAsync(Script);
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(output);
            var result = new List<ProcessDiskActivity>();
            foreach (var item in Enumerate(document.RootElement))
            {
                var name = GetString(item, "Name") ?? "Unknown";
                result.Add(new ProcessDiskActivity
                {
                    ProcessName = name,
                    ProcessId = GetInt(item, "Id") ?? 0,
                    WrittenBytesPerSecond = GetUlong(item, "WriteBytesPerSecond"),
                    ReadBytesPerSecond = GetUlong(item, "ReadBytesPerSecond"),
                    Comment = BuildComment(name)
                });
            }

            return result;
        }
        catch (Exception ex)
        {
            await _logger.LogAsync("Не удалось получить активность процессов.", ex);
            return [];
        }
    }

    private static string BuildComment(string processName)
    {
        var name = NormalizeProcessName(processName);

        if (name.Contains("nvcontainer") || name.Contains("nvidia container"))
        {
            return "NVIDIA Container: служба драйвера/NVIDIA App/GeForce Experience. Обычно пишет логи, кэш, данные оверлея, фильтров, ShadowPlay/Instant Replay или обновлений драйвера. Норма: всплески при запуске игры, записи видео, обновлении драйвера. Странно: держит много МБ/с долго, когда игр, записи и оверлея нет.";
        }

        if (name.Contains("discord"))
        {
            return "Discord: десктопное приложение на Electron/Chromium. Пишет кэш картинок, эмодзи, видео, аватарок, логи, базу профиля и данные звонков/стримов. Норма: КБ/с и короткие всплески при чатах, голосе, стриме, обновлении. Странно: постоянные МБ/с в простое.";
        }

        if (name.Contains("codex"))
        {
            return "Codex: локальный помощник разработки. Пишет измененные файлы проекта, временные файлы, логи, результаты сборки и иногда кэши зависимостей. Норма: запись во время правок, поиска, сборки или запуска тестов. Странно: заметно пишет, когда Codex ничего не делает.";
        }

        if (IsBrowser(name))
        {
            return "Браузер: пишет HTTP-кэш, cookies, историю, IndexedDB/Service Worker, профили расширений, загрузки и кэш видео. Норма: активные вкладки, YouTube/стримы, обновления, загрузки. Странно: высокая постоянная запись в простое; проверьте вкладки, расширения и загрузки.";
        }

        if (name.Contains("steam"))
        {
            return "Steam: пишет загрузки и обновления игр, распаковку, проверку файлов, Workshop, cloud sync и shader pre-caching. Норма: высокая запись при обновлениях, установке, проверке или первом запуске игры. Странно: большая запись часами без загрузок и без игры.";
        }

        if (IsGameLauncher(name))
        {
            return "Игровой лаунчер: обычно пишет загрузки, обновления, распаковку, проверку файлов, облачные сохранения и кэш магазина. Норма: во время установки/обновления/проверки. Странно: постоянная запись в простое, когда лаунчер ничего не качает.";
        }

        if (name.Contains("qbittorrent") || name.Contains("torrent") || name.Contains("transmission") || name.Contains("utorrent"))
        {
            return "Торрент-клиент: пишет скачиваемые части, докачку, раздачу, служебные файлы и иногда пересчет/проверку. Норма: высокая запись при активных загрузках. Для SSD лучше ограничить скорость/кэш или перенести папку загрузок на другой диск.";
        }

        if (name.Equals("system", StringComparison.OrdinalIgnoreCase) || name.Contains("ntoskrnl"))
        {
            return "System: это ядро Windows, поэтому за ним могут скрываться Windows Update, файл подкачки, кэш файловой системы, NTFS-журнал, драйверы, копирование и отложенная запись. Норма: короткие всплески. Странно: постоянная высокая запись; смотрите обновления, индексатор, антивирус и pagefile.";
        }

        if (name.Contains("msmpeng") || name.Contains("securityhealth") || name.Contains("defender"))
        {
            return "Microsoft Defender/антивирус: пишет журналы, карантин, базу сигнатур и служебные данные при проверке файлов. Норма: всплески после скачивания, распаковки, обновления или плановой проверки. Странно: долго грузит диск каждый день; проверьте расписание и исключения для папок сборки/игр.";
        }

        if (name.Contains("searchindexer") || name.Contains("searchhost"))
        {
            return "Индексатор Windows Search: пишет поисковый индекс после установки программ, распаковки файлов или больших изменений в папках. Норма: временно после изменений. Странно: постоянно индексирует одну и ту же папку; проверьте параметры индексирования.";
        }

        if (name.Contains("onedrive") || name.Contains("googledrive") || name.Contains("dropbox") || name.Contains("yandexdisk"))
        {
            return "Облачная синхронизация: пишет локальную базу, журнал синхронизации, временные файлы и скачанные/измененные документы. Норма: при синхронизации. Странно: один файл постоянно перезаписывается или синк идет по кругу.";
        }

        if (name.Contains("explorer") || name.Contains("7z") || name.Contains("winrar") || name.Contains("nanazip"))
        {
            return "Файловые операции: копирование, распаковка, удаление, создание миниатюр и обновление папок. Норма: во время архивации/копирования. Странно: высокая запись без видимой операции; проверьте зависшее копирование или распаковку.";
        }

        if (name.Contains("obs") || name.Contains("streamlabs") || name.Contains("bandicam") || name.Contains("action") || name.Contains("medal"))
        {
            return "Запись/стриминг: пишет видеофайл, replay buffer, кэш сцен и логи. Норма: высокая запись во время записи или буфера повтора. Для SSD лучше выбирать отдельный диск/папку записи и контролировать битрейт.";
        }

        if (name.Contains("teams") || name.Contains("zoom") || name.Contains("telegram") || name.Contains("whatsapp"))
        {
            return "Мессенджер/звонки: пишет кэш медиа, вложения, логи, базу сообщений и данные звонков. Норма: КБ/с и всплески при загрузке медиа или звонке. Странно: постоянная запись в простое; проверьте автозагрузку медиа и кэш.";
        }

        if (IsIdeOrEditor(name))
        {
            return "IDE/редактор: пишет индексы проекта, кэши, логи, временные файлы, git-данные и результаты расширений. Норма: после открытия проекта, поиска, сборки. Странно: постоянная запись в простое; проверьте расширения, watcher и папки build/cache.";
        }

        return "Неизвестный процесс: показаны счетчики Windows по общей записи процесса, не точная привязка к физическому диску. Норма зависит от программы. Если запись держится долго, проверьте путь процесса, чем он занят, и совпадает ли время с ростом записи нужного диска.";
    }

    private static string NormalizeProcessName(string processName)
    {
        var name = processName.Trim().ToLowerInvariant();
        var hashIndex = name.IndexOf('#');
        return hashIndex > 0 ? name[..hashIndex] : name;
    }

    private static bool IsBrowser(string name)
    {
        return name.Contains("chrome") ||
               name.Contains("msedge") ||
               name.Contains("edge") ||
               name.Contains("firefox") ||
               name.Contains("browser") ||
               name.Contains("yandex") ||
               name.Contains("opera") ||
               name.Contains("brave") ||
               name.Contains("vivaldi");
    }

    private static bool IsGameLauncher(string name)
    {
        return name.Contains("epicgameslauncher") ||
               name.Contains("epicwebhelper") ||
               name.Contains("eadesktop") ||
               name.Contains("battle.net") ||
               name.Contains("xbox") ||
               name.Contains("ubisoftconnect") ||
               name.Contains("gog") ||
               name.Contains("riotclient");
    }

    private static bool IsIdeOrEditor(string name)
    {
        return name.Contains("devenv") ||
               name.Contains("rider") ||
               name.Contains("webstorm") ||
               name.Contains("pycharm") ||
               name.Contains("idea") ||
               name.Contains("code");
    }

    private static string? GetString(JsonElement item, string name)
    {
        return item.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.ToString()
            : null;
    }

    private static IEnumerable<JsonElement> Enumerate(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.Array ? element.EnumerateArray() : [element];
    }

    private static int? GetInt(JsonElement item, string name)
    {
        return item.TryGetProperty(name, out var value) && int.TryParse(value.ToString(), out var parsed)
            ? parsed
            : null;
    }

    private static ulong? GetUlong(JsonElement item, string name)
    {
        return item.TryGetProperty(name, out var value) && ulong.TryParse(value.ToString(), out var parsed)
            ? parsed
            : null;
    }

    private const string Script = """
$ErrorActionPreference = 'SilentlyContinue'
$rows = Get-CimInstance -ClassName Win32_PerfFormattedData_PerfProc_Process |
    Where-Object { $_.Name -and $_.Name -ne '_Total' -and $_.Name -ne 'Idle' } |
    Sort-Object -Property IOWriteBytesPersec -Descending |
    Select-Object -First 30 @{n='Name';e={$_.Name}}, @{n='Id';e={$_.IDProcess}}, @{n='WriteBytesPerSecond';e={$_.IOWriteBytesPersec}}, @{n='ReadBytesPerSecond';e={$_.IOReadBytesPersec}}

@($rows) | ConvertTo-Json -Depth 4 -Compress
""";
}
