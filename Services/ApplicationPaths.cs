namespace DiskHealthAdvisor.Services;

public sealed class ApplicationPaths
{
    public string UserDataDirectory { get; }
    public string LogsDirectory { get; }
    public string SettingsFile { get; }
    public string HistoryFile { get; }
    public string DailyDiskEventsFile { get; }
    public string UserTbwDatabaseFile { get; }
    public string BundledTbwDatabaseFile { get; }

    public ApplicationPaths()
    {
        UserDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Disk Health Advisor");
        LogsDirectory = Path.Combine(UserDataDirectory, "Logs");
        SettingsFile = Path.Combine(UserDataDirectory, "settings.json");
        HistoryFile = Path.Combine(UserDataDirectory, "history.json");
        DailyDiskEventsFile = Path.Combine(UserDataDirectory, "daily_disk_events.json");
        UserTbwDatabaseFile = Path.Combine(UserDataDirectory, "ssd_tbw_database.json");
        BundledTbwDatabaseFile = Path.Combine(AppContext.BaseDirectory, "Data", "ssd_tbw_database.json");

        Directory.CreateDirectory(UserDataDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}
