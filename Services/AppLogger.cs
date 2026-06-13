namespace DiskHealthAdvisor.Services;

public sealed class AppLogger
{
    private readonly string _logFile;

    public AppLogger(ApplicationPaths paths)
    {
        _logFile = Path.Combine(paths.LogsDirectory, "app.log");
    }

    public async Task LogAsync(string message, Exception? exception = null)
    {
        try
        {
            var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {message}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            await File.AppendAllTextAsync(_logFile, line + Environment.NewLine);
        }
        catch
        {
            // Logging must never break diagnostics.
        }
    }
}
