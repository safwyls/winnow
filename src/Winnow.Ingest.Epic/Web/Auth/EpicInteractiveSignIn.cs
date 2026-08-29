using System.Globalization;
using Winnow.Core.Auth;
using Winnow.Ingest.Epic.Web.Credentials;
using Microsoft.Extensions.Logging;

namespace Winnow.Ingest.Epic.Web.Auth;

/// <summary>
/// Drives one interactive Epic sign-in: builds the request, falls through
/// registered prompts in order, and exchanges the captured code for a session.
/// </summary>
public sealed class EpicInteractiveSignIn
{
    /// <summary>Consent notice shown before sign-in, starting with Epic's own warning.</summary>
    public const string ConsentNotice =
        "Epic's own warning, on the page that issues this credential:\n"
        + "\n"
        + "    \"Do not share this code with any 3rd party service. It allows full\n"
        + "     access to your Epic account.\"\n"
        + "\n"
        + "Winnow is a 3rd party service. Signing in here gives Winnow a credential with\n"
        + "full access to your Epic account, and it keeps that credential — encrypted\n"
        + "with DPAPI, on this machine, renewing itself for as long as you use Winnow.\n"
        + "It is not limited to reading your library; Epic issues no narrower one.\n"
        + "\n"
        + "Winnow uses it for two things: your list of owned games, and per-game\n"
        + "playtime. It never sees your Epic password — you sign in on Epic's own\n"
        + "page. Winnow authenticates as Epic's launcher, which Epic does not support\n"
        + "and does not sanction; docs/spikes/epic-oauth.md sets out what that means.\n"
        + "\n"
        + "You can undo this at any time by signing out, which deletes the stored\n"
        + "credential. Epic ownership then falls back to the local launcher files,\n"
        + "which is where it comes from today.";

    private readonly IInteractiveAuthPrompt[] _prompts;
    private readonly IEpicTokenProvider _tokens;
    private readonly IEpicCredentialProvider _credentials;
    private readonly EpicWebOptions _options;
    private readonly ILogger<EpicInteractiveSignIn> _log;

    public EpicInteractiveSignIn(
        IEnumerable<IInteractiveAuthPrompt> prompts,
        IEpicTokenProvider tokens,
        IEpicCredentialProvider credentials,
        EpicWebOptions options,
        ILogger<EpicInteractiveSignIn> log)
    {
        _prompts = prompts.ToArray();
        _tokens = tokens;
        _credentials = credentials;
        _options = options;
        _log = log;
    }

    /// <summary>Which prompt and mechanism captured the code on the last successful run. Null until one succeeds.</summary>
    public string? LastCaptureRoute { get; private set; }

