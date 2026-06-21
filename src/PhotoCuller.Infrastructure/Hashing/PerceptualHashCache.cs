using System.Text.Json;

namespace PhotoCuller.Infrastructure.Hashing;

internal sealed class PerceptualHashCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General)
    {
        WriteIndented = false,
    };

    private readonly string _cachePath;
    private readonly object _gate = new();
    private Dictionary<string, CacheEntry>? _entries;

    public PerceptualHashCache()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PhotoCuller");
        Directory.CreateDirectory(root);
        _cachePath = Path.Combine(root, "perceptual-hash-cache.json");
    }

    public bool TryGet(
        string filePath,
        string hashAlgorithm,
        int hashSize,
        out ulong hash)
    {
        var key = CreateKey(filePath, hashAlgorithm, hashSize);
        lock (_gate)
        {
            EnsureLoaded();
            if (_entries!.TryGetValue(key, out var entry))
            {
                hash = entry.Hash;
                return true;
            }
        }

        hash = 0;
        return false;
    }

    public void Set(
        string filePath,
        string hashAlgorithm,
        int hashSize,
        ulong hash)
    {
        var key = CreateKey(filePath, hashAlgorithm, hashSize);
        lock (_gate)
        {
            EnsureLoaded();
            _entries![key] = new CacheEntry(hash);
            Save();
        }
    }

    private static string CreateKey(string filePath, string hashAlgorithm, int hashSize)
    {
        var fi = new FileInfo(filePath);
        return string.Join('|',
            Path.GetFullPath(filePath).ToUpperInvariant(),
            fi.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            fi.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            hashAlgorithm,
            hashSize.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private void EnsureLoaded()
    {
        if (_entries is not null)
            return;

        try
        {
            if (!File.Exists(_cachePath))
            {
                _entries = new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
                return;
            }

            var json = File.ReadAllText(_cachePath);
            _entries = JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(json, JsonOptions)
                ?? new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
        }
        catch
        {
            _entries = new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
        }
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(_entries, JsonOptions);
        var tmp = _cachePath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, _cachePath, overwrite: true);
    }

    private sealed record CacheEntry(ulong Hash);
}
