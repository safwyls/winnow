using System.Globalization;

namespace Winnow.Enrich.SteamWeb;

/// <summary>
/// A Steam account identity, carried as the 64-bit SteamID64 form the Web API
/// takes and convertible back to the steam3 account id the local scan uses.
/// </summary>
public readonly record struct SteamId
{
    /// <summary>
    /// <c>0x0110000100000000</c>. Added to a steam3 account id to obtain the
    /// SteamID64 of an individual account.
    /// </summary>
    public const ulong SteamId64Base = 76561197960265728UL;

    /// <summary>
    /// Highest SteamID64 an individual account can hold — the base plus the
    /// full 32-bit account-id space. Anything above this is not an individual
    /// account id and is rejected rather than silently truncated.
    /// </summary>
    public const ulong MaxIndividualSteamId64 = SteamId64Base + uint.MaxValue;

    private SteamId(ulong steamId64) => Value = steamId64;

    /// <summary>The SteamID64, as <c>GetOwnedGames</c>' <c>steamid</c> parameter wants it.</summary>
    public ulong Value { get; }

    /// <summary>
    /// The steam3 account id — the <c>userdata/&lt;steam3id&gt;</c> folder name,
    /// and therefore the value that appears as
    /// <c>CandidateOwnership.AccountRef</c> on locally-scanned candidates. Kept
    /// so a Web API candidate attributes to the same account as its local twin.
    /// </summary>
    public uint AccountId => (uint)(Value - SteamId64Base);

    /// <summary>The steam3 account id as the string form the local scan uses.</summary>
    public string AccountRef => AccountId.ToString(CultureInfo.InvariantCulture);

    /// <summary>The SteamID64 as an invariant decimal string.</summary>
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Builds an id from a steam3 account id — the <c>userdata</c> folder name.
    /// Account id 0 is Steam's anonymous account and holds no library, so it is
    /// rejected the same way <c>SteamAccountEnumerator</c> rejects it.
    /// </summary>
    public static SteamId? FromAccountId(ulong accountId)
        => accountId is > 0 and <= uint.MaxValue ? new SteamId(SteamId64Base + accountId) : null;

    /// <summary>Wraps an already-64-bit id, rejecting anything outside the individual-account range.</summary>
    public static SteamId? FromSteamId64(ulong steamId64)
        => steamId64 > SteamId64Base && steamId64 <= MaxIndividualSteamId64
            ? new SteamId(steamId64)
            : null;

    /// <summary>
    /// Parses either form from text, which is what callers actually hold: the
    /// steam3 folder name from a local scan, or a SteamID64 a user pasted into
    /// settings.
    ///
    /// <para>The two ranges do not overlap — an individual SteamID64 is always
    /// greater than <see cref="SteamId64Base"/> and a steam3 account id always
    /// fits in 32 bits — so the discrimination is exact rather than a guess.</para>
    /// </summary>
    public static bool TryParse(string? value, out SteamId steamId)
    {
        steamId = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!ulong.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        var result = parsed <= uint.MaxValue ? FromAccountId(parsed) : FromSteamId64(parsed);
        if (result is not { } id)
        {
            return false;
        }

        steamId = id;
        return true;
    }
}
