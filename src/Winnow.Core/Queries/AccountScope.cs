namespace Winnow.Core.Queries;

/// <summary>
/// Which local accounts the library is drawn from. A stored user fact, read
/// inside the derived-bucket query itself rather than passed down from a caller
/// — the same discipline as the <c>update_acknowledgements</c> watermark, and
/// for the same reason: the grid, the rail counts, the filter chips, the
/// recommender and the feed must all answer this question identically, and a
/// second consumer answering it separately is how they begin to disagree.
///
/// <para>This is deliberately NOT part of <see cref="BucketThresholds"/>. Those
/// exist to be retuned; this is a preference the user set, and no retuning may
/// put back a library they asked to narrow.</para>
/// </summary>
public static class AccountScope
{
    /// <summary>Settings key holding the preference. Absent means <see cref="All"/>.</summary>
    public const string SettingKey = "library.account_scope";

    /// <summary>
    /// Every Steam account signed in on this machine. The default, and what
    /// every install did before the toggle existed.
    /// </summary>
    public const string All = "all";

    /// <summary>
    /// Only the account the configured Steam Web API key belongs to. Never
    /// reached unless <see cref="SteamOwnedAccount.RefSettingKey"/> also holds
    /// a confirmed account, so a stored <c>own</c> on a machine that lost its
    /// key degrades to <see cref="All"/> rather than to an empty library.
    /// </summary>
    public const string Own = "own";

    /// <summary>
    /// Parses stored preference text. Anything that is not exactly
    /// <see cref="Own"/> is <see cref="All"/> — an unwritten key, a blank, and
    /// a value from a future version all mean "show everything", which is the
    /// answer that cannot hide a game the user owns.
    /// </summary>
    public static string Parse(string? stored)
        => string.Equals(stored?.Trim(), Own, StringComparison.Ordinal) ? Own : All;

    /// <summary>Formats the preference for storage. Round-trips with <see cref="Parse"/>.</summary>
    public static string Format(bool ownAccountOnly) => ownAccountOnly ? Own : All;

    /// <summary>Whether stored text asks for the filtered view.</summary>
    public static bool IsOwnOnly(string? stored) => Parse(stored) == Own;
}

/// <summary>
/// Which Steam account is <em>the user's</em>, as distinct from the other
/// accounts that happen to be signed in on the same PC.
///
/// <para>Winnow cannot ask Steam "who are you"; it can only observe that a
/// Web API call made with the configured key answered <em>for</em> a particular
/// account. <c>SteamPlaytimeBackfillService</c> is where that observation
/// happens, and it records the answer here so every later read is a settings
/// lookup rather than a network call.</para>
/// </summary>
public static class SteamOwnedAccount
{
    /// <summary>
    /// Settings key holding the Steam3 account id the configured Web API key
    /// was observed to belong to. Blank or absent means "not confirmed", which
    /// is the state that keeps the visibility toggle disabled.
    /// </summary>
    public const string RefSettingKey = "steam.owned_account_ref";

    /// <summary>
    /// Settings key holding a fingerprint of the API key that earned the
    /// confirmation. <b>Never the key itself</b> — a one-way digest, stored only
    /// so a changed key can be recognised as changed.
    ///
    /// <para>Without it a user who pastes a second person's key keeps the first
    /// person's account id, and the filter would then hide their own games in
    /// favour of a stranger's. The fingerprint makes "the key changed" a fact
    /// the app can notice, and noticing it clears
    /// <see cref="RefSettingKey"/> until a fresh call re-earns it.</para>
    /// </summary>
    public const string KeyFingerprintSettingKey = "steam.owned_account_key";

    /// <summary>
    /// Blank is the same absence as never-written, matching how the Epic token
    /// store already clears a value it cannot delete (<c>ISettingsRepository</c>
    /// has no remove).
    /// </summary>
    public static string? Clean(string? stored)
        => string.IsNullOrWhiteSpace(stored) ? null : stored.Trim();
}
