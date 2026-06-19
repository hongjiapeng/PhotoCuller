using Microsoft.Extensions.Logging;

namespace PhotoCuller.Infrastructure.Logging;

/// <summary>
/// High-performance Serilog log messages using LoggerMessage source-generated delegates.
/// Avoids boxing and string allocation on the hot scan path.
/// </summary>
internal static partial class Log
{
    // ── Scanner ───────────────────────────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Scan started: {Directory}")]
    internal static partial void ScanStarted(ILogger logger, string directory);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Found {Count} image files in {Directory}")]
    internal static partial void FilesFound(ILogger logger, int count, string directory);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "{Count} files need scanning (skipping {Skipped} unchanged)")]
    internal static partial void FilesToScan(ILogger logger, int count, int skipped);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Scan completed: {Processed} files processed in {ElapsedSec:F1}s")]
    internal static partial void ScanCompleted(ILogger logger, int processed, double elapsedSec);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Scan cancelled for {Directory}")]
    internal static partial void ScanCancelled(ILogger logger, string directory);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Scan failed for {Directory}")]
    internal static partial void ScanFailed(ILogger logger, Exception ex, string directory);

    // ── Metadata / decoder ───────────────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Failed to read metadata for {Path}")]
    internal static partial void MetadataFailed(ILogger logger, Exception ex, string path);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Gray decode failed for {Path}")]
    internal static partial void GrayDecodeFailed(ILogger logger, Exception ex, string path);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "RGB decode failed for {Path}")]
    internal static partial void RgbDecodeFailed(ILogger logger, Exception ex, string path);

    // ── Grouping ──────────────────────────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Building groups for {Count} photos...")]
    internal static partial void BuildingGroups(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Grouping complete: {Groups} groups ({Exact} exact, {Burst} burst, {Similar} similar)")]
    internal static partial void GroupingComplete(
        ILogger logger, int groups, int exact, int burst, int similar);

    // ── File move ─────────────────────────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Moved {Src} → {Dst}")]
    internal static partial void FileMoved(ILogger logger, string src, string dst);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Failed to move {Path}")]
    internal static partial void MoveFileFailed(ILogger logger, Exception ex, string path);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Undone: {Target} → {Source}")]
    internal static partial void Undone(ILogger logger, string target, string source);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Undo failed for log entry {Id}")]
    internal static partial void UndoFailed(ILogger logger, Exception ex, Guid id);

    // ── Repository ────────────────────────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Failed to flush photo batch of {Count} items.")]
    internal static partial void BatchFlushFailed(ILogger logger, Exception ex, int count);

    // ── Grouping diagnostics ──────────────────────────────────────────────────

    [LoggerMessage(Level = LogLevel.Information,
        Message = "pHash candidates: {Count} photos with hash (out of {Total} total)")]
    internal static partial void PHashCandidates(ILogger logger, int count, int total);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "pHash pairs compared: {Pairs}, similar pairs found: {Similar} (threshold ≤ {Threshold})")]
    internal static partial void PHashComparisons(ILogger logger, long pairs, int similar, int threshold);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Similar group #{Index}: {Count} photos, recommended={RecommendedId}")]
    internal static partial void SimilarGroupCreated(ILogger logger, int index, int count, Guid? recommendedId);
}
