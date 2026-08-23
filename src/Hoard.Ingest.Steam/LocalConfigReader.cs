using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoard.Ingest.Steam;

/// <summary>
/// Per-app playtime as recorded in one account's <c>localconfig.vdf</c>.
/// </summary>
/// <param name="AppId">Steam appid as a string.</param>
/// <param name="PlaytimeMinutes">Total playtime, in minutes (verified units — spike §3).</param>
/// <param name="Playtime2WeeksMinutes">Rolling two-week playtime in minutes; present only on recently played apps.</param>
/// <param name="LastPlayedUtc">Last played, UTC; null when absent or when Steam wrote the <c>86400</c> "unknown" sentinel.</param>
public sealed record SteamAppPlaytime(
    string AppId,
    long PlaytimeMinutes,
    long? Playtime2WeeksMinutes,
    DateTime? LastPlayedUtc);

/// <summary>
/// Reads the per-account playtime map from
/// <c>userdata/&lt;steam3id&gt;/config/localconfig.vdf</c>, navigating exactly
/// <c>UserLocalConfigStore / Software / Valve / Steam / apps</c>. The sibling
/// <c>apptickets</c> block is ALSO an appid-keyed map — grabbing the first
/// appid-keyed node is the documented false-match hazard, so never do that.
/// Read-only; missing or malformed file yields an empty map.
/// </summary>
public sealed class LocalConfigReader
{
    private static readonly string[] AppsPath = ["Software", "Valve", "Steam", "apps"];

    private readonly ILogger<LocalConfigReader> _logger;

    public LocalConfigReader(ILogger<LocalConfigReader>? logger = null)
        => _logger = logger ?? NullLogger<LocalConfigReader>.Instance;

    public IReadOnlyDictionary<string, SteamAppPlaytime> Read(string localConfigVdfPath)
    {
        var doc = KeyValues1.TryLoad(localConfigVdfPath, _logger);
        if (doc is null)
        {
            return new Dictionary<string, SteamAppPlaytime>(StringComparer.Ordinal);
        }

        var node = doc.Root;
        foreach (var segment in AppsPath)
        {
            var next = KeyValues1.Child(node, segment);
            if (next is null)
            {
                _logger.LogDebug(
                    "localconfig.vdf {Path} has no {Segment} node; no playtime data",
                    localConfigVdfPath, segment);
                return new Dictionary<string, SteamAppPlaytime>(StringComparer.Ordinal);
            }

            node = next;
        }

        var result = new Dictionary<string, SteamAppPlaytime>(StringComparer.Ordinal);
        foreach (var pair in node.Children)
        {
            if (string.IsNullOrEmpty(pair.Key) || pair.Value is not { IsCollection: true } app)
            {
                continue;
            }

            // Key order inside an app block is unstable and some blocks hold
            // only cloud/quota data — skip blocks without Playtime rather
            // than emitting zeros (spike §3 traps 2–3).
            var playtime = KeyValues1.GetLong(app, "Playtime");
            if (playtime is null)
            {
                continue;
            }

            result[pair.Key] = new SteamAppPlaytime(
                AppId: pair.Key,
                PlaytimeMinutes: playtime.Value,
                Playtime2WeeksMinutes: KeyValues1.GetLong(app, "Playtime2wks"),
                LastPlayedUtc: KeyValues1.GetEpochUtc(app, "LastPlayed"));
        }

        return result;
    }
}
