using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using DiskHealthAdvisor.Models;

namespace DiskHealthAdvisor.Services;

public sealed partial class OnlineTbwLookupService
{
    private const string JohnnyLuckyUrl = "https://www.johnnylucky.org/data-storage/ssd-database.html";
    private const int MaxCandidates = 8;

    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(12)
    };
    private readonly AppLogger _logger;

    public OnlineTbwLookupService(AppLogger logger)
    {
        _logger = logger;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("DiskHealthAdvisor/1.0");
    }

    public async Task<IReadOnlyList<OnlineTbwCandidate>> SearchAsync(DiskInfo disk, string? query)
    {
        var searchText = string.IsNullOrWhiteSpace(query) ? disk.Model : query;
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return [];
        }

        var candidates = new List<OnlineTbwCandidate>();

        try
        {
            var html = await _httpClient.GetStringAsync(JohnnyLuckyUrl);
            var text = NormalizeText(WebUtility.HtmlDecode(StripTags(html)));
            candidates.AddRange(SearchTextForTbw(text, searchText, disk, "Johnny Lucky SSD Database", JohnnyLuckyUrl));
        }
        catch (Exception ex)
        {
            await _logger.LogAsync("Не удалось прочитать Johnny Lucky SSD Database для онлайн-поиска TBW.", ex);
        }

        if (candidates.Count < 2)
        {
            candidates.AddRange(await SearchWebSnippetsAsync(searchText, disk));
        }

        return Rank(candidates, searchText);
    }

    private async Task<IReadOnlyList<OnlineTbwCandidate>> SearchWebSnippetsAsync(string query, DiskInfo disk)
    {
        var candidates = new List<OnlineTbwCandidate>();
        var queries = new[]
        {
            $"\"{query}\" TBW SSD",
            $"\"{query}\" endurance TBW",
            $"{query} SSD TBW endurance"
        };

        foreach (var searchQuery in queries)
        {
            try
            {
                var url = "https://duckduckgo.com/html/?q=" + WebUtility.UrlEncode(searchQuery);
                var html = await _httpClient.GetStringAsync(url);
                var text = NormalizeText(WebUtility.HtmlDecode(StripTags(html)));
                candidates.AddRange(SearchTextForTbw(text, query, disk, "Веб-поиск TBW", url));

                if (candidates.Count >= MaxCandidates)
                {
                    break;
                }
            }
            catch (Exception ex)
            {
                await _logger.LogAsync($"Не удалось выполнить веб-поиск TBW: {searchQuery}", ex);
            }
        }

        return candidates;
    }

    private static List<OnlineTbwCandidate> SearchTextForTbw(string text, string query, DiskInfo disk, string source, string sourceUrl)
    {
        var candidates = new List<OnlineTbwCandidate>();
        var queryTokens = ModelTokens(query);
        if (queryTokens.Length == 0)
        {
            return candidates;
        }

        foreach (Match enduranceMatch in EnduranceRegex().Matches(text))
        {
            var start = Math.Max(0, enduranceMatch.Index - 260);
            var length = Math.Min(text.Length - start, 620);
            var window = text.Substring(start, length);
            var score = queryTokens.Count(t => window.Contains(t, StringComparison.OrdinalIgnoreCase));
            if (HasDistinctiveModelToken(queryTokens) && !ContainsDistinctiveModelToken(window, queryTokens))
            {
                continue;
            }

            if (score < RequiredTokenMatches(queryTokens))
            {
                continue;
            }

            var tbw = ParseEnduranceToTbw(enduranceMatch.Groups["value"].Value, enduranceMatch.Groups["unit"].Value);
            if (tbw <= 0)
            {
                continue;
            }

            candidates.Add(new OnlineTbwCandidate
            {
                Model = GuessModel(window, query),
                CapacityGb = GuessCapacityGb(window, disk),
                Tbw = tbw,
                WarrantyYears = GuessWarranty(window),
                MemoryType = GuessMemoryType(window),
                Source = source,
                SourceUrl = sourceUrl,
                Evidence = TrimEvidence(window),
                Warning = "Проверьте вручную: онлайн-источники часто указывают endurance для линейки или другой ёмкости, а не строго для выбранного диска."
            });
        }

        return candidates;
    }

    private static IReadOnlyList<OnlineTbwCandidate> Rank(IEnumerable<OnlineTbwCandidate> candidates, string searchText)
    {
        return candidates
            .Where(c => c.Tbw > 0)
            .GroupBy(c => $"{Normalize(c.Model)}::{c.CapacityGb?.ToString(CultureInfo.InvariantCulture) ?? ""}::{c.Tbw}")
            .Select(g => g.OrderByDescending(c => SourcePriority(c.Source)).First())
            .OrderByDescending(c => ScoreCandidate(c.Model, searchText))
            .ThenByDescending(c => SourcePriority(c.Source))
            .ThenBy(c => c.CapacityGb is null ? 1 : 0)
            .Take(MaxCandidates)
            .ToList();
    }

    private static decimal ParseEnduranceToTbw(string valueText, string unit)
    {
        var normalized = valueText.Replace(",", ".", StringComparison.Ordinal);
        if (!decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            return 0;
        }

        return unit.Equals("PBW", StringComparison.OrdinalIgnoreCase) ? value * 1000m : value;
    }

    private static int RequiredTokenMatches(string[] tokens)
    {
        if (tokens.Any(t => t.Length >= 7))
        {
            return 1;
        }

        return Math.Min(2, tokens.Length);
    }

    private static bool HasDistinctiveModelToken(string[] tokens) => tokens.Any(t => t.Length >= 7 && HasDigit(t));

    private static bool ContainsDistinctiveModelToken(string text, string[] tokens) =>
        tokens.Where(t => t.Length >= 7 && HasDigit(t))
            .Any(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));

    private static bool HasDigit(string text) => text.Any(char.IsDigit);

    private static int? GuessWarranty(string text)
    {
        var match = WarrantyRegex().Match(text);
        return match.Success && int.TryParse(match.Groups[1].Value, out var years) ? years : null;
    }

    private static string? GuessMemoryType(string text)
    {
        if (text.Contains("QLC", StringComparison.OrdinalIgnoreCase)) return "QLC";
        if (text.Contains("TLC", StringComparison.OrdinalIgnoreCase)) return "TLC";
        if (text.Contains("MLC", StringComparison.OrdinalIgnoreCase)) return "MLC";
        return null;
    }

    private static int? GuessCapacityGb(string text, DiskInfo disk)
    {
        if (disk.SizeBytes is not null)
        {
            return (int)Math.Round(disk.SizeBytes.Value / 1_000_000_000d);
        }

        var tb = CapacityTbRegex().Match(text);
        if (tb.Success && decimal.TryParse(tb.Groups[1].Value.Replace(",", ".", StringComparison.Ordinal), NumberStyles.Number, CultureInfo.InvariantCulture, out var tbValue))
        {
            return (int)Math.Round(tbValue * 1000);
        }

        var gb = CapacityGbRegex().Match(text);
        return gb.Success && int.TryParse(gb.Groups[1].Value, out var gbValue) ? gbValue : null;
    }

    private static string GuessModel(string text, string query)
    {
        var queryTokens = ModelTokens(query);
        if (queryTokens.Length == 0)
        {
            return query.Trim();
        }

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var bestIndex = Array.FindIndex(words, w => queryTokens.Any(t => w.Contains(t, StringComparison.OrdinalIgnoreCase)));
        if (bestIndex < 0)
        {
            return query.Trim();
        }

        var start = Math.Max(0, bestIndex - 3);
        var count = Math.Min(words.Length - start, 8);
        return string.Join(' ', words.Skip(start).Take(count)).Trim(' ', ',', ';', ':');
    }

    private static int ScoreCandidate(string candidateModel, string query)
    {
        var candidate = Normalize(candidateModel);
        return ModelTokens(query).Count(t => candidate.Contains(t, StringComparison.OrdinalIgnoreCase));
    }

    private static int SourcePriority(string source) =>
        source.Contains("Johnny Lucky", StringComparison.OrdinalIgnoreCase) ? 2 : 1;

    private static string TrimEvidence(string text)
    {
        var compact = NormalizeText(text);
        return compact.Length <= 360 ? compact : compact[..360] + "...";
    }

    private static string NormalizeText(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Normalize(string? value) => NormalizeText(value ?? "").ToUpperInvariant();

    private static string[] Tokens(string? value) => Normalize(value)
        .Replace("-", " ", StringComparison.Ordinal)
        .Replace("_", " ", StringComparison.Ordinal)
        .Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static string[] ModelTokens(string? value) => Tokens(value)
        .Where(t => t.Length >= 2)
        .Where(t => t is not "SSD" and not "NVME" and not "SATA" and not "M2" and not "PCIE")
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static string StripTags(string html) => TagRegex().Replace(html, " ");

    [GeneratedRegex("<[^>]+>", RegexOptions.Compiled)]
    private static partial Regex TagRegex();

    [GeneratedRegex("(?<value>\\d+(?:[\\.,]\\d+)?)\\s*(?<unit>TBW|PBW)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex EnduranceRegex();

    [GeneratedRegex("(\\d+)\\s*years?", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex WarrantyRegex();

    [GeneratedRegex("(\\d+(?:[\\.,]\\d+)?)\\s*TB", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex CapacityTbRegex();

    [GeneratedRegex("(\\d+)\\s*GB", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex CapacityGbRegex();
}
