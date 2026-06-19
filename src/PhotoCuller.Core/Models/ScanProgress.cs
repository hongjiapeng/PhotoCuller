using PhotoCuller.Core.Enums;

namespace PhotoCuller.Core.Models;

/// <summary>Scan progress snapshot pushed to the UI by the background scan service via a Channel.</summary>
public sealed record ScanProgress(
    ScanPhase Phase,
    int Current,
    int Total,
    string? CurrentFile = null,
    string? Message = null,
    DateTime? StartedAt = null)
{
    /// <summary>Progress percentage (0-100); returns 0 when Total is 0.</summary>
    public int Percent => Total > 0 ? (int)((double)Current / Total * 100) : 0;

    public static ScanProgress Idle =>
        new(ScanPhase.Idle, 0, 0);

    public static ScanProgress Completed(int total, DateTime startedAt) =>
        new(ScanPhase.Completed, total, total, StartedAt: startedAt);

    public static ScanProgress Cancelled(int processed, int total) =>
        new(ScanPhase.Cancelled, processed, total);
}
