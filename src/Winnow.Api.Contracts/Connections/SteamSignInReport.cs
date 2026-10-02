using Winnow.Core.Auth;
using Winnow.Core.Ingest;
using Winnow.Enrich.SteamWeb.Credentials;
namespace Winnow.App.Services;
/// <summary>
/// What a completed sign-in attempt leaves behind, with nothing secret in it.
///
/// <para>The type S5's Stores screen binds to. It carries no token and no
/// refresh token by construction rather than by redaction: a view model cannot
/// leak a credential it was never handed. What it does carry is every fact the
/// screen has to be honest about — whether the session can be renewed, whether
/// it survived to disk, and what state it is in.</para>
/// </summary>
/// <param name="Outcome">How the browser session ended.</param>
/// <param name="Detail">A safe one-line reason, fit to show a user.</param>
/// <param name="SteamId">The account that signed in, or null when none did.</param>
/// <param name="ExpiresAt">When the access token dies, read from the token itself.</param>
/// <param name="RefreshTokenCaptured">
/// Whether a refresh token was captured. False means a working session that
/// cannot be renewed: it lasts about a day and unattended syncs will stop when
/// it does, which is the sentence the screen has to say out loud.
/// </param>
/// <param name="Persisted">
/// Whether the session reached the encrypted store. False on a host that cannot
/// encrypt, where the session still works for this run and has to be repeated
/// after a restart.
/// </param>
/// <param name="Health">The state the Stores screen renders.</param>
/// <param name="AccountConfirmed">
/// Whether this sign-in recorded which Steam account is the user's. TASK-55's
/// acceptance criterion 4: the visibility toggle is live the moment the window
/// closes, with no import, no Year in Review call and no waiting.
/// </param>
/// <param name="Pages">
/// The account pages, when the user agreed to that capture in the same session.
/// Null is the ordinary case and is not a failure.
/// </param>
public sealed record SteamSignInReport(
    SteamSignInOutcome Outcome,
    string? Detail,
    string? SteamId,
    DateTimeOffset? ExpiresAt,
    bool RefreshTokenCaptured,
    bool Persisted,
    SteamSessionHealth Health,
    SteamAccountPages? Pages,
    bool AccountConfirmed = false)
{

/// <summary>Whether a credential came out of this and is now the provider's.</summary>
    public bool SignedIn => Outcome == SteamSignInOutcome.SignedIn;
}
