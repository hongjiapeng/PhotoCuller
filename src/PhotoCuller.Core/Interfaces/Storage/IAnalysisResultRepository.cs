using PhotoCuller.Core.Models;

namespace PhotoCuller.Core.Interfaces.Storage;

/// <summary>Publishes a complete analysis atomically, retaining the previous result on failure.</summary>
public interface IAnalysisResultRepository
{
    Task ReplaceAsync(
        IReadOnlyList<PhotoItem> photos,
        IReadOnlyList<PhotoGroup> groups,
        CancellationToken ct = default);
}
