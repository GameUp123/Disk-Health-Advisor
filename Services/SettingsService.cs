using DiskHealthAdvisor.Models;

namespace DiskHealthAdvisor.Services;

public sealed class SettingsService
{
    private readonly ApplicationPaths _paths;
    private readonly JsonFileStore<AppSettings> _store;

    public SettingsService(ApplicationPaths paths, AppLogger logger)
    {
        _paths = paths;
        _store = new JsonFileStore<AppSettings>(logger);
    }

    public Task<AppSettings> LoadAsync() => _store.LoadAsync(_paths.SettingsFile);

    public Task SaveAsync(AppSettings settings) => _store.SaveAsync(_paths.SettingsFile, settings);
}
