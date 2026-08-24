namespace Hoard.Covers;

/// <summary>
/// <c>%LOCALAPPDATA%\Hoard\covers\</c>, keyed by provider + id. Three artefacts
/// per key:
/// <list type="bullet">
/// <item><c>{stem}.src.jpg</c> — the bytes exactly as downloaded, so a retuned
/// dormancy floor or a larger display can be re-derived without re-fetching.</item>
/// <item><c>{stem}.floor.v{N}.jpg</c> — the pre-computed dormancy floor variant
/// the two-layer cross-fade needs. The version is part of the name so that
/// retuning the floor matrix orphans the old variants instead of silently
/// serving them: bump <see cref="FloorVariantVersion"/> and every tile
/// re-derives from its retained <c>.src.jpg</c>, with no re-fetching.</item>
/// <item><c>{stem}.none</c> — a negative marker. A 404 is a normal outcome and
/// must not cost a request on every launch; the file's write time carries the
/// TTL and its contents carry the <see cref="CoverSourceSet"/> identity that
/// produced it, so registering a new source retires prior negatives instead of
/// leaving the user a month of placeholder art.</item>
/// </list>
/// Writes are temp-file + move, so a kill mid-write never leaves a truncated
/// JPEG that would decode as garbage on the next run.
/// </summary>
public sealed class CoverDiskCache
{
    private readonly CoverCacheOptions _options;

    public CoverDiskCache(CoverCacheOptions options)
    {
        _options = options;
        Root = options.CacheDirectory;
    }

    public string Root { get; }

    public string SourcePath(CoverKey key) => Path.Combine(Root, key.CacheStem + ".src.jpg");

    /// <summary>
    /// Bumped whenever <see cref="CoverImaging.FloorMatrix"/> changes. v2 added
    /// the §1 cool shift and raised the brightness floor to 0.68.
    /// </summary>
    public const int FloorVariantVersion = 2;

    public string FloorPath(CoverKey key) =>
        Path.Combine(Root, $"{key.CacheStem}.floor.v{FloorVariantVersion}.jpg");

    public string NegativePath(CoverKey key) => Path.Combine(Root, key.CacheStem + ".none");

    /// <summary>
    /// True when a previous run established this key has no art anywhere, still
    /// within TTL <em>and</em> still under the same set of sources.
    ///
    /// <para>A marker whose recorded identity differs from
    /// <paramref name="sourceSetId"/> is deleted and reported as unknown: the
    /// question it answered is not the question being asked. Markers written
    /// before identities existed are empty files, so they fail the comparison
    /// too and are retried — which is precisely what should happen to every
    /// <c>.none</c> that Steam wrote while it was the only source.</para>
    /// </summary>
    public bool IsKnownMissing(CoverKey key, string sourceSetId)
    {
        var path = NegativePath(key);
        if (!File.Exists(path))
        {
            return false;
        }

        if (!string.Equals(ReadMarkerSourceSet(path), sourceSetId, StringComparison.Ordinal))
        {
            TryDelete(path);
            return false;
        }

        var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(path);
        if (age <= _options.NegativeTtl)
        {
            return true;
        }

        TryDelete(path);
        return false;
    }

    /// <summary>
    /// Records that every source in <paramref name="sourceSetId"/> declined.
    /// The identity is the payload — see <see cref="IsKnownMissing"/>.
    /// </summary>
    public void MarkMissing(CoverKey key, string sourceSetId)
    {
        EnsureRoot();
        WriteAtomic(NegativePath(key), System.Text.Encoding.UTF8.GetBytes(sourceSetId ?? string.Empty));
    }

    /// <summary>
    /// The identity stamped on a marker, or <see langword="null"/> when it
    /// carries none (a pre-identity marker) or cannot be read.
    /// </summary>
    private static string? ReadMarkerSourceSet(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Length is 0 or > CoverSourceSet.MaxLength)
            {
                return null;
            }

            var text = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(path)).Trim();
            return text.Length == 0 ? null : text;
        }
        catch (IOException)
        {
            // A concurrent write. Treat as unreadable; the caller retries the
            // fetch, which is the safe direction to be wrong in.
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public bool TryReadSource(CoverKey key, out byte[] bytes) => TryRead(SourcePath(key), out bytes);

    public bool TryReadFloor(CoverKey key, out byte[] bytes) => TryRead(FloorPath(key), out bytes);

    public void WriteSource(CoverKey key, byte[] bytes)
    {
        EnsureRoot();
        WriteAtomic(SourcePath(key), bytes);
        TryDelete(NegativePath(key));
    }

    public void WriteFloor(CoverKey key, byte[] bytes)
    {
        EnsureRoot();
        WriteAtomic(FloorPath(key), bytes);
    }

    private void EnsureRoot() => Directory.CreateDirectory(Root);

    private static bool TryRead(string path, out byte[] bytes)
    {
        try
        {
            if (File.Exists(path))
            {
                bytes = File.ReadAllBytes(path);
                return bytes.Length > 0;
            }
        }
        catch (IOException)
        {
            // A concurrent write; treat as a miss and re-derive.
        }
        catch (UnauthorizedAccessException)
        {
        }

        bytes = [];
        return false;
    }

    private static void WriteAtomic(string path, byte[] bytes)
    {
        // A managed thread id is unique within one process and nowhere else.
        // Two Hoard processes over the same cache directory — a second launch
        // while the first is still warming, or an app running beside its own
        // test run — hand out thread id 1 apiece and collide on the temp name,
        // so one write truncates the other's buffer and the survivor moves a
        // half-file into place under a name that says it is complete. A GUID
        // costs one allocation per write and is unique across processes and
        // machines.
        var temp = path + "." + Guid.NewGuid().ToString("n") + ".tmp";
        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
        }
        catch (IOException)
        {
            TryDelete(temp);
        }
        catch (UnauthorizedAccessException)
        {
            TryDelete(temp);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
