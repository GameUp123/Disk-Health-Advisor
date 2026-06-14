using System.Diagnostics;
using System.Globalization;
using System.Text;
using DiskHealthAdvisor.Models;

namespace DiskHealthAdvisor.Services;

public sealed class LocalUpdateService
{
    private const string AppExeName = "DiskHealthAdvisor.exe";

    private readonly ApplicationPaths _paths;
    private readonly AppLogger _logger;

    public LocalUpdateService(ApplicationPaths paths, AppLogger logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public LocalUpdateStatus Inspect(string? sourceDirectory)
    {
        var currentExePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, AppExeName);
        var currentDirectory = NormalizeDirectory(AppContext.BaseDirectory);
        var currentBuild = File.Exists(currentExePath) ? File.GetLastWriteTime(currentExePath) : (DateTime?)null;

        if (string.IsNullOrWhiteSpace(sourceDirectory))
        {
            return new LocalUpdateStatus
            {
                CurrentExePath = currentExePath,
                CurrentBuildText = FormatBuild(currentBuild),
                Summary = "Папка свежей сборки еще не выбрана.",
                Problem = "Нажмите «Выбрать папку» и укажите publish-папку, где лежит DiskHealthAdvisor.exe."
            };
        }

        var source = NormalizeDirectory(sourceDirectory);
        var sourceExePath = Path.Combine(source, AppExeName);
        var sameDirectory = string.Equals(source, currentDirectory, StringComparison.OrdinalIgnoreCase);
        var sourceBuild = File.Exists(sourceExePath) ? File.GetLastWriteTime(sourceExePath) : (DateTime?)null;
        var sourceValid = sourceBuild is not null;
        var newer = sourceBuild is not null && currentBuild is not null && sourceBuild > currentBuild.Value.AddSeconds(2);

        var problem = "";
        if (!Directory.Exists(source))
        {
            problem = "Такой папки нет. Выберите существующую папку со сборкой.";
        }
        else if (!sourceValid)
        {
            problem = "В этой папке не найден DiskHealthAdvisor.exe. Нужна папка результата dotnet publish.";
        }
        else if (sameDirectory)
        {
            problem = "Это текущая папка программы. Для обновления нужна другая папка со свежей сборкой.";
        }

        var summary = sourceValid
            ? newer
                ? "Найдена сборка свежее текущей. Можно применить локальное обновление."
                : "Сборка найдена, но она не выглядит свежее текущей. Применить можно, если вы уверены."
            : "Свежая сборка пока не найдена.";

        return new LocalUpdateStatus
        {
            IsSourceValid = sourceValid,
            IsSameDirectory = sameDirectory,
            HasNewerBuild = newer,
            SourceDirectory = source,
            SourceExePath = sourceExePath,
            CurrentExePath = currentExePath,
            SourceBuildText = FormatBuild(sourceBuild),
            CurrentBuildText = FormatBuild(currentBuild),
            Summary = summary,
            Problem = problem
        };
    }

    public async Task StartApplyAsync(LocalUpdateStatus status)
    {
        if (!status.CanApply)
        {
            throw new InvalidOperationException("Local update source is not ready.");
        }

        var targetDirectory = NormalizeDirectory(AppContext.BaseDirectory);
        var targetExePath = Environment.ProcessPath ?? Path.Combine(targetDirectory, AppExeName);
        var scriptDirectory = Path.Combine(_paths.UserDataDirectory, "Updates");
        Directory.CreateDirectory(scriptDirectory);

        var scriptPath = Path.Combine(scriptDirectory, $"apply-local-update-{DateTime.Now:yyyyMMdd-HHmmss}.ps1");
        var logPath = Path.Combine(scriptDirectory, "last-local-update.log");
        await File.WriteAllTextAsync(scriptPath, BuildUpdaterScript(), new UTF8Encoding(false));

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = string.Join(
                " ",
                "-NoProfile",
                "-ExecutionPolicy Bypass",
                "-File",
                QuoteArgument(scriptPath),
                "-Source",
                QuoteArgument(status.SourceDirectory),
                "-Target",
                QuoteArgument(targetDirectory),
                "-Exe",
                QuoteArgument(targetExePath),
                "-Pid",
                Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
                "-Log",
                QuoteArgument(logPath)),
            UseShellExecute = true,
            WorkingDirectory = scriptDirectory
        };

        if (!CanWriteToDirectory(targetDirectory))
        {
            startInfo.Verb = "runas";
        }

        Process.Start(startInfo);
        await _logger.LogAsync($"Started local update from '{status.SourceDirectory}' to '{targetDirectory}'.");
    }

    private static string BuildUpdaterScript() =>
        """
        param(
            [Parameter(Mandatory=$true)][string]$Source,
            [Parameter(Mandatory=$true)][string]$Target,
            [Parameter(Mandatory=$true)][string]$Exe,
            [Parameter(Mandatory=$true)][int]$Pid,
            [Parameter(Mandatory=$true)][string]$Log
        )

        $ErrorActionPreference = 'Stop'
        Start-Transcript -Path $Log -Force | Out-Null
        try {
            if (Get-Process -Id $Pid -ErrorAction SilentlyContinue) {
                Wait-Process -Id $Pid -ErrorAction SilentlyContinue
            }

            Start-Sleep -Milliseconds 400

            if (-not (Test-Path -LiteralPath (Join-Path $Source 'DiskHealthAdvisor.exe'))) {
                throw "Source folder does not contain DiskHealthAdvisor.exe: $Source"
            }

            New-Item -ItemType Directory -Path $Target -Force | Out-Null
            Get-ChildItem -LiteralPath $Source -Force | ForEach-Object {
                $destination = Join-Path $Target $_.Name
                Copy-Item -LiteralPath $_.FullName -Destination $destination -Recurse -Force
            }

            Start-Process -FilePath $Exe
        }
        finally {
            Stop-Transcript | Out-Null
        }
        """;

    private static bool CanWriteToDirectory(string directory)
    {
        try
        {
            var probe = Path.Combine(directory, $".write-test-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "test");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeDirectory(string directory) =>
        Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string QuoteArgument(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

    private static string FormatBuild(DateTime? value) =>
        value is null ? "Нет данных" : value.Value.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture);
}
