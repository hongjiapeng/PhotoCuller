using PhotoCuller.Core.Models;

namespace PhotoCuller.Core.Interfaces.Scanning;

/// <summary>
/// Scan progress reporting service.
/// The background scan service calls Report() to write updates; the UI layer subscribes to Progress to read them.
/// Uses Channel&lt;T&gt; for thread safety instead of events.
/// </summary>
public interface IScanProgressService
{
    /// <summary>Observable progress stream (the UI subscribes to this to update the interface).</summary>
    IAsyncEnumerable<ScanProgress> ReadAllAsync(CancellationToken ct = default);

    /// <summary>Called by the background service to push a progress snapshot.</summary>
    void Report(ScanProgress progress);

    /// <summary>The latest progress snapshot (used for initial binding).</summary>
    ScanProgress Current { get; }
}
