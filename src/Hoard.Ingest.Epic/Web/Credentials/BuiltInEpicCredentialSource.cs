namespace Hoard.Ingest.Epic.Web.Credentials;

/// <summary>
/// Epic's launcher OAuth client, embedded in Hoard. The LAST source consulted,
/// so anything the user supplies wins.
///
/// <para><b>This reverses an earlier decision, and the reversal is the point of
/// this comment.</b> The original module shipped no credentials at all: the pair
/// was a setting like the Steam key and the IGDB pair, and
/// <c>docs/spikes/epic-oauth.md</c> §10 recorded the reasoning — "no credential
/// Hoard has no right to enters the repository or a shipped binary", and "the
/// decision to impersonate Epic's launcher is made by the person doing it". That
/// reasoning is still correct on its own terms. It was overruled on 2026-08-26
/// because it does not survive the feature this file exists for.</para>
///
/// <para><b>Why it was overruled.</b> A sign-in BUTTON cannot ask a user for an
/// OAuth client id and secret; a flow that opens with "paste a credential you
/// will have to find yourself, from a launcher binary, on a forum" is not a
/// sign-in, it is a research assignment. And there is no version where the user
/// supplies their own, because Epic issues no client that can read a personal
/// library: Epic Account Services will register anyone an application, but its
/// consent scopes stop at <c>basic_profile</c> / <c>friends_list</c> /
/// <c>presence</c> / <c>country</c>, and <c>library:public:items</c> exists only
/// on the launcher client. So the choice was never "Hoard's credentials or the
/// user's". It was "these credentials, or the feature does not exist".</para>
///
/// <para><b>What that costs, stated rather than glossed.</b> Epic considers this
/// unsupported and has said so on the record — <i>"we do not offer or expose an
/// API for these specific items, and it is not something we would be able to
/// support"</i>. Their ToS §3 prohibits the reverse engineering that produced
/// this pair. Hoard is now the party distributing it, not the user, which is a
/// real transfer of responsibility and not a technicality. Every other tool in
/// this space — Legendary, Heroic, Rare, the Playnite plugins — embeds the same
/// pair, and Epic has not rotated it since 2020, but six years of tolerance is
/// not permission. The realistic failure mode remains breakage rather than bans:
/// see <c>docs/spikes/epic-oauth.md</c> §12, which the user should read before
/// signing in, and <c>ROADMAP.md</c> §3, which records the decision.</para>
///
/// <para><b>Last in the chain, deliberately.</b> A user who has their own pair —
/// from Epic, from a fork, from a future world where Epic ships a supported
/// client — sets it in the settings table or in configuration and it takes
/// precedence, with no code change and no way for this source to shadow it. The
/// day Epic rotates this pair, that is also the workaround.</para>
///
/// <para><b>These values are verified, not remembered.</b> The pair below
/// returned HTTP 200 from the live token endpoint on 2026-08-26. Do not
/// "correct" it from memory or from a search result — if it stops working, spend
/// a real request to find out what replaced it and record the date here.</para>
/// </summary>
public sealed class BuiltInEpicCredentialSource : IEpicCredentialSource
{
    /// <summary>
    /// Epic's launcher client id (<c>launcherAppClient2</c>). Not a secret in any
    /// meaningful sense — it identifies which client is being impersonated, and
    /// it is in every one of the tools named above.
    /// </summary>
    private const string LauncherClientId = "34a02cf8f4414e29b15921876da36f9a";

    /// <summary>
    /// Epic's launcher client secret. Extracted from the launcher binary by
    /// others and publicly circulated since 2020; unrotated as of 2026-08-26.
    ///
    /// <para>It is a plain constant rather than something obfuscated because
    /// obfuscation here would be theatre: the value is one HTTP request away from
    /// anyone running the binary, it is already public, and encoding it would only
    /// make the next person reading this file believe Hoard is protecting
    /// something it is not.</para>
    /// </summary>
    private const string LauncherClientSecret = "daafbccc737745039dffe53d94fc76cf";

    /// <inheritdoc/>
    public string Name => "built-in";

    /// <inheritdoc/>
    public ValueTask<EpicClientCredentials?> TryGetAsync(CancellationToken ct = default)
        => ValueTask.FromResult(EpicClientCredentials.TryCreate(LauncherClientId, LauncherClientSecret, Name));
}
