using Microsoft.Extensions.Logging;
using PhotoCuller.Core.Interfaces.Scoring;
using PhotoCuller.Core.Models;

namespace PhotoCuller.Infrastructure.ImageProcessing.Scorers;

/// <summary>
/// Resolution scorer based on total pixel count.
/// Does not need OpenCv — uses already-read Width/Height from PhotoItem.
///
/// Scale:
///   &lt; 0.5 MP  → 0–30   (too small for most uses)
///   0.5–2 MP  → 30–70  (acceptable, e.g. social media)
///   2–8 MP    → 70–95  (good, suitable for print)
///   8–24 MP   → 95–100 (excellent)
///   &gt; 24 MP   → 100    (capped — no bonus for huge files)
/// </summary>
public sealed class ResolutionAnalyzer
{
    private readonly ILogger<ResolutionAnalyzer> _logger;

    public ResolutionAnalyzer(ILogger<ResolutionAnalyzer> logger) => _logger = logger;

    public Task<float?> AnalyzeAsync(PhotoItem photo, CancellationToken ct = default)
    {
        if (photo.Width <= 0 || photo.Height <= 0)
            return Task.FromResult<float?>(null);

        double mp = (double)photo.Width * photo.Height / 1_000_000.0;

        double score;
        if (mp < 0.5)
            score = mp / 0.5 * 30.0;
        else if (mp < 2.0)
            score = 30.0 + (mp - 0.5) / 1.5 * 40.0;
        else if (mp < 8.0)
            score = 70.0 + (mp - 2.0) / 6.0 * 25.0;
        else if (mp < 24.0)
            score = 95.0 + (mp - 8.0) / 16.0 * 5.0;
        else
            score = 100.0;

        return Task.FromResult<float?>((float)Math.Clamp(score, 0, 100));
    }
}
