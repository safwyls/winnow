using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Winnow.Ingest.Steam;

/// <summary>
/// One library root from <c>libraryfolders.vdf</c>.
/// </summary>
/// <param name="Path">Absolute root of the library (e.g. <c>D:\SteamLibrary</c>). Manifests live at <c>{Path}\steamapps</c>.</param>
/// <param name="Label">User-assigned label; empty string when unset.</param>
/// <param name="Apps">
/// Appid → size-on-disk in bytes. This is the authoritative "which appids live
/// in this root" list, but size can legitimately be 0 (app pending
/// install/update) — the appmanifest is the authority on install state.
/// </param>
public sealed record SteamLibraryFolder(
    string Path,
    string? Label,
    IReadOnlyDictionary<string, long> Apps)
{
    /// <summary>The steamapps directory holding this root's appmanifest_*.acf files.</summary>
    public string SteamAppsPath => System.IO.Path.Combine(Path, "steamapps");
}

/// <summary>
/// Reads library roots from <c>&lt;steam&gt;/steamapps/libraryfolders.vdf</c>
/// (§4.1). Read-only; missing or malformed file yields an empty list.
/// </summary>
public sealed class LibraryFoldersReader
{
    private readonly ILogger<LibraryFoldersReader> _logger;

    public LibraryFoldersReader(ILogger<LibraryFoldersReader>? logger = null)
        => _logger = logger ?? NullLogger<LibraryFoldersReader>.Instance;

    public IReadOnlyList<SteamLibraryFolder> Read(string libraryFoldersVdfPath)
    {
        var doc = KeyValues1.TryLoad(libraryFoldersVdfPath, _logger);
        if (doc is null)
        {
            return [];
        }

        var folders = new List<SteamLibraryFolder>();
        foreach (var pair in doc.Root.Children)
        {
            // Library entries are collection nodes keyed "0", "1", ...; older
            // clients also wrote scalar keys (e.g. contentstatsid) at this level.
            if (pair.Value is not { IsCollection: true } node)
            {
                continue;
            }

            var path = KeyValues1.GetString(node, "path");
            if (string.IsNullOrWhiteSpace(path))
            {
                _logger.LogWarning(
                    "libraryfolders.vdf entry '{Key}' has no path; skipping", pair.Key);
                continue;
            }

            var apps = new Dictionary<string, long>(StringComparer.Ordinal);
            if (KeyValues1.Child(node, "apps") is { } appsNode)
            {
                foreach (var app in appsNode.Children)
                {
                    if (string.IsNullOrEmpty(app.Key))
                    {
                        continue;
                    }

                    _ = long.TryParse(
                        app.Value.ToString(CultureInfo.InvariantCulture),
                        NumberStyles.Integer, CultureInfo.InvariantCulture, out var sizeBytes);
                    apps[app.Key] = sizeBytes;
                }
            }

            folders.Add(new SteamLibraryFolder(path, KeyValues1.GetString(node, "label"), apps));
        }

        return folders;
    }
}
