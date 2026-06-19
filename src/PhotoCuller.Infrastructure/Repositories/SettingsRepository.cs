using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PhotoCuller.Core.Interfaces.Storage;
using PhotoCuller.Core.Models;

namespace PhotoCuller.Infrastructure.Repositories;

/// <summary>
/// Persists AppSettings to %LocalAppData%\PhotoCuller\settings.json.
/// Uses System.Text.Json with indented output for human readability.
/// Thread-safe via SemaphoreSlim (single writer at a time).
/// </summary>
public sealed class SettingsRepository : ISettingsRepository, IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _settingsPath;
    private readonly ILogger<SettingsRepository> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public SettingsRepository(ILogger<SettingsRepository> logger)
    {
        _logger = logger;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(appData, "PhotoCuller");
        Directory.CreateDirectory(dir);
        _settingsPath = Path.Combine(dir, "settings.json");
    }

    public async Task<AppSettings> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_settingsPath))
        {
            _logger.LogInformation(
                "Settings file not found at {Path}. Using defaults.", _settingsPath);
            return new AppSettings();
        }

        try
        {
            await _lock.WaitAsync(ct);
            var json = await File.ReadAllTextAsync(_settingsPath, ct);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts);
            return settings ?? new AppSettings();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to load settings from {Path}. Using defaults.", _settingsPath);
            return new AppSettings();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        try
        {
            await _lock.WaitAsync(ct);
            var json = JsonSerializer.Serialize(settings, JsonOpts);

            // Write to temp file then rename — atomic on Windows NTFS
            var tmp = _settingsPath + ".tmp";
            await File.WriteAllTextAsync(tmp, json, ct);
            File.Move(tmp, _settingsPath, overwrite: true);

            _logger.LogDebug("Settings saved to {Path}.", _settingsPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings to {Path}.", _settingsPath);
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose() => _lock.Dispose();
}
