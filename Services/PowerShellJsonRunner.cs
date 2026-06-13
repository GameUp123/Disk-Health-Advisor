using System.Diagnostics;
using System.Text;

namespace DiskHealthAdvisor.Services;

public sealed class PowerShellJsonRunner
{
    private readonly AppLogger _logger;

    public PowerShellJsonRunner(AppLogger logger)
    {
        _logger = logger;
    }

    public async Task<string?> RunAsync(string script, CancellationToken cancellationToken = default)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {Encode(script)}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            var errors = await errorTask;
            if (!string.IsNullOrWhiteSpace(errors))
            {
                await _logger.LogAsync("PowerShell вернул предупреждение/ошибку: " + errors.Trim());
            }

            return await outputTask;
        }
        catch (Exception ex)
        {
            await _logger.LogAsync("Не удалось выполнить PowerShell-запрос.", ex);
            return null;
        }
    }

    private static string Encode(string script)
    {
        return Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
    }
}
