using Microsoft.Extensions.Logging;
using PhotoCuller.Core.Enums;
using PhotoCuller.Core.Interfaces.Hashing;
using PhotoCuller.Core.Interfaces.Storage;
using PhotoCuller.Core.Models;
using PhotoCuller.Infrastructure.Logging;

namespace PhotoCuller.Infrastructure.Hashing;

/// <summary>
/// Builds PhotoGroup lists from a flat collection of PhotoItems.
///
/// Two passes in order of priority:
///
///   Pass 1 — Exact duplicates (SHA-256)
///     Any photos sharing an identical hash → ExactDuplicate group.
///     Already-grouped photos are excluded from subsequent passes.
///
///   Pass 2 — Visual groups (pHash + capture-sequence context)
///     A strict global threshold finds near-duplicates across the library. A
///     more tolerant threshold is used only for nearby, sequential filenames
///     in the same directory. Both edge types are clustered in one graph so a
///     burst cannot hide members from later similarity detection.
///
/// After grouping, each group is assigned a recommended photo:
///   - If any member is ManualKeep → that one wins.
///   - Otherwise → highest OverallScore wins (ties broken by resolution).
///   - Photos with no score, zero dimensions, or ErrorMessage are penalised.
/// </summary>
public sealed class DuplicateGroupingService : IDuplicateGroupingService
{
    private readonly IPerceptualHashService _pHash;
    private readonly ISettingsRepository _settingsRepository;
    private readonly ILogger<DuplicateGroupingService> _logger;

    public DuplicateGroupingService(
        IPerceptualHashService pHash,
        ISettingsRepository settingsRepository,
        ILogger<DuplicateGroupingService> logger)
    {
        _pHash = pHash;
        _settingsRepository = settingsRepository;
        _logger = logger;
    }

    public Task<IReadOnlyList<PhotoGroup>> BuildGroupsAsync(
        IReadOnlyList<PhotoItem> photos,
        IProgress<string>? progress = null,
        CancellationToken ct = default) =>
        Task.Run(async () =>
        {
            var settings = await _settingsRepository.LoadAsync(ct);
            return BuildGroups(photos, settings, progress, ct);
        }, ct);

    // ── Core algorithm ───────────────────────────────────────────────────────

    private IReadOnlyList<PhotoGroup> BuildGroups(
        IReadOnlyList<PhotoItem> photos,
        AppSettings settings,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        Log.BuildingGroups(_logger, photos.Count);

        var groups = new List<PhotoGroup>();
        var grouped = new HashSet<Guid>(); // tracks which photos already have a group

        // Pass 1: exact duplicates
        progress?.Report("Detecting exact duplicates...");
        ct.ThrowIfCancellationRequested();
        groups.AddRange(BuildExactDuplicateGroups(photos, grouped));

        // Pass 2: similar photos and capture sequences in one graph
        progress?.Report("Detecting similar photos...");
        ct.ThrowIfCancellationRequested();
        groups.AddRange(BuildVisualGroups(photos, grouped, settings, ct));

        // Assign recommended photo to each group
        foreach (var group in groups)
            AssignRecommendation(group);

        // Log final group details
        int idx = 0;
        foreach (var g in groups.Where(g => g.GroupType == GroupType.Similar))
            Log.SimilarGroupCreated(_logger, ++idx, g.PhotoCount, g.RecommendedPhotoId);

        Log.GroupingComplete(_logger, groups.Count,
            groups.Count(g => g.GroupType == GroupType.ExactDuplicate),
            groups.Count(g => g.GroupType == GroupType.Burst),
            groups.Count(g => g.GroupType == GroupType.Similar));

        return groups;
    }

    // ── Pass 1: Exact duplicates ─────────────────────────────────────────────

    private static IEnumerable<PhotoGroup> BuildExactDuplicateGroups(
        IReadOnlyList<PhotoItem> photos, HashSet<Guid> grouped)
    {
        var byHash = photos
            .Where(p => !string.IsNullOrEmpty(p.Sha256Hash))
            .GroupBy(p => p.Sha256Hash!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);

        foreach (var hashGroup in byHash)
        {
            var members = hashGroup.ToList();
            var group = new PhotoGroup
            {
                GroupType      = GroupType.ExactDuplicate,
                Name           = $"Duplicate ({members.Count} photos)",
                PhotoCount     = members.Count,
                SimilarityScore = 1.0f,
            };

            foreach (var photo in members)
            {
                photo.GroupId = group.Id;
                group.Photos.Add(photo);
                grouped.Add(photo.Id);
            }

            yield return group;
        }
    }

    // ── Pass 2: Similar photos and capture sequences ────────────────────────

