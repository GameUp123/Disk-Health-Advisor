using DiskHealthAdvisor.Models;

namespace DiskHealthAdvisor.Services.Database;

public sealed class SsdTbwDatabaseService
{
    private readonly ApplicationPaths _paths;
    private readonly JsonFileStore<List<SsdTbwRecord>> _store;

    public SsdTbwDatabaseService(ApplicationPaths paths, AppLogger logger)
    {
        _paths = paths;
        _store = new JsonFileStore<List<SsdTbwRecord>>(logger);
    }

    public async Task<List<SsdTbwRecord>> LoadAsync()
    {
        var records = new List<SsdTbwRecord>();
        records.AddRange(await _store.LoadAsync(_paths.BundledTbwDatabaseFile));
        records.AddRange(await _store.LoadAsync(_paths.UserTbwDatabaseFile));
        return records
            .Where(r => !string.IsNullOrWhiteSpace(r.Model) && r.Tbw > 0)
            .GroupBy(r => $"{Normalize(r.Model)}::{r.CapacityGb?.ToString() ?? ""}")
            .Select(g => g.Last())
            .OrderBy(r => r.Model)
            .ThenBy(r => r.CapacityGb ?? int.MaxValue)
            .ToList();
    }

    public async Task<SsdTbwRecord?> FindForDiskAsync(DiskInfo disk)
    {
        var records = await LoadAsync();
        var diskModel = Normalize(disk.Model);
        if (string.IsNullOrWhiteSpace(diskModel))
        {
            return null;
        }

        var diskCapacityGb = CapacityGb(disk);
        return records
            .Select(r => new
            {
                Record = r,
                Score = ScoreMatch(diskModel, diskCapacityGb, r),
                CapacityDistance = CapacityDistance(diskCapacityGb, r.CapacityGb)
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.CapacityDistance)
            .Select(x => x.Record)
            .FirstOrDefault();
    }

    public async Task AddManualAsync(DiskInfo disk, decimal tbw)
    {
        await AddRecordAsync(disk, new SsdTbwRecord
        {
            Tbw = tbw,
            Source = "manual",
            Comment = "Добавлено пользователем"
        });
    }

    public async Task AddOnlineCandidateAsync(DiskInfo disk, OnlineTbwCandidate candidate)
    {
        await AddRecordAsync(disk, new SsdTbwRecord
        {
            Model = disk.Model ?? "",
            CapacityGb = CapacityGb(disk) ?? candidate.CapacityGb,
            Tbw = candidate.Tbw,
            WarrantyYears = candidate.WarrantyYears,
            MemoryType = candidate.MemoryType,
            Source = candidate.SourceUrl,
            Comment = $"{candidate.Source}. Найдено как: {candidate.Model}. {candidate.Warning}"
        });
    }

    private async Task AddRecordAsync(DiskInfo disk, SsdTbwRecord record)
    {
        var records = await _store.LoadAsync(_paths.UserTbwDatabaseFile);
        var model = string.IsNullOrWhiteSpace(record.Model) ? disk.Model?.Trim() : record.Model.Trim();
        if (string.IsNullOrWhiteSpace(model) || record.Tbw <= 0)
        {
            return;
        }

        record.Model = model;
        record.CapacityGb ??= CapacityGb(disk);
        records.RemoveAll(r => IsSameRecord(r, record));
        records.Add(record);

        await _store.SaveAsync(_paths.UserTbwDatabaseFile, records);
    }

    private static bool IsSameRecord(SsdTbwRecord left, SsdTbwRecord right)
    {
        if (Normalize(left.Model) != Normalize(right.Model))
        {
            return false;
        }

        return left.CapacityGb == right.CapacityGb || left.CapacityGb is null || right.CapacityGb is null;
    }

    private static int ScoreMatch(string diskModel, int? diskCapacityGb, SsdTbwRecord record)
    {
        var recordModel = Normalize(record.Model);
        var score = 0;

        if (diskModel == recordModel)
        {
            score += 100;
        }
        else if (diskModel.Contains(recordModel, StringComparison.OrdinalIgnoreCase) ||
                 recordModel.Contains(diskModel, StringComparison.OrdinalIgnoreCase))
        {
            score += 80;
        }

        var diskTokens = Tokens(diskModel);
        var recordTokens = Tokens(recordModel);
        var tokenMatches = diskTokens.Count(t => recordTokens.Contains(t, StringComparer.OrdinalIgnoreCase));
        if (tokenMatches < RequiredTokenMatches(diskTokens))
        {
            return score >= 80 ? score : 0;
        }

        score += tokenMatches * 10;

        if (diskCapacityGb is not null && record.CapacityGb is not null)
        {
            var distance = Math.Abs(diskCapacityGb.Value - record.CapacityGb.Value);
            if (distance <= Math.Max(16, diskCapacityGb.Value * 0.08))
            {
                score += 30;
            }
            else if (distance <= Math.Max(64, diskCapacityGb.Value * 0.18))
            {
                score += 10;
            }
            else
            {
                score -= 25;
            }
        }

        return score;
    }

    private static int CapacityDistance(int? diskCapacityGb, int? recordCapacityGb)
    {
        if (diskCapacityGb is null || recordCapacityGb is null)
        {
            return int.MaxValue;
        }

        return Math.Abs(diskCapacityGb.Value - recordCapacityGb.Value);
    }

    private static int? CapacityGb(DiskInfo disk) =>
        disk.SizeBytes is null ? null : (int)Math.Round(disk.SizeBytes.Value / 1_000_000_000d);

    private static int RequiredTokenMatches(string[] tokens)
    {
        if (tokens.Any(t => t.Length >= 7))
        {
            return 1;
        }

        return Math.Min(2, tokens.Length);
    }

    private static string[] Tokens(string? value) => Normalize(value)
        .Replace("-", " ", StringComparison.Ordinal)
        .Replace("_", " ", StringComparison.Ordinal)
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Where(t => t.Length >= 2)
        .Where(t => t is not "SSD" and not "NVME" and not "SATA" and not "M2" and not "PCIE")
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static string Normalize(string? value)
    {
        return string.Join(' ', (value ?? "")
            .Trim()
            .ToUpperInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
