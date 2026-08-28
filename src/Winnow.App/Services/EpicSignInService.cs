using Winnow.Ingest.Epic.Web;
using Winnow.Ingest.Epic.Web.Auth;
using Microsoft.Extensions.Logging;

namespace Winnow.App.Services;

/// <summary>
/// The app-layer seam a "Sign in to Epic" command binds to.
///
/// <para><b>This exists so a view model never touches an ingest component.</b>
/// §5.1 is explicit that the UI reads the database and raises commands, and
/// composition belongs to <c>Program</c>. A button that resolved
/// <c>EpicInteractiveSignIn</c> or <c>IEpicTokenProvider</c> directly would put
/// the ingest layer in the view model's constructor and quietly delete that
/// boundary; this one type is what a command talks to instead.</para>
///
/// <para><b>It is deliberately not a view model and has no UI in it.</b> Where
/// the sign-in is offered, what it looks like and how its result is presented
/// are separate decisions. This answers three questions — is a session live,
/// start one, end one — and nothing else.</para>
///
/// <para><b>Nothing here throws.</b> Every failure comes back as an
/// <see cref="EpicSignInResult"/> with a reason, and every one of them leaves
/// the local Epic readers, and therefore the user's Epic library, exactly as
/// they were.</para>
/// </summary>
public sealed class EpicSignInService
{
    private readonly EpicInteractiveSignIn _signIn;
    private readonly IEpicAccountClient _client;
    private readonly ILogger<EpicSignInService> _log;

    public EpicSignInService(
        EpicInteractiveSignIn signIn, IEpicAccountClient client, ILogger<EpicSignInService> log)
    {
        _signIn = signIn;
        _client = client;
        _log = log;
    }

    /// <summary>
    /// Which prompt and mechanism last captured a code — "embedded browser via
    /// launcher JS bridge", say. Null until a sign-in succeeds in this process.
    ///
    /// <para>Worth surfacing somewhere eventually: of the three capture routes
    /// the embedded browser arms, one is an unverified hypothesis, and this is
    /// how anyone finds out which one Epic actually exercises. It never contains
    /// a code.</para>
    /// </summary>
    public string? LastCaptureRoute => _signIn.LastCaptureRoute;

    /// <summary>
    /// Whether a stored session exists that is still worth trying. Makes no
    /// network request.
    /// </summary>
    public ValueTask<bool> IsSignedInAsync(CancellationToken ct = default)
        => _client.IsSignedInAsync(ct);

    /// <summary>
    /// Runs the interactive sign-in: consent, the provider's own page, a code,
    /// and an encrypted session.
    ///
    /// <para>Safe to invoke from a UI command. It is long-running by nature — it
    /// waits on a human — so a caller must not block the UI thread on it, but the
    /// browser window it opens is its own and does not freeze the app.</para>
    /// </summary>
    public async Task<EpicSignInResult> SignInAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await _signIn.SignInAsync(ct);

            if (result.Succeeded)
            {
                _log.LogInformation(
                    "Epic sign-in completed ({Route}). The session is {Persistence}.",
                    _signIn.LastCaptureRoute ?? "route not recorded",
                    result.Persisted ? "stored encrypted" : "held in memory for this run only");
            }

            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The app is shutting down underneath the sign-in. Not a failure to
            // report, and not something to swallow into a misleading result.
            throw;
        }
        catch (Exception ex)
        {
            // The contract says none of this throws, and this is the belt that
            // makes it true at the boundary with the UI: an exception surfacing
            // into a command handler would take down a window over an optional
            // feature. Type only — a browser or HTTP exception message can quote
            // a URL, and this flow's URLs carry codes.
            _log.LogWarning(
                "Epic sign-in failed unexpectedly ({ExceptionType}). Nothing was changed and the local "
                + "Epic readers are unaffected.",
                ex.GetType().Name);
            return EpicSignInResult.Failed(EpicSignInFailure.UnexpectedResponse);
        }
    }

    /// <summary>
    /// Ends the session and deletes the stored credential.
    ///
    /// <para>Epic ownership then falls back to the local launcher files, which is
    /// where it comes from by default anyway — signing out costs acquisition
    /// dates and playtime, never games.</para>
    /// </summary>
    public Task SignOutAsync(CancellationToken ct = default) => _client.SignOutAsync(ct);

    /// <summary>
    /// One sentence a UI can show for a failure. Each says what to DO, because
    /// the remedies here are genuinely different from one another and a generic
    /// "sign-in failed" would send the user to the wrong one.
    /// </summary>
    public static string Explain(EpicSignInFailure failure) => failure switch
    {
        EpicSignInFailure.None =>
            "Signed in.",
        EpicSignInFailure.Cancelled =>
            "Sign-in cancelled. Nothing was changed.",
        EpicSignInFailure.NoInteractivePrompt =>
            "This machine cannot show a sign-in window — there is no WebView2 runtime and no console. "
            + "Run 'dotnet run --project src/Winnow.App -- --epic-login' from a terminal instead.",
        EpicSignInFailure.NoAuthenticatedSession =>
            "The sign-in window closed without an Epic account being signed in, so Epic would not issue a "
            + "code. Nothing was changed — try again and complete the sign-in on Epic's page.",
        EpicSignInFailure.NoCodeCaptured =>
            "Epic's sign-in page finished without handing back a code. This usually means Epic changed "
            + "the page; the manual flow ('--epic-login') still works. Epic ownership is unchanged.",
        EpicSignInFailure.InvalidAuthorizationCode =>
            "Epic rejected the code. Codes are single-use and expire within minutes, so the usual cause "
            + "is that it was already spent or is stale. Try signing in again.",
        EpicSignInFailure.InvalidClientCredentials =>
            "Epic rejected the OAuth client itself, not the sign-in. Epic may have rotated the launcher "
            + "credentials; see docs/spikes/epic-oauth.md.",
        EpicSignInFailure.Unreachable =>
            "Could not reach Epic. Nothing was changed; try again.",
        EpicSignInFailure.NotConfigured =>
            "No Epic OAuth client credentials are available.",
        _ =>
            "Epic answered with something Winnow did not understand. Nothing was changed.",
    };
}