    private IEnumerable<PhotoGroup> BuildVisualGroups(
        IReadOnlyList<PhotoItem> photos,
        HashSet<Guid> grouped,
        AppSettings settings,
        CancellationToken ct)
    {
        var candidates = photos
            .Where(p => !grouped.Contains(p.Id) && p.PerceptualHash.HasValue)
            .ToList();

        Log.PHashCandidates(_logger, candidates.Count, photos.Count);

        var profile = SimilarityThresholds.FromSettings(settings);
        if (candidates.Count < 2)
        {
            Log.SimilarHashSearch(_logger,
                candidates.Count,
                queryCount: 0,
                similarPairs: 0,
                groupCount: 0,
                profile.SearchThreshold);
            yield break;
        }

        var uf = new UnionFind(candidates.Count);
        var tree = new HammingBkTree(_pHash.HammingDistance);

        long similarPairs = 0;
        int queryCount = 0;
        for (int i = 0; i < candidates.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            queryCount++;

            foreach (var match in tree.Find(
                         candidates[i].PerceptualHash!.Value,
                         profile.SearchThreshold))
            {
                var globallySimilar = match.Distance <= profile.GlobalThreshold;
                var sameSequence = match.Distance <= profile.SequenceThreshold
                    && IsCaptureSequencePair(candidates[i], candidates[match.Index], profile);

                if (globallySimilar || sameSequence)
                {
                    uf.Union(i, match.Index);
                    similarPairs++;
                }
            }

            tree.Add(candidates[i].PerceptualHash!.Value, i);
        }

        // Collect groups with ≥ 2 members
        var clusters = new Dictionary<int, List<PhotoItem>>();
        for (int i = 0; i < candidates.Count; i++)
        {
            var root = uf.Find(i);
            if (!clusters.ContainsKey(root))
                clusters[root] = [];
            clusters[root].Add(candidates[i]);
        }

        var similarClusters = clusters.Values
            .SelectMany(c => SplitIntoCoherentClusters(c, profile))
            .Where(c => c.Count >= 2)
            .ToList();

        Log.SimilarHashSearch(_logger,
            candidates.Count,
            queryCount,
            similarPairs,
            similarClusters.Count,
            profile.SearchThreshold);

        foreach (var cluster in similarClusters)
        {
            var isCaptureSequence = IsCaptureSequenceCluster(cluster, profile);
            var groupType = isCaptureSequence ? GroupType.Burst : GroupType.Similar;
            var group = new PhotoGroup
            {
                GroupType       = groupType,
                Name            = isCaptureSequence
                    ? $"Burst ({cluster.Count} shots)"
                    : $"Similar ({cluster.Count} photos)",
                PhotoCount      = cluster.Count,
                SimilarityScore = isCaptureSequence ? 0.9f : 0.75f,
            };

            foreach (var photo in cluster)
            {
                photo.GroupId = group.Id;
                group.Photos.Add(photo);
                grouped.Add(photo.Id);
            }

            yield return group;
        }
    }

    private bool IsCaptureSequenceCluster(
        IReadOnlyList<PhotoItem> cluster,
        SimilarityProfile profile)
    {
        if (!profile.UseCaptureSequence || cluster.Count < 2)
            return false;

        var sorted = cluster
            .Where(p => GetCaptureTime(p).HasValue)
            .OrderBy(GetCaptureTime)
            .ToList();
        if (sorted.Count != cluster.Count)
            return false;

        for (var i = 1; i < sorted.Count; i++)
        {
            if (!IsCaptureSequencePair(sorted[i - 1], sorted[i], profile))
                return false;

            var distance = _pHash.HammingDistance(
                sorted[i - 1].PerceptualHash!.Value,
                sorted[i].PerceptualHash!.Value);
            if (distance > profile.SequenceThreshold)
                return false;
        }

        return true;
    }

