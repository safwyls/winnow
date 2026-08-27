using System.Globalization;
using Hoard.Core.Auth;
using Hoard.Ingest.Epic.Web.Credentials;
using Microsoft.Extensions.Logging;

namespace Hoard.Ingest.Epic.Web.Auth;

/// <summary>
/// Drives one interactive Epic sign-in: builds the request, finds a prompt that
/// can run here, and spends whatever code comes back on the grant it belongs to.
///
/// <para><b>This is the only place that knows the mapping.</b> A prompt returns
/// a code and says which KIND it is; this class turns that into the right
/// <c>grant_type</c>. The prompt knows nothing about tokens or grants, and
/// <see cref="EpicTokenProvider"/> knows nothing about browsers — the seam
/// between them is <see cref="IInteractiveAuthPrompt"/>, which lives in
/// <c>Hoard.Core</c> so that this project never references a UI framework
/// (§5.1).</para>
///
/// <para><b>The prompt chain falls through in registration order.</b> The
/// embedded browser is registered first and the console second, so a machine
/// with a WebView2 runtime gets the automatic flow and one without — a headless
/// host, an unusual Windows install, a future where Epic breaks the embedded
/// page — gets the manual one. A prompt that reports itself unavailable, or that
/// runs and captures nothing, is skipped; a user who deliberately CANCELS is
/// not. Escalating past a cancel would put a second window in front of someone
/// who just closed the first.</para>
///
/// <para><b>Nothing here throws.</b> Every ending is an
/// <see cref="EpicSignInResult"/>, and every failing ending leaves the existing
/// local Epic ingest exactly as it was.</para>
/// </summary>
public sealed class EpicInteractiveSignIn
{
    /// <summary>
    /// What the user is told before anything opens.
    ///
    /// <para><b>The first paragraph is Epic's own warning, verbatim</b>, from the
    /// body of <c>id/api/redirect</c>. The console flow has always printed it at
    /// the moment the user copied the code. The embedded flow never shows the
    /// user a code at all, so that moment disappears — and the consent has to
    /// move somewhere deliberate rather than evaporate because the flow got
    /// smoother (<c>docs/spikes/embedded-auth.md</c> §8).</para>
    ///
    /// <para>The rest says the thing Epic's warning implies but does not spell
    /// out for this situation: Hoard is the third party, and what it will be
    /// holding afterwards is not a read-only library key.</para>
    /// </summary>
    public const string ConsentNotice =
        "Epic's own warning, on the page that issues this credential:\n"
        + "\n"
        + "    \"Do not share this code with any 3rd party service. It allows full\n"
        + "     access to your Epic account.\"\n"
        + "\n"
        + "Hoard is a 3rd party service. Signing in here gives Hoard a credential with\n"
        + "full access to your Epic account, and it keeps that credential — encrypted\n"
        + "with DPAPI, on this machine, renewing itself for as long as you use Hoard.\n"
        + "It is not limited to reading your library; Epic issues no narrower one.\n"
        + "\n"
        + "Hoard uses it for two things: your list of owned games, and per-game\n"
        + "playtime. It never sees your Epic password — you sign in on Epic's own\n"
        + "page. Hoard authenticates as Epic's launcher, which Epic does not support\n"
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

    /// <summary>
    /// Which prompt captured the code on the last successful run, and by which
    /// mechanism — "embedded browser via launcher bridge", say. Null until one
    /// succeeds.
    ///
    /// <para>Exists because the three capture routes cannot be told apart without
    /// a real sign-in, and one of them
    /// (<see cref="AuthCaptureStrategies.RedirectInterception"/>) is an
    /// unverified hypothesis. This is how the first person to run it finds out
    /// which one actually fired. It never contains a code.</para>
    /// </summary>
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

        return EpicSignInResult.Failed(EpicSignInFailure.NoCodeCaptured);
    }

    /// <summary>
    /// The one request every prompt is handed.
    ///
    /// <para><b>All three capture routes are armed at once, and that is not
    /// laziness.</b> The obvious reading of "try the bridge, then the redirect,
    /// then the DOM" is three sequential attempts — but each attempt is a whole
    /// interactive sign-in, and an authorization code is single-use and dies in
    /// minutes, so serialising them would make the user sign in up to three times
    /// and would burn a code on every miss. Armed together in one browser
    /// session they cost nothing extra: whichever mechanism Epic actually
    /// exercises fires first and the others never do. The result records which
    /// one it was, so a real sign-in settles the question the spike could
    /// not.</para>
    /// </summary>
    private AuthPromptRequest BuildRequest(string clientId)
    {
        var redirect = _options.LauncherRedirectUrl;

        var start = _options.UseAuthorizeEndpointForSignIn
            ? string.Format(
                CultureInfo.InvariantCulture,
                _options.AuthorizeUrlFormat,
                Uri.EscapeDataString(clientId),
                Uri.EscapeDataString(redirect.ToString()))
            : string.Format(CultureInfo.InvariantCulture, _options.AuthorizationCodeUrlFormat, clientId);

        return new AuthPromptRequest
        {
            ProviderName = "Epic Games",
            StartUrl = new Uri(start),
            RedirectUrl = redirect,
            ConsentNotice = ConsentNotice,
            Strategies = AuthCaptureStrategies.All,

            // Priority order, and the order matters: an exchange code arrives
            // through the bridge as a push, while these are read off a page that
            // may carry either. `authorizationCode` first because that is the
            // field Epic populates on this endpoint for a signed-in session; the
            // spike saw both present and null while unauthenticated.
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
