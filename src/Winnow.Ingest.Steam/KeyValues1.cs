using System.Globalization;
using Winnow.Core.Domain;
using Microsoft.Extensions.Logging;
using ValveKeyValue;

namespace Winnow.Ingest.Steam;

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

    /// <summary>
    /// Reads an epoch-seconds value as UTC, mapping Steam's placeholders to
    /// null via the shared <see cref="SteamTime"/> rule — the same one the Web
    /// API reader applies to <c>rtime_last_played</c>, so the two sources agree
    /// on what "unknown" looks like instead of one of them inventing 1970.
    /// </summary>
    internal static DateTime? GetEpochUtc(KVObject parent, string name)
        => SteamTime.FromEpochSeconds(GetLong(parent, name));
}
