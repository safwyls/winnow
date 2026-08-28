using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.App.Services;

namespace Winnow.App.ViewModels;

/// <summary>
/// Which of the four things the Epic row can be. Kept as one enum rather than
/// three booleans because they are mutually exclusive and three booleans is how
/// a screen ends up drawing "signed in" and "not connected" at the same time.
/// </summary>
public enum EpicConnection
{
    /// <summary>No session, and none was expected. The ordinary state.</summary>
    SignedOut = 0,

    /// <summary>A sign-in is running. Can legitimately last minutes — a password and a 2FA code.</summary>
    SigningIn,

    /// <summary>A session exists and its refresh token is still worth spending.</summary>
    SignedIn,

    /// <summary>
    /// A session was stored and its refresh token lapsed while the app was
    /// closed. Distinct from <see cref="SignedOut"/> on purpose: something
    /// happened TO the user, and saying so is the difference between an
    /// explanation and Winnow appearing to have forgotten them.
    /// </summary>
    Lapsed,
}

/// <summary>
/// The Stores panel: the first screen in Winnow that says out loud which sources
/// feed the library and what each one can and cannot know.
///
/// <para><b>That honesty is the screen; the sign-in button is one row in
/// it.</b> Every number elsewhere in the app — a bucket count, a playtime
/// column, a dormancy fade — is downstream of what the ingest sources could see,
/// and until now nothing told the user what that was. An Epic game cannot enter
/// a playtime bucket at all, because Epic writes playtime nowhere on disk; a
/// Steam library with no Web API key is missing every game the user owns and has
/// never launched on this machine. Both are facts about the user's own library
/// that the library itself cannot state.</para>
///
/// <para><b>§5.1: nothing here touches ingest or a repository.</b> The panel
/// talks to <see cref="IStoreConnections"/>, an App-layer seam, and reads its
/// counts through <see cref="IStoreTitleCounts"/>, which
/// <see cref="LibraryViewModel"/> happens to implement. Both exist so this file
/// can be constructed in a test with two small fakes and no database.</para>
///
/// <para><b>No Flare anywhere on this screen, in any state.</b> Not for a failed
/// sign-in, not for "not connected", not for the button. Flare marks unread
/// updates and the bucket that counts them, and the badge's meaning survives
/// exactly as long as it is the only thing wearing that colour (§2, §5.2). The
/// palette this screen uses instead: <c>Volt</c> for a source that is giving
/// Winnow everything it can, <c>Amber</c> for something the user meant to be
/// working and is not, <c>TextDim</c> for an optional connection nobody has
/// made — which is a choice, not a fault, and must not look like one.</para>
/// </summary>
public partial class StoresViewModel : ObservableObject
{
    /// <summary>
    /// The console peer's command line, verbatim. Rendered in Plex Mono where a
    /// user can select and copy it — the one thing prose cannot do for a command.
    /// </summary>
    public const string ConsoleSignInCommand = "dotnet run --project src/Winnow.App -- --epic-login";

    /// <summary>
    /// <see cref="ConsoleSignInCommand"/>, as something a compiled binding can
    /// reach. A const field is not a bindable member, and the alternative — the
    /// literal typed into the markup — is the copy that quietly stops matching
    /// the argument <c>Program</c> actually accepts.
    /// </summary>
    public string ConsoleSignInCommandText => ConsoleSignInCommand;

    private readonly IStoreConnections _connections;
    private readonly IStoreTitleCounts? _counts;

    /// <param name="connections">The §5.1 seam. Required — the panel is nothing without it.</param>
    /// <param name="counts">
    /// Optional so the panel still composes for a host that has not registered a
    /// library view model. Without it the cards drop their title counts and say
    /// everything else, which is a smaller loss than a screen that will not open.
    /// </param>
    public StoresViewModel(IStoreConnections connections, IStoreTitleCounts? counts = null)
    {
        _connections = connections;
        _counts = counts;
    }

    // ══ Rail and header ═════════════════════════════════════════════════════

