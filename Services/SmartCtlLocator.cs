namespace DiskHealthAdvisor.Services;

public static class SmartCtlLocator
{
    public static string? Find(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        var local = Path.Combine(AppContext.BaseDirectory, "Tools", "smartctl", "smartctl.exe");
        if (File.Exists(local))
        {
            return local;
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var commonPaths = new[]
        {
            Path.Combine(programFiles, "smartmontools", "bin", "smartctl.exe"),
            Path.Combine(programFiles, "smartmontools", "smartctl.exe"),
            Path.Combine(programFilesX86, "smartmontools", "bin", "smartctl.exe"),
            Path.Combine(programFilesX86, "smartmontools", "smartctl.exe")
        };

        foreach (var path in commonPaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return FindInPath();
    }

    private static string? FindInPath()
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathVariable))
        {
            return null;
        }

        foreach (var directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), "smartctl.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // Ignore broken PATH entries.
            }
        }

        return null;
    }
}
