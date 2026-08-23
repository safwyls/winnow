using System.Globalization;
using Microsoft.Extensions.Logging;
using ValveKeyValue;

namespace Hoard.Ingest.Steam;

/// <summary>
/// Shared helpers for reading Steam KeyValues1 files with ValveKeyValue (§9:
/// never hand-roll a VDF parser). All lookups are case-insensitive: on-disk
/// key casing is inconsistent even within a single file (`appid` vs
/// `StateFlags` vs `lastupdated` — see docs/spikes/steam-local-files.md), and
/// Valve's own KeyValues is case-insensitive.
/// </summary>
internal static class KeyValues1
{
    // Steam writes escaped backslashes into paths ("C:\\Program Files (x86)\\Steam");
    // ValveKeyValue leaves them doubled unless escape-sequence handling is enabled.
    private static readonly KVSerializerOptions Options = new() { HasEscapeSequences = true };

    /// <summary>
    /// Opens and parses a text-KV1 file, returning null (never throwing) when
    /// the file is missing, locked, or malformed. Steam owns these files and
    /// is an eventually-consistent writer (§4.1) — tolerate anything.
    /// </summary>
    internal static KVDocument? TryLoad(string path, ILogger logger)
    {
        if (!File.Exists(path))
        {
            logger.LogDebug("Steam file not found (skipping): {Path}", path);
            return null;
        }

        try
        {
            // Steam may hold the file open for writing; share as widely as possible.
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(stream, Options);
        }
#pragma warning disable CA1031 // deliberate: a torn/exotic file from Steam must degrade to "no data", not crash ingest
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogWarning(ex, "Failed to read Steam KeyValues file {Path}", path);
            return null;
        }
    }

    /// <summary>Case-insensitive child lookup (first match wins, KV1 collections allow duplicates).</summary>
    internal static KVObject? Child(KVObject parent, string name)
    {
        foreach (var pair in parent.Children)
        {
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }

    internal static string? GetString(KVObject parent, string name)
    {
        var child = Child(parent, name);
        return child is { IsCollection: false, IsNull: false }
            ? child.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    internal static long? GetLong(KVObject parent, string name)
        => long.TryParse(
            GetString(parent, name),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;

    /// <summary>Reads an epoch-seconds value as UTC, mapping sentinels to null.</summary>
    internal static DateTime? GetEpochUtc(KVObject parent, string name)
        => SteamTime.FromEpochSeconds(GetLong(parent, name));
}

/// <summary>Epoch conversions with Steam's sentinel handling.</summary>
internal static class SteamTime
{
    /// <summary>
    /// 1980-01-01T00:00:00Z. Steam writes `LastPlayed "86400"` (1970-01-02)
    /// for games last played before it tracked timestamps, and `"0"` for
    /// never-launched installs; anything below this floor means "unknown".
    /// </summary>
    internal const long MinValidEpochSeconds = 315_532_800;

    internal static DateTime? FromEpochSeconds(long? seconds)
        => seconds is { } s && s >= MinValidEpochSeconds
            ? DateTimeOffset.FromUnixTimeSeconds(s).UtcDateTime
            : null;
}
