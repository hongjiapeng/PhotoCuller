using Microsoft.Extensions.Logging;
using OpenCvSharp;
using PhotoCuller.Core.Interfaces.Hashing;

namespace PhotoCuller.Infrastructure.Hashing;

/// <summary>
/// Uses ORB descriptors and RANSAC geometry to confirm that two hash-based
/// candidates contain the same photographed scene.
/// </summary>
public sealed class OrbLocalFeatureMatcher : ILocalFeatureMatcher, IDisposable
{
    private const int MaxImageEdge = 1200;
    private const int MaxFeatures = 1400;
    private const int MinimumKeyPoints = 16;
    private const int MinimumGoodMatches = 12;
    private const int MinimumInliers = 8;
    private const double MinimumInlierRatio = 0.30;
    private const double MinimumInlierCoverage = 0.03;
    private const float RatioTestThreshold = 0.75f;
    private const double RansacReprojectionThreshold = 4.0;
    private const int MaxCachedImages = 256;

    private readonly ILogger<OrbLocalFeatureMatcher> _logger;
    private readonly Dictionary<string, CachedFeatures> _featureCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _cacheOrder = new();
    private readonly object _gate = new();
    private bool _disposed;

    public OrbLocalFeatureMatcher(ILogger<OrbLocalFeatureMatcher> logger)
        => _logger = logger;

