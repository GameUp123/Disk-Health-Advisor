namespace DiskHealthAdvisor.Models;

public sealed class InvestigationUserAction
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public string ActionTitle { get; set; } = "";
    public string? UserComment { get; set; }
}
