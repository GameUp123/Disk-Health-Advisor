using DiskHealthAdvisor.Models;

namespace DiskHealthAdvisor.Services.Database;

public sealed class InvestigationRepository
{
    private readonly ApplicationPaths _paths;
    private readonly JsonFileStore<List<DiskInvestigation>> _store;
    private readonly string _file;

    public InvestigationRepository(ApplicationPaths paths, AppLogger logger)
    {
        _paths = paths;
        _store = new JsonFileStore<List<DiskInvestigation>>(logger);
        _file = Path.Combine(_paths.UserDataDirectory, "investigations.json");
    }

    public Task<List<DiskInvestigation>> LoadAsync() => _store.LoadAsync(_file);

    public Task SaveAsync(List<DiskInvestigation> investigations) => _store.SaveAsync(_file, investigations);
}
