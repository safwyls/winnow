using System.Globalization;
using Winnow.Core.Ingest;
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
    private static readonly KVSerializerOptions Options = new() { HasEscapeSequences = true, FileLoader = new RejectIncludes() };

    /// <summary>
    /// Opens and parses a text-KV1 file, returning null (never throwing) when
    /// the file is missing, locked, or malformed. Steam owns these files and
    /// is an eventually-consistent writer (§4.1) — tolerate anything.
    /// </summary>
    internal static KVDocument? TryLoad(string path, ILogger logger, StorefrontParserLimits limits)
    {
        if (!File.Exists(path))
        {
            logger.LogDebug("Steam file not found (skipping): {Path}", path);
            return null;
        }

        try
        {
            var bytes = StorefrontFile.Read(path, limits);
            using var stream = new MemoryStream(bytes, writable: false);
            CheckTextDepth(stream, limits.MaxDepth);
            stream.Position = 0;
            return KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(stream, Options);
        }
#pragma warning disable CA1031 // deliberate: a torn/exotic file from Steam must degrade to "no data", not crash ingest
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogWarning("Failed to read Steam KeyValues file {Path} ({Failure})", path, ex.GetType().Name);
            return null;
        }
    }

    // This is a resource preflight, not a VDF parser. ValveKeyValue remains the
    // authority for syntax and values, but has no depth option to protect its recursion.
    private static void CheckTextDepth(Stream stream, int maxDepth)
    {
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var depth = 0;
        var quoted = false;
        var escaped = false;
        var comment = false;
        int next;
        while ((next = reader.Read()) != -1)
        {
            var character = (char)next;
            if (character == '\0')
                throw new IOException("Binary data is not supported by this text reader.");
            if (comment)
            {
                if (character is '\r' or '\n') comment = false;
                continue;
            }
            if (quoted)
            {
                if (escaped) escaped = false;
                else if (character == '\\') escaped = true;
                else if (character == '"') quoted = false;
                continue;
            }
            if (character == '"') quoted = true;
            else if (character == '/' && reader.Peek() == '/')
            {
                reader.Read();
                comment = true;
            }
            else if (character == '{' && ++depth > maxDepth)
                throw new IOException("Storefront file exceeds the configured nesting limit.");
            else if (character == '}' && --depth < 0)
                throw new IOException("Unbalanced storefront file.");
        }
    }

    private sealed class RejectIncludes : IIncludedFileLoader
    {
        public Stream OpenFile(string filePath)
            => throw new IOException("Includes are not supported in storefront files.");
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