    public Task<bool> IsSameSceneAsync(
        string firstFilePath,
        string secondFilePath,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var result = IsSameScene(firstFilePath, secondFilePath, ct);
                return Task.FromResult(result);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "ORB scene verification failed for {FirstPath} and {SecondPath}",
                firstFilePath,
                secondFilePath);
            return Task.FromResult(false);
        }
    }

    private bool IsSameScene(
        string firstFilePath,
        string secondFilePath,
        CancellationToken ct)
    {
        var first = GetOrExtractFeatures(firstFilePath, ct);
        var second = GetOrExtractFeatures(secondFilePath, ct);
        if (first is null || second is null)
            return false;

        ct.ThrowIfCancellationRequested();
        return HasConsistentGeometry(first, second)
            || HasConsistentGeometry(second, first);
    }

    private static bool HasConsistentGeometry(
        LocalFeatures first,
        LocalFeatures second)
    {
        using var matcher = new BFMatcher(NormTypes.Hamming);
        var nearestMatches = matcher.KnnMatch(
            first.Descriptors,
            second.Descriptors,
            k: 2);
        var goodMatches = nearestMatches
            .Where(matches => matches.Length >= 2
                && matches[0].Distance
                    < RatioTestThreshold * matches[1].Distance)
            .Select(matches => matches[0])
            .ToList();
        if (goodMatches.Count < MinimumGoodMatches)
            return false;

        var sourcePoints = goodMatches.Select(match =>
        {
            var point = first.KeyPoints[match.QueryIdx].Pt;
            return new Point2d(point.X, point.Y);
        }).ToArray();
        var targetPoints = goodMatches.Select(match =>
        {
            var point = second.KeyPoints[match.TrainIdx].Pt;
            return new Point2d(point.X, point.Y);
        }).ToArray();

        using var inlierMask = new Mat();
        using var homography = Cv2.FindHomography(
            sourcePoints,
            targetPoints,
            HomographyMethods.Ransac,
            RansacReprojectionThreshold,
            inlierMask);
        if (homography.Empty() || inlierMask.Empty())
            return false;

        var inlierCount = Cv2.CountNonZero(inlierMask);
        var inlierRatio = inlierCount / (double)goodMatches.Count;
        return inlierCount >= MinimumInliers
            && inlierRatio >= MinimumInlierRatio
            && HasSufficientInlierCoverage(
                sourcePoints,
                targetPoints,
                inlierMask,
                first.ImageSize,
                second.ImageSize);
    }

    private static bool HasSufficientInlierCoverage(
        Point2d[] sourcePoints,
        Point2d[] targetPoints,
        Mat inlierMask,
        Size sourceSize,
        Size targetSize)
    {
        var sourceInliers = new List<Point2d>();
        var targetInliers = new List<Point2d>();
        for (var i = 0; i < sourcePoints.Length; i++)
        {
            if (inlierMask.At<byte>(i, 0) == 0)
                continue;

            sourceInliers.Add(sourcePoints[i]);
            targetInliers.Add(targetPoints[i]);
        }

        return GetBoundingCoverage(sourceInliers, sourceSize)
                >= MinimumInlierCoverage
            && GetBoundingCoverage(targetInliers, targetSize)
                >= MinimumInlierCoverage;
    }

    private static double GetBoundingCoverage(
        List<Point2d> points,
        Size imageSize)
    {
        if (points.Count < MinimumInliers
            || imageSize.Width <= 0
            || imageSize.Height <= 0)
        {
            return 0;
        }

        var width = points.Max(point => point.X)
            - points.Min(point => point.X);
        var height = points.Max(point => point.Y)
            - points.Min(point => point.Y);
        return width * height
            / (imageSize.Width * (double)imageSize.Height);
    }

    private LocalFeatures? GetOrExtractFeatures(
        string filePath,
        CancellationToken ct)
    {
        var signature = FileSignature.FromPath(filePath);
        if (_featureCache.TryGetValue(filePath, out var cached))
        {
            if (cached.Signature == signature)
                return cached.Features;

            cached.Features?.Dispose();
            var refreshed = ExtractFeatures(filePath, ct);
            _featureCache[filePath] = new CachedFeatures(signature, refreshed);
            return refreshed;
        }

        var extracted = ExtractFeatures(filePath, ct);
        _featureCache[filePath] = new CachedFeatures(signature, extracted);
        _cacheOrder.Enqueue(filePath);

        while (_cacheOrder.Count > MaxCachedImages)
        {
            var oldestPath = _cacheOrder.Dequeue();
            if (_featureCache.Remove(oldestPath, out var oldest))
                oldest.Features?.Dispose();
        }

        return extracted;
    }

    private static LocalFeatures? ExtractFeatures(
        string filePath,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var source = Cv2.ImRead(filePath, ImreadModes.Grayscale);
        if (source.Empty())
            return null;

        using var resized = new Mat();
        var largestEdge = Math.Max(source.Width, source.Height);
        if (largestEdge > MaxImageEdge)
        {
            var scale = MaxImageEdge / (double)largestEdge;
            Cv2.Resize(
                source,
                resized,
                new Size(),
                scale,
                scale,
                InterpolationFlags.Area);
        }
        else
        {
            source.CopyTo(resized);
        }

        ct.ThrowIfCancellationRequested();
        using var orb = ORB.Create(nFeatures: MaxFeatures);
        using var mask = new Mat();
        var descriptors = new Mat();
        orb.DetectAndCompute(
            resized,
            mask,
            out var keyPoints,
            descriptors);
        if (descriptors.Empty() || keyPoints.Length < MinimumKeyPoints)
        {
            descriptors.Dispose();
            return null;
        }

        return new LocalFeatures(keyPoints, descriptors, resized.Size());
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            foreach (var cached in _featureCache.Values)
                cached.Features?.Dispose();
            _featureCache.Clear();
            _cacheOrder.Clear();
            _disposed = true;
        }
    }

    private sealed class LocalFeatures(
        KeyPoint[] keyPoints,
        Mat descriptors,
        Size imageSize) : IDisposable
    {
        public KeyPoint[] KeyPoints { get; } = keyPoints;
        public Mat Descriptors { get; } = descriptors;
        public Size ImageSize { get; } = imageSize;

        public void Dispose() => Descriptors.Dispose();
    }

    private readonly record struct CachedFeatures(
        FileSignature Signature,
        LocalFeatures? Features);

    private readonly record struct FileSignature(
        bool Exists,
        long Length,
        DateTime LastWriteTimeUtc)
    {
        public static FileSignature FromPath(string filePath)
        {
            var file = new FileInfo(filePath);
            return file.Exists
                ? new FileSignature(true, file.Length, file.LastWriteTimeUtc)
                : default;
        }
    }
}
