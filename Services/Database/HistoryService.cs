using DiskHealthAdvisor.Models;

namespace DiskHealthAdvisor.Services.Database;

public sealed class HistoryService
{
    private readonly ApplicationPaths _paths;
    private readonly JsonFileStore<List<DiskSnapshot>> _store;

    public HistoryService(ApplicationPaths paths, AppLogger logger)
    {
        _paths = paths;
        _store = new JsonFileStore<List<DiskSnapshot>>(logger);
    }

    public Task<List<DiskSnapshot>> LoadAsync() => _store.LoadAsync(_paths.HistoryFile);

    public async Task AddSnapshotsAsync(IEnumerable<DiskSnapshot> snapshots)
    {
        var history = await LoadAsync();
        history.AddRange(snapshots);
        await _store.SaveAsync(_paths.HistoryFile, history);
    }

    public static DiskSnapshot CreateSnapshot(DiskInfo disk, HealthReport report)
    {
        return new DiskSnapshot
        {
            Timestamp = DateTimeOffset.Now,
            DiskIdentity = disk.Identity,
            Model = disk.Model,
            Serial = disk.Serial,
            TemperatureCelsius = disk.TemperatureCelsius,
            PowerOnHours = disk.PowerOnHours,
            TotalBytesWritten = disk.TotalBytesWritten,
            TotalBytesRead = disk.TotalBytesRead,
            WearPercentage = disk.WearPercentage,
            UnsafeShutdowns = disk.UnsafeShutdowns,
            MediaErrors = disk.MediaErrors,
            ReallocatedSectors = disk.ReallocatedSectors,
            CurrentPendingSectors = disk.CurrentPendingSectors,
            UncorrectableErrors = disk.UncorrectableErrors,
            CrcErrors = disk.CrcErrors,
            HealthLevel = report.Level
        };
    }
}
