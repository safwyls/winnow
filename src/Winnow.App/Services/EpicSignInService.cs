using Winnow.Ingest.Epic.Web;
using Winnow.Ingest.Epic.Web.Auth;
using Microsoft.Extensions.Logging;

namespace Winnow.App.Services;

/// <summary>
/// App-layer seam for Epic sign-in (§5.1). Answers three questions: is a session
/// live, start one, end one. Never throws; failures come back as
/// <see cref="EpicSignInResult"/> with a reason.
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

    /// <summary>Which prompt mechanism last captured a code. Null until a sign-in succeeds in this process.</summary>
    public string? LastCaptureRoute => _signIn.LastCaptureRoute;

    /// <summary>Whether a stored session is still worth trying. No network request.</summary>
    public ValueTask<bool> IsSignedInAsync(CancellationToken ct = default)
        => _client.IsSignedInAsync(ct);

    /// <summary>Runs the interactive sign-in (consent, browser, code, encrypted session). Long-running; do not block the UI thread.</summary>
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

    /// <summary>Ends the session and deletes the stored credential. Ownership falls back to local files.</summary>
    public Task SignOutAsync(CancellationToken ct = default) => _client.SignOutAsync(ct);

    /// <summary>Returns a user-facing sentence for a failure, stating the specific remedy.</summary>
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