    private IEnumerable<List<PhotoItem>> SplitIntoCoherentClusters(
        List<PhotoItem> connectedComponent,
        SimilarityProfile profile)
    {
        if (connectedComponent.Count < 3)
        {
            yield return connectedComponent;
            yield break;
        }

        var ordered = connectedComponent
            .OrderBy(p => GetCaptureTime(p) ?? DateTime.MaxValue)
            .ThenBy(p => p.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var coherent = new List<List<PhotoItem>>();

        foreach (var photo in ordered)
        {
            var target = coherent.FirstOrDefault(group =>
                AreCompatible(group[0], photo, profile)
                && AreCompatible(group[^1], photo, profile));

            if (target is null)
                coherent.Add([photo]);
            else
                target.Add(photo);
        }

        foreach (var cluster in coherent)
            yield return cluster;
    }

    private bool AreCompatible(
        PhotoItem a,
        PhotoItem b,
        SimilarityProfile profile)
    {
        var distance = _pHash.HammingDistance(
            a.PerceptualHash!.Value,
            b.PerceptualHash!.Value);
        return distance <= profile.GlobalThreshold
            || (distance <= profile.SequenceThreshold
                && IsCaptureSequencePair(a, b, profile));
    }

    private static bool IsCaptureSequencePair(
        PhotoItem a,
        PhotoItem b,
        SimilarityProfile profile)
    {
        if (!profile.UseCaptureSequence)
            return false;

        var directoryA = Path.GetDirectoryName(a.FilePath) ?? string.Empty;
        var directoryB = Path.GetDirectoryName(b.FilePath) ?? string.Empty;
        if (!string.Equals(directoryA, directoryB, StringComparison.OrdinalIgnoreCase))
            return false;

        var timeA = GetCaptureTime(a);
        var timeB = GetCaptureTime(b);
        if (!timeA.HasValue || !timeB.HasValue
            || Math.Abs((timeA.Value - timeB.Value).TotalSeconds)
                > profile.SequenceWindowSeconds)
        {
            return false;
        }

        var hasNumberA = TryGetSequenceNumber(a, out var prefixA, out var numberA);
        var hasNumberB = TryGetSequenceNumber(b, out var prefixB, out var numberB);
        if (!hasNumberA || !hasNumberB)
            return true;

        var numberGap = Math.Abs(numberA - numberB);
        return string.Equals(prefixA, prefixB, StringComparison.OrdinalIgnoreCase)
            && numberGap > 0
            && numberGap <= profile.MaxSequenceNumberGap;
    }

    private static DateTime? GetCaptureTime(PhotoItem photo)
        => photo.ExifTakenTime ?? photo.CreatedTime;

    private static bool TryGetSequenceNumber(
        PhotoItem photo,
        out string prefix,
        out long number)
    {
        var fileName = string.IsNullOrWhiteSpace(photo.FileName)
            ? Path.GetFileName(photo.FilePath)
            : photo.FileName;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var digitStart = stem.Length;
        while (digitStart > 0 && char.IsAsciiDigit(stem[digitStart - 1]))
            digitStart--;

        prefix = stem[..digitStart];
        number = 0;
        return digitStart < stem.Length
            && long.TryParse(stem.AsSpan(digitStart), out number);
    }

    // ── Recommendation strategy ──────────────────────────────────────────────

    private static void AssignRecommendation(PhotoGroup group)
    {
        if (!group.Photos.Any()) return;

        // Manual keep always wins
        var manualKeep = group.Photos
            .FirstOrDefault(p => p.RecommendationStatus == RecommendationStatus.ManualKeep);
        if (manualKeep is not null)
        {
            group.RecommendedPhotoId = manualKeep.Id;
            return;
        }

        // Rank remaining candidates by quality
        var best = group.Photos
            .Where(p => string.IsNullOrEmpty(p.ErrorMessage))   // skip errored
            .Where(p => p.Width > 0 && p.Height > 0)            // skip zero-dim
            .OrderByDescending(p => p.OverallScore ?? 0)
            .ThenByDescending(p => (long)(p.Width) * p.Height)  // resolution tiebreak
            .ThenBy(p => p.FileName)                             // deterministic tiebreak
            .FirstOrDefault();

        if (best is not null)
        {
            group.RecommendedPhotoId = best.Id;
            best.RecommendationStatus = RecommendationStatus.Keep;

            // Mark others as Review (not forced-delete — user decides)
            foreach (var other in group.Photos.Where(p => p.Id != best.Id))
            {
                if (other.RecommendationStatus == RecommendationStatus.Unprocessed)
                    other.RecommendationStatus = RecommendationStatus.Review;
            }
        }
    }

    // ── Union-Find (path compression + union by rank) ────────────────────────

    private sealed class UnionFind
    {
        private readonly int[] _parent;
        private readonly int[] _rank;

        public UnionFind(int n)
        {
            _parent = Enumerable.Range(0, n).ToArray();
            _rank   = new int[n];
        }

        public int Find(int x)
        {
            if (_parent[x] != x)
                _parent[x] = Find(_parent[x]); // path compression
            return _parent[x];
        }

        public void Union(int x, int y)
        {
            int rx = Find(x), ry = Find(y);
            if (rx == ry) return;

            // Union by rank
            if (_rank[rx] < _rank[ry]) (rx, ry) = (ry, rx);
            _parent[ry] = rx;
            if (_rank[rx] == _rank[ry]) _rank[rx]++;
        }
    }
}
