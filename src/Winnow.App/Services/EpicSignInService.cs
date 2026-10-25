using Winnow.Api.Contracts.Connections;
using Winnow.Core.Auth;
using Winnow.Ingest.Epic.Web.Auth;

namespace Winnow.App.Services;

public sealed class EpicSignInService(IStoreConnectionApi api, IEnumerable<IInteractiveAuthPrompt> prompts)
{
    public string? LastCaptureRoute { get; private set; }
    public async ValueTask<bool> IsSignedInAsync(CancellationToken ct = default) => (await api.GetAsync(ct)).Epic?.IsLive == true;
    public Task SignOutAsync(CancellationToken ct = default) => api.SignOutEpicAsync(ct);

    public async Task<EpicSignInResult> SignInAsync(CancellationToken ct = default)
    {
        var challenge = await api.BeginEpicAsync(ct);
        try
        {
            var failure = EpicSignInFailure.NoInteractivePrompt;
            foreach (var prompt in prompts)
            {
                if (!await prompt.IsAvailableAsync(ct)) continue;
                AuthCodeResult result;
                try { result = await prompt.RequestCodeAsync(challenge.Request, ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception) { failure = EpicSignInFailure.UnexpectedResponse; continue; }
                if (result.Outcome == AuthPromptOutcome.Cancelled) return EpicSignInResult.Failed(EpicSignInFailure.Cancelled);
                if (result.Outcome == AuthPromptOutcome.Captured && !string.IsNullOrWhiteSpace(result.Code))
                {
                    LastCaptureRoute = $"{prompt.Name} via {result.Via}";
                    return await api.CompleteEpicAsync(challenge, result, ct);
                }
                failure = result.Outcome == AuthPromptOutcome.NoSession ? EpicSignInFailure.NoAuthenticatedSession : EpicSignInFailure.NoCodeCaptured;
            }
            return EpicSignInResult.Failed(failure);
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await api.CancelAsync(challenge.AttemptId, cleanup.Token); }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException) { }
        }
    }
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
