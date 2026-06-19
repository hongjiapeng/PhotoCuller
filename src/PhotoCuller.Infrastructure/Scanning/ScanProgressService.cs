using System.Runtime.CompilerServices;
using System.Threading.Channels;
using PhotoCuller.Core.Interfaces.Scanning;
using PhotoCuller.Core.Models;

namespace PhotoCuller.Infrastructure.Scanning;

/// <summary>
/// Thread-safe scan progress broadcaster backed by a bounded Channel.
/// The background scanner calls Report(); the UI reads via ReadAllAsync().
/// </summary>
public sealed class ScanProgressService : IScanProgressService, IDisposable
{
    private readonly Channel<ScanProgress> _channel =
        Channel.CreateBounded<ScanProgress>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = false,
        });

    private volatile ScanProgress _current = ScanProgress.Idle;

    public ScanProgress Current => _current;

    public void Report(ScanProgress progress)
    {
        _current = progress;
        // Non-blocking: if the channel is full, the oldest item is dropped (configured above)
        _channel.Writer.TryWrite(progress);
    }

    public async IAsyncEnumerable<ScanProgress> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var progress in _channel.Reader.ReadAllAsync(ct))
            yield return progress;
    }

    public void Dispose() => _channel.Writer.TryComplete();
}
