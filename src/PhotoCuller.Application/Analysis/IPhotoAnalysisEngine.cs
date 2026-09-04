using PhotoCuller.Core.Models;

namespace PhotoCuller.Application.Analysis;

/// <summary>
/// UI-independent entry point for a complete PhotoCuller analysis run.
/// </summary>
public interface IPhotoAnalysisEngine
{
    Task<PhotoAnalysisRun> AnalyzeAsync(
        string directory,
        PhotoAnalysisOptions? options = null,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
