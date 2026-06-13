namespace DiskHealthAdvisor.Models;

public sealed class AppSettings
{
    public string? SmartCtlPath { get; set; }
    public bool ExpertMode { get; set; }
    public string ThemeName { get; set; } = "Океан";
    public Dictionary<string, string> DiskProfiles { get; set; } = [];
}
