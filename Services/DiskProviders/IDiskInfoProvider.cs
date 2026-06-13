using DiskHealthAdvisor.Models;

namespace DiskHealthAdvisor.Services.DiskProviders;

public interface IDiskInfoProvider
{
    Task<IReadOnlyList<DiskInfo>> GetDisksAsync();
}
