using System.Diagnostics;

namespace DiskHealthAdvisor.Services;

public sealed class SmartCtlBootstrapService
{
    private readonly AppLogger _logger;

    public SmartCtlBootstrapService(AppLogger logger)
    {
        _logger = logger;
    }

    public string? Find(string? configuredPath) => SmartCtlLocator.Find(configuredPath);

    public async Task<(bool Success, string Message)> InstallWithWingetAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "winget.exe",
                Arguments = "install --id smartmontools.smartmontools -e --accept-package-agreements --accept-source-agreements",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return (false, "Не удалось запустить winget.");
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var output = await outputTask;
            var error = await errorTask;
            if (!string.IsNullOrWhiteSpace(output))
            {
                await _logger.LogAsync("winget smartmontools output: " + output.Trim());
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                await _logger.LogAsync("winget smartmontools error: " + error.Trim());
            }

            var installedPath = SmartCtlLocator.Find(null);
            if (process.ExitCode == 0 && installedPath is not null)
            {
                return (true, "smartctl.exe установлен и найден: " + installedPath);
            }

            if (installedPath is not null)
            {
                return (true, "smartctl.exe найден: " + installedPath);
            }

            return (false, "winget завершился, но smartctl.exe не найден. Можно установить smartmontools вручную и выбрать путь в настройках.");
        }
        catch (Exception ex)
        {
            await _logger.LogAsync("Не удалось установить smartmontools через winget.", ex);
            return (false, "Не удалось установить smartmontools через winget. Возможно, winget недоступен или нет доступа к интернету.");
        }
    }
}
