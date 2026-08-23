using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoard.Ingest.Steam;

/// <summary>
/// One Steam account found under <c>&lt;steam&gt;/userdata/</c>.
/// </summary>
/// <param name="Steam3Id">The steam3 account id (the userdata folder name).</param>
/// <param name="UserdataPath">Absolute path of <c>userdata/&lt;steam3id&gt;</c>.</param>
public sealed record SteamAccount(string Steam3Id, string UserdataPath)
{
    /// <summary>This account's localconfig.vdf (may not exist on disk).</summary>
    public string LocalConfigPath => Path.Combine(UserdataPath, "config", "localconfig.vdf");
}

/// <summary>
/// Enumerates <c>userdata/&lt;steam3id&gt;</c> account folders. Machines
/// routinely have several accounts (spike §3 trap 6) — callers must consider
/// all of them, not just the first. Missing directory yields an empty list.
/// </summary>
public sealed class SteamAccountEnumerator
{
    private readonly ILogger<SteamAccountEnumerator> _logger;

    public SteamAccountEnumerator(ILogger<SteamAccountEnumerator>? logger = null)
        => _logger = logger ?? NullLogger<SteamAccountEnumerator>.Instance;

    public IReadOnlyList<SteamAccount> Enumerate(string steamRoot)
    {
        var userdata = Path.Combine(steamRoot, "userdata");
        if (!Directory.Exists(userdata))
        {
            _logger.LogDebug("No userdata directory at {Path}", userdata);
            return [];
        }

        var accounts = new List<SteamAccount>();
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(userdata))
            {
                var name = Path.GetFileName(directory);

                // Steam3 ids are positive integers; "0" is the anonymous
                // account and holds no library data.
                if (ulong.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                    && id > 0)
                {
                    accounts.Add(new SteamAccount(name, directory));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Failed to enumerate Steam accounts under {Path}", userdata);
        }

        accounts.Sort(static (a, b) => string.CompareOrdinal(a.Steam3Id, b.Steam3Id));
        return accounts;
    }
}
