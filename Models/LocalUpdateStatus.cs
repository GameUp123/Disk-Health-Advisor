namespace DiskHealthAdvisor.Models;

public sealed class LocalUpdateStatus
{
    public bool IsSourceValid { get; init; }
    public bool IsSameDirectory { get; init; }
    public bool HasNewerBuild { get; init; }
    public string SourceDirectory { get; init; } = "";
    public string SourceExePath { get; init; } = "";
    public string CurrentExePath { get; init; } = "";
    public string SourceBuildText { get; init; } = "Нет данных";
    public string CurrentBuildText { get; init; } = "Нет данных";
    public string Summary { get; init; } = "";
    public string Problem { get; init; } = "";

    public bool CanApply => IsSourceValid && !IsSameDirectory;
}