    /// <summary>The screen title. §7: name the thing the user recognises.</summary>
    public string Title => "Stores";

    /// <summary>
    /// The standing explanation. It leads with the part that is true of all
    /// three — everything here is optional — so that an unconnected row reads as
    /// a choice rather than as an unfinished setup step.
    /// </summary>
    public string IntroMessage =>
        "Where your library comes from. All three are read from files already on this machine, "
        + "and none of them needs an account for Winnow to work. Two of them know things those "
        + "files don't.";

    // ══ Steam ═══════════════════════════════════════════════════════════════

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(SteamStatusLabel), nameof(SteamStatusIsLive),
        nameof(SteamAddsMessage), nameof(ShowSteamKeyHint))]
    public partial bool SteamWebApiConfigured { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SteamCountText), nameof(ShowSteamCount))]
    public partial int SteamTitleCount { get; set; }

    /// <summary>Plex Mono, tabular, grouped — every number in the app (§3).</summary>
    public string SteamCountText => SteamTitleCount.ToString("N0");

    /// <summary>
    /// The count stands down before the library has loaded. A zero here would be
    /// a claim that Steam contributed nothing, which is a different statement
    /// from "we have not counted yet" and is the app's recurring failure mode —
    /// a source's silence recorded as an answer.
    /// </summary>
    public bool ShowSteamCount => SteamTitleCount > 0;

    public string SteamLocalMessage =>
        "Always on. Steam's own config files give Winnow the games on this machine, whether they're "
        + "installed or not, plus playtime and the date you last played each one.";

    public string SteamStatusLabel => SteamWebApiConfigured ? "WEB API KEY SET" : "NO WEB API KEY";

    public bool SteamStatusIsLive => SteamWebApiConfigured;

    /// <summary>
    /// The one fact the key changes, stated as what it costs rather than as a
    /// setup instruction. A user who never sets a key is not doing anything
    /// wrong; they are looking at a smaller library than they own, and only this
    /// screen can tell them so.
    /// </summary>
    public string SteamAddsMessage => SteamWebApiConfigured
        ? "Winnow also reads your full owned list from Steam, so games you've played on another PC "
          + "and never installed here are in your library too."
        : "Steam's local files only describe games this PC has installed or played. Everything else "
          + "you own is missing from your library until a key is set — on a large account that is "
          + "usually most of it.";

    /// <summary>Shown only when there is no key. Where the key goes, and nothing about how to get one.</summary>
    public bool ShowSteamKeyHint => !SteamWebApiConfigured;

    public string SteamKeyHintMessage =>
        "A key goes in the Steam__ApiKey environment variable, or in appsettings.local.json beside "
        + "the executable. It is read from there and sent to nobody but Steam.";

    // ══ Epic ════════════════════════════════════════════════════════════════

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(EpicIsSignedIn), nameof(EpicIsSigningIn), nameof(EpicIsLapsed),
        nameof(EpicCanSignIn), nameof(EpicStatusLabel), nameof(EpicStatusIsLive),
        nameof(EpicStatusNeedsAttention), nameof(EpicSignInButtonText),
        nameof(ShowEpicAccountLine), nameof(ShowEpicAnonymousLine))]
    public partial EpicConnection EpicState { get; set; } = EpicConnection.SignedOut;

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(EpicAccountLine), nameof(ShowEpicAccountLine), nameof(ShowEpicAnonymousLine))]
    public partial string? EpicDisplayName { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EpicCountText), nameof(ShowEpicCount))]
    public partial int EpicTitleCount { get; set; }

    public string EpicCountText => EpicTitleCount.ToString("N0");

    public bool ShowEpicCount => EpicTitleCount > 0;

    /// <summary>
    /// The last sign-in attempt's reason, when it failed.
    /// <see cref="StoreSignInProblem.None"/> whenever nothing has gone wrong,
    /// including on first open.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEpicProblem), nameof(ShowConsoleRoute))]
    public partial StoreSignInProblem EpicProblem { get; set; } = StoreSignInProblem.None;

    /// <summary>
    /// The sentence for <see cref="EpicProblem"/>, written by
    /// <c>EpicSignInService.Explain</c> and shown verbatim. Not rewritten here:
    /// each of those says what to DO, the remedies really are different from one
    /// another, and a second copy is one that drifts.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEpicProblem))]
    public partial string? EpicProblemMessage { get; set; }

    public bool ShowEpicProblem =>
        EpicProblem != StoreSignInProblem.None && !string.IsNullOrWhiteSpace(EpicProblemMessage);

    /// <summary>
    /// True when a sign-in succeeded but this host could not encrypt the session
    /// at rest. Said out loud because the consequence is invisible until the next
    /// launch, where it looks exactly like a bug: the user signed in, and Winnow
    /// asks again.
    /// </summary>
    [ObservableProperty]
    public partial bool EpicSessionNotPersisted { get; set; }

    public string EpicSessionNotPersistedMessage =>
        "This machine can't encrypt the session, so it's held for this run only. Winnow will ask you "
        + "to sign in again next time it starts — it will not store the credential unprotected.";

    public bool EpicIsSignedIn => EpicState == EpicConnection.SignedIn;

    public bool EpicIsSigningIn => EpicState == EpicConnection.SigningIn;

    public bool EpicIsLapsed => EpicState == EpicConnection.Lapsed;

    public bool EpicCanSignIn => EpicState is EpicConnection.SignedOut or EpicConnection.Lapsed;

    public string EpicStatusLabel => EpicState switch
    {
        EpicConnection.SignedIn => "SIGNED IN",
        EpicConnection.SigningIn => "SIGNING IN",
        EpicConnection.Lapsed => "SESSION EXPIRED",
        _ => "NOT SIGNED IN",
    };

    public bool EpicStatusIsLive => EpicState == EpicConnection.SignedIn;

    /// <summary>
    /// Amber, and only here on this screen: a session the user established and
    /// that stopped working is the one state on the panel that is neither
    /// working nor a deliberate choice. Never Flare — see the class remarks.
    /// </summary>
    public bool EpicStatusNeedsAttention => EpicState == EpicConnection.Lapsed;

    public string EpicSignInButtonText =>
        EpicState == EpicConnection.Lapsed ? "Sign in again" : "Sign in to Epic";

    /// <summary>
    /// The display name is the only account-identifying value this panel renders
    /// — never the account id, and never anything from the token itself.
    /// </summary>
    public bool ShowEpicAccountLine =>
        EpicState is EpicConnection.SignedIn or EpicConnection.Lapsed
        && !string.IsNullOrWhiteSpace(EpicDisplayName);

    public string EpicAccountLine => EpicDisplayName ?? string.Empty;

    /// <summary>
    /// Shown when a session exists but Epic supplied no display name. Better
    /// than a blank where a name should be, which reads as a rendering fault.
    /// </summary>
    public bool ShowEpicAnonymousLine =>
        EpicState == EpicConnection.SignedIn && string.IsNullOrWhiteSpace(EpicDisplayName);

    public string EpicAnonymousMessage => "Connected. Epic didn't supply a display name for this account.";

    public string EpicLocalMessage =>
        "Always on. The launcher's own catalog gives Winnow your owned Epic games and which of them "
        + "are installed, with no account and no network.";

    /// <summary>
    /// The gap, stated in terms of the buckets the user already knows. This is
    /// the sentence the whole panel exists to make possible: it is a fact about
    /// their library that the library cannot state about itself.
    /// </summary>
    public string EpicGapMessage =>
        "Epic writes no playtime and no last-played date to disk — anywhere. So Epic games sit "
        + "outside the playtime buckets entirely: nothing on this machine knows whether you've "
        + "never played one, bounced off it, or played it out.";

    public string EpicSignInAddsMessage =>
        "Signing in adds per-game playtime and the date you acquired each title, which is what puts "
        + "Epic games into a bucket. Epic publishes no last-played date on any endpoint, so how long "
        + "ago you played one still comes from Winnow watching it run, not from Epic.";

    /// <summary>
    /// Says that the consent step exists and roughly what it will say, without
    /// being a second gate. The real notice is shown by the prompt before
    /// anything navigates (<c>docs/spikes/embedded-auth.md</c> §8) and a second
    /// confirmation here would add no protection — it would teach the user to
    /// click through the one that matters.
    /// </summary>
    public string EpicConsentPromiseMessage =>
        "Before anything opens, Winnow shows you what it will be holding: a credential with full "
        + "access to your Epic account, kept encrypted on this machine. You can stop there.";

    /// <summary>
    /// The console peer, offered as a standing choice and not only after a
    /// failure. <c>epic-oauth.md</c> §1's amendment is explicit that the posture
    /// objection to an embedded browser survives the technical answer to it: the
    /// password is typed into a window Winnow opened, so the user's protection
    /// went from structural to promised. The peer route is how someone declines
    /// that without losing the feature, and a route nobody is told about is not
    /// a route.
    /// </summary>
    public string EpicConsoleAlternativeMessage =>
        "Rather not type your Epic password into a window Winnow opened? Sign in from a terminal "
        + "instead and Winnow never hosts the page at all:";

    public string EpicSigningInMessage =>
        "Epic's sign-in page is open in its own window. Finish there — including any code from your "
        + "phone — and this updates itself. Nothing has changed yet.";

    public string EpicSignOutMessage =>
        "Signing out deletes the stored credential. Your Epic games stay: they come from the local "
        + "files, which is where they came from before you signed in.";

    public string EpicLapsedMessage =>
        "The session expired while Winnow was closed. Your Epic games are still here from the local "
        + "files; playtime and acquisition dates stopped updating when it lapsed.";

    /// <summary>
    /// The console command is offered as a copyable line for the two failures it
    /// actually answers. Not for a rejected code (the remedy is another try) and
    /// not for an unreachable Epic (the console flow needs the same network).
    /// </summary>
    public bool ShowConsoleRoute =>
        EpicProblem is StoreSignInProblem.NoPromptAvailable or StoreSignInProblem.NoCodeCaptured;

    // ══ GOG ═════════════════════════════════════════════════════════════════

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GogCountText), nameof(ShowGogCount))]
    public partial int GogTitleCount { get; set; }

    public string GogCountText => GogTitleCount.ToString("N0");

    public bool ShowGogCount => GogTitleCount > 0;

    public string GogLocalMessage =>
        "Always on, and it is the whole story. Galaxy's own database gives Winnow your owned GOG "
        + "games, playtime, last-played dates, purchase dates and install paths.";

    /// <summary>
    /// <b>Stated, never drawn as a disabled button.</b> A greyed "Sign in"
    /// beside the other two would say Winnow is missing a feature here, and it is
    /// not: this is a measured finding, and the measurement is in
    /// <c>ROADMAP.md</c> §4 — the authenticated endpoint carries no playtime, no
    /// last-played, no title and no DLC flag, all four of which the local reader
    /// already has.
    /// </summary>
    public string GogNoSignInMessage =>
        "There is nothing to sign into. GOG's authenticated library endpoint carries no playtime, "
        + "no last-played date, no title and no DLC flag — every one of which Galaxy's local "
        + "database already gives Winnow. A sign-in here would cost you a password and buy nothing.";

    public string GogStatusLabel => "LOCAL FILES";

    // ══ Commands ════════════════════════════════════════════════════════════

    /// <summary>
    /// Reads the current state of both connections and the per-store counts.
    /// Called when the panel opens rather than on a timer: nothing here changes
    /// except as a result of something the user did.
    ///
    /// <para>It deliberately does <b>not</b> overwrite
    /// <see cref="EpicState"/> while a sign-in is running. The user can open and
    /// close this panel while Epic's window is up, and a refresh that reset the
    /// row to "not signed in" would look like the attempt had failed.</para>
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync(CancellationToken ct)
    {
        SteamWebApiConfigured = await _connections.IsSteamWebApiConfiguredAsync(ct);

        if (EpicState != EpicConnection.SigningIn)
        {
            ApplySession(await _connections.GetEpicSessionAsync(ct));
        }

        if (_counts?.TitlesByStore() is { } byStore)
        {
            SteamTitleCount = byStore.GetValueOrDefault("steam");
            EpicTitleCount = byStore.GetValueOrDefault("epic");
            GogTitleCount = byStore.GetValueOrDefault("gog");
        }
    }

    /// <summary>
    /// The §5.1 command: it raises an intent, and the seam does the work. The
    /// view model does not know that a browser is involved, that there is an
    /// OAuth code, or that a token gets encrypted — and it never sees any of the
    /// three.
    ///
    /// <para><b>Cancellable, and that is not decoration.</b> The call waits on a
    /// person finding a password manager and a phone; the generated
    /// <c>SignInToEpicCancelCommand</c> closes the browser window and the flow
    /// comes back <see cref="StoreSignInProblem.Cancelled"/>. Concurrent
    /// execution is off by default in <c>AsyncRelayCommand</c>, so a second
    /// click while one is running cannot open a second window.</para>
    /// </summary>
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task SignInToEpicAsync(CancellationToken ct)
    {
        // Cleared before the attempt, not after it: leaving the previous
        // failure on screen while a new sign-in runs makes the panel look like
        // it is reporting on the attempt in progress.
        EpicProblem = StoreSignInProblem.None;
        EpicProblemMessage = null;
        EpicSessionNotPersisted = false;
        EpicState = EpicConnection.SigningIn;

        StoreSignInOutcome outcome;
        try
        {
            outcome = await _connections.SignInToEpicAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // A belt. StoreConnections already turns cancellation into a named
            // outcome, so this only fires if one escapes some other way — but a
            // command handler is the wrong place to discover that, and an
            // unhandled exception on a UI thread takes the window down over an
            // optional feature. Cancelling is deliberate, so it is never worded
            // as a fault and is never retried automatically.
            ApplySession(await SafeSessionAsync());
            EpicProblem = StoreSignInProblem.Cancelled;
            EpicProblemMessage = StoreSignInMessages.Cancelled;
            return;
        }

        if (outcome.Succeeded)
        {
            EpicDisplayName = outcome.DisplayName;
            EpicSessionNotPersisted = !outcome.Persisted;
            EpicState = EpicConnection.SignedIn;
            return;
        }

        // A failed attempt leaves whatever session already existed alone —
        // every one of these outcomes is documented as changing nothing — so the
        // row is redrawn from the stored state rather than assumed to be signed
        // out. A lapsed session that failed to renew must still read "expired".
        ApplySession(await SafeSessionAsync());
        EpicProblem = outcome.Problem;
        EpicProblemMessage = outcome.Message;
    }

    /// <summary>
    /// Ends the session. Not a destructive act and deliberately not dressed as
    /// one: <c>Danger</c> is reserved for the one irreversible thing in the app
    /// (§12.3), and this costs two fields on some Epic rows until the user signs
    /// in again.
    /// </summary>
    [RelayCommand]
    private async Task SignOutOfEpicAsync(CancellationToken ct)
    {
        await _connections.SignOutOfEpicAsync(ct);

        EpicDisplayName = null;
        EpicSessionNotPersisted = false;
        EpicProblem = StoreSignInProblem.None;
        EpicProblemMessage = null;
        EpicState = EpicConnection.SignedOut;
    }

    /// <summary>
    /// A status read on a failure path must not be able to turn one failure into
    /// two. If the store cannot be read here the panel keeps the row it already
    /// had rather than losing the reason it was drawing.
    /// </summary>
    private async Task<StoreSession?> SafeSessionAsync()
    {
        try
        {
            return await _connections.GetEpicSessionAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void ApplySession(StoreSession? session)
    {
        EpicDisplayName = session?.DisplayName;
        EpicState = session switch
        {
            { IsLive: true } => EpicConnection.SignedIn,
            not null => EpicConnection.Lapsed,
            null => EpicConnection.SignedOut,
        };
    }
}
