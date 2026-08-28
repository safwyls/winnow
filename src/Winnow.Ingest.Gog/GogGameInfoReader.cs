using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Winnow.Ingest.Gog;

/// <summary>
/// Reads <c>goggame-&lt;gameId&gt;.info</c> files out of a game's install
/// directory. Read-only; a missing or malformed file yields null and never an
/// exception.
///
/// <para>Sibling files in the same directory (<c>goggame-&lt;id&gt;.hashdb</c>,
/// <c>.ico</c>, <c>goggame-galaxyFileList.ini</c>, <c>goglog.ini</c>) are not
/// needed and are not opened.</para>
/// </summary>
public sealed class GogGameInfoReader
{
    private readonly ILogger<GogGameInfoReader> _logger;

    /// <param name="logger">Optional logger.</param>
    public GogGameInfoReader(ILogger<GogGameInfoReader>? logger = null)
        => _logger = logger ?? NullLogger<GogGameInfoReader>.Instance;

    /// <summary>Reads the <c>.info</c> for one product id in an install directory, or null.</summary>
    public GogGameInfo? ReadForGame(string installDirectory, string gameId)
    {
        ArgumentNullException.ThrowIfNull(installDirectory);
        ArgumentNullException.ThrowIfNull(gameId);

        return Read(Path.Combine(installDirectory, $"goggame-{gameId}.info"));
    }

    /// <summary>
    /// Reads every <c>goggame-*.info</c> in a directory. Used to see a base game's
    /// installed DLC without Galaxy.
    /// </summary>
    public IReadOnlyList<GogGameInfo> ReadDirectory(string installDirectory)
    {
        ArgumentNullException.ThrowIfNull(installDirectory);

        if (!Directory.Exists(installDirectory))
        {
            return [];
        }

        var infos = new List<GogGameInfo>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(
                         installDirectory, "goggame-*.info", SearchOption.TopDirectoryOnly))
            {
                if (!file.EndsWith(".info", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var info = Read(file);
                if (info is not null)
                {
                    infos.Add(info);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not enumerate goggame-*.info under {Path}", installDirectory);
        }

        return infos;
    }

    /// <summary>Reads one <c>.info</c> file, or null when it is missing, unreadable or has no <c>gameId</c>.</summary>
    public GogGameInfo? Read(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(filePath);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var gameId = StringOf(root, "gameId");
            if (gameId.Length == 0)
            {
                return null;
            }

            return new GogGameInfo(
                GameId: gameId,
                RootGameId: StringOf(root, "rootGameId"),
                Name: NullIfEmpty(StringOf(root, "name")),
                BuildId: NullIfEmpty(StringOf(root, "buildId")),
                PrimaryPlayTaskPath: PrimaryPlayTaskPath(root),
                FilePath: filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(ex, "Could not read {Path}; skipping", filePath);
            return null;
        }
    }

    /// <summary>
    /// The primary play task's path, falling back to the first task. Note the
    /// <c>clientId</c> alongside it in the same file pairs with a
    /// <c>clientSecret</c> in Galaxy's <c>ProductAuthorizations</c> table —
    /// neither is read here, and neither should ever be logged or fixtured.
    /// </summary>
    private static string? PrimaryPlayTaskPath(JsonElement root)
    {
        if (!root.TryGetProperty("playTasks", out var tasks) || tasks.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string? first = null;
        foreach (var task in tasks.EnumerateArray())
        {
            if (task.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var path = NullIfEmpty(StringOf(task, "path"));
            if (path is null)
            {
                continue;
            }

            first ??= path;
            if (task.TryGetProperty("isPrimary", out var primary) && primary.ValueKind == JsonValueKind.True)
            {
                return path;
            }
        }

        return first;
    }

    private static string StringOf(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string? NullIfEmpty(string value)
        => value.Length == 0 ? null : value;
}
