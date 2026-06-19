using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhotoCuller.Core.Enums;
using PhotoCuller.Core.Interfaces.Hashing;
using PhotoCuller.Core.Models;

namespace PhotoCuller.Infrastructure.Hashing;

/// <summary>
/// Builds PhotoGroup lists from a flat collection of PhotoItems.
///
/// Three passes in order of priority:
///
///   Pass 1 — Exact duplicates (SHA-256)
///     Any photos sharing an identical hash → ExactDuplicate group.
///     Already-grouped photos are excluded from subsequent passes.
///
///   Pass 2 — Burst shots (EXIF time window + pHash)
///     Within the same directory, photos taken within BurstTimeWindowSeconds
///     AND whose pHash Hamming distance ≤ SimilarityThresholdPossible are
///     grouped as a Burst.
///
///   Pass 3 — Visually similar (pHash, Union-Find)
///     All remaining ungrouped photos are compared pairwise.
///     Union-Find merges any pair with Hamming distance ≤ SimilarityThresholdPossible.
///     Groups with ≥ 2 members become Similar groups.
///
/// After grouping, each group is assigned a recommended photo:
///   - If any member is ManualKeep → that one wins.
///   - Otherwise → highest OverallScore wins (ties broken by resolution).
///   - Photos with no score, zero dimensions, or ErrorMessage are penalised.
/// </summary>
public sealed class DuplicateGroupingService : IDuplicateGroupingService
{
    private readonly IPerceptualHashService _pHash;
    private readonly AppSettings _settings;
    private readonly ILogger<DuplicateGroupingService> _logger;

    public DuplicateGroupingService(
        IPerceptualHashService pHash,
        IOptions<AppSettings> settings,
        ILogger<DuplicateGroupingService> logger)
    {
        _pHash = pHash;
        _settings = settings.Value;
        _logger = logger;
    }

    public Task<IReadOnlyList<PhotoGroup>> BuildGroupsAsync(
        IReadOnlyList<PhotoItem> photos,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
        => Task.Run(() => BuildGroups(photos, progress, ct), ct);

    // ── Core algorithm ───────────────────────────────────────────────────────

    private IReadOnlyList<PhotoGroup> BuildGroups(
        IReadOnlyList<PhotoItem> photos,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Building groups for {Count} photos...", photos.Count);

        var groups = new List<PhotoGroup>();
        var grouped = new HashSet<Guid>(); // tracks which photos already have a group

        // Pass 1: exact duplicates
        progress?.Report("Detecting exact duplicates...");
        ct.ThrowIfCancellationRequested();
        groups.AddRange(BuildExactDuplicateGroups(photos, grouped));

        // Pass 2: burst shots
        progress?.Report("Detecting burst shots...");
        ct.ThrowIfCancellationRequested();
        groups.AddRange(BuildBurstGroups(photos, grouped, ct));

        // Pass 3: similar photos (Union-Find)
        progress?.Report("Detecting similar photos...");
        ct.ThrowIfCancellationRequested();
        groups.AddRange(BuildSimilarGroups(photos, grouped, ct));

        // Assign recommended photo to each group
        foreach (var group in groups)
            AssignRecommendation(group);

        _logger.LogInformation(
            "Grouping complete: {Groups} groups " +
            "({Exact} exact, {Burst} burst, {Similar} similar)",
            groups.Count,
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

    // ── Pass 2: Burst shots ──────────────────────────────────────────────────

    private IEnumerable<PhotoGroup> BuildBurstGroups(
        IReadOnlyList<PhotoItem> photos,
        HashSet<Guid> grouped,
        CancellationToken ct)
    {
        if (!_settings.UseCaptureTimeForGrouping)
            yield break;

        var windowSec = _settings.BurstTimeWindowSeconds;
        var threshold = _settings.SimilarityThresholdPossible;

        // Only consider photos with capture time and pHash, not yet grouped
        var candidates = photos
            .Where(p => !grouped.Contains(p.Id)
                     && p.PerceptualHash.HasValue
                     && (p.ExifTakenTime ?? p.CreatedTime) is not null)
            .OrderBy(p => p.ExifTakenTime ?? p.CreatedTime)
            .ToList();

        // Group by directory first (burst shots are in the same folder)
        var byDir = candidates.GroupBy(p => Path.GetDirectoryName(p.FilePath) ?? "");

        foreach (var dirGroup in byDir)
        {
            ct.ThrowIfCancellationRequested();

            var sorted = dirGroup.OrderBy(p => p.ExifTakenTime ?? p.CreatedTime).ToList();
            var visited = new HashSet<Guid>();

            for (int i = 0; i < sorted.Count; i++)
            {
                if (visited.Contains(sorted[i].Id)) continue;

                var burst = new List<PhotoItem> { sorted[i] };
                var baseTime = (sorted[i].ExifTakenTime ?? sorted[i].CreatedTime)!.Value;

                for (int j = i + 1; j < sorted.Count; j++)
                {
                    if (visited.Contains(sorted[j].Id)) continue;

                    var t = (sorted[j].ExifTakenTime ?? sorted[j].CreatedTime)!.Value;
                    if ((t - baseTime).TotalSeconds > windowSec) break;

                    // Must also be visually similar
                    if (sorted[i].PerceptualHash.HasValue && sorted[j].PerceptualHash.HasValue
                        && _pHash.HammingDistance(
                            sorted[i].PerceptualHash!.Value,
                            sorted[j].PerceptualHash!.Value) <= threshold)
                    {
                        burst.Add(sorted[j]);
                        visited.Add(sorted[j].Id);
                    }
                }

                if (burst.Count < 2) continue;

                visited.Add(sorted[i].Id);
                var group = new PhotoGroup
                {
                    GroupType  = GroupType.Burst,
                    Name       = $"Burst ({burst.Count} shots)",
                    PhotoCount = burst.Count,
                    SimilarityScore = 0.9f,
                };

                foreach (var photo in burst)
                {
                    photo.GroupId = group.Id;
                    group.Photos.Add(photo);
                    grouped.Add(photo.Id);
                }

                yield return group;
            }
        }
    }

    // ── Pass 3: Similar photos (Union-Find) ──────────────────────────────────

    private IEnumerable<PhotoGroup> BuildSimilarGroups(
        IReadOnlyList<PhotoItem> photos,
        HashSet<Guid> grouped,
        CancellationToken ct)
    {
        var candidates = photos
            .Where(p => !grouped.Contains(p.Id) && p.PerceptualHash.HasValue)
            .ToList();

        if (candidates.Count < 2)
            yield break;

        var threshold = _settings.SimilarityThresholdPossible;
        var uf = new UnionFind(candidates.Count);

        // O(n²) pairwise comparison — acceptable for typical library sizes
        // For very large libraries (>50k), a VP-tree or LSH index would be needed
        for (int i = 0; i < candidates.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            for (int j = i + 1; j < candidates.Count; j++)
            {
                if (_pHash.HammingDistance(
                    candidates[i].PerceptualHash!.Value,
                    candidates[j].PerceptualHash!.Value) <= threshold)
                {
                    uf.Union(i, j);
                }
            }
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

        foreach (var cluster in clusters.Values.Where(c => c.Count >= 2))
        {
            var group = new PhotoGroup
            {
                GroupType  = GroupType.Similar,
                Name       = $"Similar ({cluster.Count} photos)",
                PhotoCount = cluster.Count,
                SimilarityScore = 0.75f,
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