    /// <summary>
    /// Runs the flow. Returns a session, or a reason there is none.
    /// </summary>
    public async Task<EpicSignInResult> SignInAsync(CancellationToken ct = default)
    {
        if (await _credentials.GetAsync(ct) is not { } credentials)
        {
            // Effectively unreachable now that a built-in pair is the last
            // source, but the module is registerable without it and a future
            // rotation could empty every source. Answering NotConfigured is
            // still the honest outcome.
            return EpicSignInResult.Failed(EpicSignInFailure.NotConfigured);
        }

        var request = BuildRequest(credentials.ClientId);

        var attempted = false;
        var lastFailure = EpicSignInFailure.NoCodeCaptured;
        foreach (var prompt in _prompts)
        {
            if (!await prompt.IsAvailableAsync(ct))
            {
                _log.LogDebug("Interactive sign-in prompt {Prompt} is not available here; trying the next.", prompt.Name);
                continue;
            }

            attempted = true;

            AuthCodeResult captured;
            try
            {
                captured = await prompt.RequestCodeAsync(request, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A prompt is UI code and UI code throws in ways a contract
                // cannot enumerate — a window created on the wrong thread, a
                // runtime that half-loaded, a native handle that went away. The
                // contract says a prompt never throws; this is the belt that
                // makes it true even when an implementation forgets, because the
                // one thing that must not happen is an exception surfacing into
                // the app and taking the local Epic ingest down with it.
                // Type only: a message from a browser host can quote a URL, and
                // this flow's URLs carry codes.
                _log.LogWarning(
                    "The {Prompt} sign-in prompt failed ({ExceptionType}); trying the next.",
                    prompt.Name, ex.GetType().Name);
                continue;
            }

            switch (captured.Outcome)
            {
                case AuthPromptOutcome.Captured when !string.IsNullOrWhiteSpace(captured.Code):
                    LastCaptureRoute = string.Create(
                        CultureInfo.InvariantCulture, $"{prompt.Name} via {captured.Via}");
                    _log.LogInformation(
                        "Captured an Epic {CodeKind} through the {Prompt} prompt ({Route}). Exchanging it now.",
                        captured.Kind, prompt.Name, captured.Via);

                    return captured.Kind == AuthCodeKind.ExchangeCode
                        ? await _tokens.SignInWithExchangeCodeAsync(captured.Code!, ct)
                        : await _tokens.SignInWithAuthorizationCodeAsync(captured.Code!, ct);

                case AuthPromptOutcome.NoSession:
                    // The provider answered, and answered that nobody is signed
                    // in. Distinct from a broken capture and worded as such —
                    // reporting this as "no code captured" is what made the first
                    // real run look like a bug in Winnow rather than a sign-in
                    // that never happened. Falls through like a failure, because
                    // the console peer can still get a code from a browser the
                    // user is already signed into.
                    _log.LogWarning(
                        "The {Prompt} prompt finished with no signed-in Epic account ({Detail}); "
                        + "trying the next.",
                        prompt.Name, captured.Detail ?? "no reason given");
                    lastFailure = EpicSignInFailure.NoAuthenticatedSession;
                    continue;

                case AuthPromptOutcome.Cancelled:
                    // Deliberate. Do NOT fall through to the next prompt: the
                    // user closed a window, and answering that by opening a
                    // different one is not a fallback, it is nagging.
                    _log.LogInformation("Epic sign-in was cancelled by the user. Nothing was changed.");
                    return EpicSignInResult.Failed(EpicSignInFailure.Cancelled);

                case AuthPromptOutcome.Unavailable:
                    _log.LogDebug(
                        "The {Prompt} prompt declined ({Detail}); trying the next.",
                        prompt.Name, captured.Detail ?? "no reason given");
                    continue;

                default:
                    _log.LogWarning(
                        "The {Prompt} prompt produced no Epic code ({Detail}); trying the next.",
                        prompt.Name, captured.Detail ?? "no reason given");
                    continue;
            }
        }

        if (!attempted)
        {
            _log.LogInformation(
                "No interactive sign-in prompt can run on this machine ({PromptCount} registered). "
                + "Epic ownership is unchanged and the local Epic readers are unaffected.",
                _prompts.Length);
            return EpicSignInResult.Failed(EpicSignInFailure.NoInteractivePrompt);
        }

        return EpicSignInResult.Failed(lastFailure);
    }

    /// <summary>Builds the prompt request with start and harvest URLs and all capture routes armed.</summary>
    private AuthPromptRequest BuildRequest(string clientId)
    {
        var redirect = _options.LauncherRedirectUrl;

        // The page that issues a code, given a session. Never the starting point.
        var harvest = new Uri(string.Format(
            CultureInfo.InvariantCulture, _options.AuthorizationCodeUrlFormat, clientId));

        var start = _options.UseAuthorizeEndpointForSignIn
            ? new Uri(string.Format(
                CultureInfo.InvariantCulture,
                _options.AuthorizeUrlFormat,
                Uri.EscapeDataString(clientId),
                Uri.EscapeDataString(redirect.ToString())))
            : harvest;

        return new AuthPromptRequest
        {
            ProviderName = "Epic Games",
            StartUrl = start,
            HarvestUrl = harvest,
            RedirectUrl = redirect,
            ConsentNotice = ConsentNotice,
            Strategies = AuthCaptureStrategies.All,

            // Priority order, and the order matters: an exchange code arrives
            // through the bridge as a push, while these are read off a body that
            // may carry either. `authorizationCode` first because that is the
            // field Epic populates on this endpoint for a signed-in session.
            //
            // Their PRESENCE is also how "nobody is signed in" is told apart from
            // "the page changed": Epic returns both fields, both null, for a
            // browser with no session — a real answer, not a failed capture. See
            // AuthCodeBody.
            JsonCodeFields =
            [
                new AuthJsonCodeField("authorizationCode", AuthCodeKind.AuthorizationCode),
                new AuthJsonCodeField("exchangeCode", AuthCodeKind.ExchangeCode),
            ],

            // Its own browser profile, so an Epic session is not shared with any
            // other provider added later and a half-finished attempt can be
            // resumed without retyping a password.
            ProfileKey = "epic",
        };
    }
}
