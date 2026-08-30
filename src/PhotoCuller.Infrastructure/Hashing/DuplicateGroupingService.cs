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
///   Pass 2 — Visual groups (pHash + dHash + capture-sequence context)
///     A strict global threshold finds near-duplicates across the library. A
///     more tolerant threshold is used only when both perceptual hashes agree
///     for adjacent filenames in the same directory. Both edge types are
///     clustered in one graph so a burst cannot hide members from later
///     similarity detection.
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

    public async Task<IReadOnlyList<PhotoGroup>> BuildGroupsAsync(
        IReadOnlyList<PhotoItem> photos,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var settings = await _settingsRepository.LoadAsync(ct);
        return await Task.Run(
            () => BuildGroupsCoreAsync(photos, settings, progress, ct), ct);
    }

    // ── Core algorithm ───────────────────────────────────────────────────────

    private async Task<IReadOnlyList<PhotoGroup>> BuildGroupsCoreAsync(
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
        groups.AddRange(await BuildVisualGroupsAsync(photos, grouped, settings, ct));

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

    private async Task<IReadOnlyList<PhotoGroup>> BuildVisualGroupsAsync(
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
            return [];
        }

        // pHash captures broad structure well; dHash rejects subject changes
        // that happen to share a similar low-frequency composition.
        var differenceHashes = await LoadDifferenceHashesAsync(candidates, ct);

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
                var globallySimilar = match.Distance <= profile.GlobalThreshold
                    && DifferenceHashesMatch(
                        candidates[i],
                        candidates[match.Index],
                        differenceHashes,
                        profile.GlobalDifferenceThreshold);
                var sameSequence = IsSequenceVisualMatch(
                    candidates[i],
                    candidates[match.Index],
                    match.Distance,
                    differenceHashes,
                    profile);

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
            .SelectMany(c => SplitIntoCoherentClusters(c, differenceHashes, profile))
            .Where(c => c.Count >= 2)
            .ToList();

        Log.SimilarHashSearch(_logger,
            candidates.Count,
            queryCount,
            similarPairs,
            similarClusters.Count,
            profile.SearchThreshold);

        var result = new List<PhotoGroup>(similarClusters.Count);
        foreach (var cluster in similarClusters)
        {
            var isCaptureSequence = IsCaptureSequenceCluster(
                cluster, differenceHashes, profile);
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

            result.Add(group);
        }

        return result;
    }

    private async Task<Dictionary<Guid, ulong>> LoadDifferenceHashesAsync(
        IReadOnlyList<PhotoItem> candidates,
        CancellationToken ct)
    {
        var hashes = new Dictionary<Guid, ulong>(candidates.Count);
        foreach (var photo in candidates)
        {
            ct.ThrowIfCancellationRequested();
            var hash = await _pHash.ComputeDHashAsync(photo.FilePath, ct);
            if (hash.HasValue)
                hashes[photo.Id] = hash.Value;
        }

        return hashes;
    }

    private bool IsCaptureSequenceCluster(
        IReadOnlyList<PhotoItem> cluster,
        IReadOnlyDictionary<Guid, ulong> differenceHashes,
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
            if (!IsSequenceContextPair(
                    sorted[i - 1],
                    sorted[i],
                    profile,
                    profile.BurstMaxIntervalSeconds))
            {
                return false;
            }

            var distance = _pHash.HammingDistance(
                sorted[i - 1].PerceptualHash!.Value,
                sorted[i].PerceptualHash!.Value);
            if (distance > profile.GlobalThreshold)
                return false;

            if (!DifferenceHashesMatch(
                    sorted[i - 1],
                    sorted[i],
                    differenceHashes,
                    profile.GlobalDifferenceThreshold))
            {
                return false;
            }
        }

        return true;
    }

    private IEnumerable<List<PhotoItem>> SplitIntoCoherentClusters(
        List<PhotoItem> connectedComponent,
        IReadOnlyDictionary<Guid, ulong> differenceHashes,
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
                IsSequenceAnchorCompatible(
                    group[0], photo, differenceHashes, profile)
                && AreCompatible(
                    group[^1], photo, differenceHashes, profile));

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
        IReadOnlyDictionary<Guid, ulong> differenceHashes,
        SimilarityProfile profile)
    {
        var distance = _pHash.HammingDistance(
            a.PerceptualHash!.Value,
            b.PerceptualHash!.Value);
        return (distance <= profile.GlobalThreshold
                && DifferenceHashesMatch(
                    a, b, differenceHashes, profile.GlobalDifferenceThreshold))
            || IsSequenceVisualMatch(
                a, b, distance, differenceHashes, profile);
    }

    private bool IsSequenceAnchorCompatible(
        PhotoItem anchor,
        PhotoItem photo,
        IReadOnlyDictionary<Guid, ulong> differenceHashes,
        SimilarityProfile profile)
    {
        var distance = _pHash.HammingDistance(
            anchor.PerceptualHash!.Value,
            photo.PerceptualHash!.Value);
        if (distance <= profile.GlobalThreshold)
        {
            return DifferenceHashesMatch(
                anchor,
                photo,
                differenceHashes,
                profile.GlobalDifferenceThreshold);
        }

        return distance <= profile.SequenceThreshold
            && IsSameCaptureSession(anchor, photo, profile)
            && DifferenceHashesMatch(
                anchor,
                photo,
                differenceHashes,
                profile.SequenceDifferenceThreshold);
    }

    private bool IsSequenceVisualMatch(
        PhotoItem a,
        PhotoItem b,
        int perceptualDistance,
        IReadOnlyDictionary<Guid, ulong> differenceHashes,
        SimilarityProfile profile)
        => perceptualDistance <= profile.SequenceThreshold
            && IsSequenceContextPair(a, b, profile)
            && DifferenceHashesMatch(
                a,
                b,
                differenceHashes,
                profile.SequenceDifferenceThreshold);

    private bool DifferenceHashesMatch(
        PhotoItem a,
        PhotoItem b,
        IReadOnlyDictionary<Guid, ulong> differenceHashes,
        int threshold)
        => differenceHashes.TryGetValue(a.Id, out var hashA)
            && differenceHashes.TryGetValue(b.Id, out var hashB)
            && _pHash.HammingDistance(hashA, hashB)
                <= threshold;

    private static bool IsSequenceContextPair(
        PhotoItem a,
        PhotoItem b,
        SimilarityProfile profile,
        int? timeWindowSeconds = null)
    {
        if (!IsSameCaptureSession(a, b, profile, timeWindowSeconds))
            return false;

        var hasNumberA = TryGetSequenceNumber(a, out var prefixA, out var numberA);
        var hasNumberB = TryGetSequenceNumber(b, out var prefixB, out var numberB);
        if (!hasNumberA || !hasNumberB)
            return true;

        var numberGap = Math.Abs(numberA - numberB);
        return string.Equals(prefixA, prefixB, StringComparison.OrdinalIgnoreCase)
            && numberGap > 0
            && numberGap <= profile.MaxSequenceNumberGap;
    }

    private static bool IsSameCaptureSession(
        PhotoItem a,
        PhotoItem b,
        SimilarityProfile profile,
        int? timeWindowSeconds = null)
    {
        if (!profile.UseCaptureSequence)
            return false;

        var directoryA = Path.GetDirectoryName(a.FilePath) ?? string.Empty;
        var directoryB = Path.GetDirectoryName(b.FilePath) ?? string.Empty;
        if (!string.Equals(directoryA, directoryB, StringComparison.OrdinalIgnoreCase))
            return false;

        var timeA = GetCaptureTime(a);
        var timeB = GetCaptureTime(b);
        var allowedSeconds = timeWindowSeconds ?? profile.SequenceWindowSeconds;
        if (!timeA.HasValue || !timeB.HasValue
            || Math.Abs((timeA.Value - timeB.Value).TotalSeconds)
                > allowedSeconds)
        {
            return false;
        }

        var hasNumberA = TryGetSequenceNumber(a, out var prefixA, out _);
        var hasNumberB = TryGetSequenceNumber(b, out var prefixB, out _);
        return !hasNumberA || !hasNumberB
            || string.Equals(prefixA, prefixB, StringComparison.OrdinalIgnoreCase);
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
