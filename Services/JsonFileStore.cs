using System.Text.Json;
using System.Text.Json.Serialization;

namespace DiskHealthAdvisor.Services;

public sealed class JsonFileStore<T> where T : new()
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AppLogger _logger;

    public JsonFileStore(AppLogger logger)
    {
        _logger = logger;
    }

    public async Task<T> LoadAsync(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new T();
            }

            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions) ?? new T();
        }
        catch (Exception ex)
        {
            await _logger.LogAsync($"Не удалось прочитать JSON: {path}", ex);
            return new T();
        }
    }

    public async Task SaveAsync(string path, T value)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var stream = File.Create(path);
            await JsonSerializer.SerializeAsync(stream, value, JsonOptions);
        }
        catch (Exception ex)
        {
            await _logger.LogAsync($"Не удалось сохранить JSON: {path}", ex);
            throw;
        }
    }
}
