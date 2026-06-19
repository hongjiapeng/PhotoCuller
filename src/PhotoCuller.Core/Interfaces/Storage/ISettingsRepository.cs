using PhotoCuller.Core.Models;

namespace PhotoCuller.Core.Interfaces.Storage;

/// <summary>Application settings persistence (reads and writes settings.json).</summary>
public interface ISettingsRepository
{
    Task<AppSettings> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(AppSettings settings, CancellationToken ct = default);
}
