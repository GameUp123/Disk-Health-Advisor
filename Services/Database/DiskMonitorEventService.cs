using DiskHealthAdvisor.Models;

namespace DiskHealthAdvisor.Services.Database;

public sealed class DiskMonitorEventService
{
    private readonly ApplicationPaths _paths;
    private readonly JsonFileStore<List<DiskMonitorEvent>> _store;

    public DiskMonitorEventService(ApplicationPaths paths, AppLogger logger)
    {
        _paths = paths;
        _store = new JsonFileStore<List<DiskMonitorEvent>>(logger);
    }

    public async Task<List<DiskMonitorEvent>> LoadTodayAsync()
    {
        var today = DateTimeOffset.Now.Date;
        return (await _store.LoadAsync(_paths.DailyDiskEventsFile))
            .Where(e => e.Timestamp.ToLocalTime().Date == today)
            .OrderByDescending(e => e.Timestamp)
            .ToList();
    }

    public async Task AddAsync(IEnumerable<DiskMonitorEvent> events)
    {
        var newEvents = events.ToList();
        if (newEvents.Count == 0)
        {
            return;
        }

        var cutoff = DateTimeOffset.Now.AddDays(-7);
        var existing = (await _store.LoadAsync(_paths.DailyDiskEventsFile))
            .Where(e => e.Timestamp >= cutoff)
            .ToList();

        existing.AddRange(newEvents);
        await _store.SaveAsync(_paths.DailyDiskEventsFile, existing.OrderBy(e => e.Timestamp).ToList());
    }
}
