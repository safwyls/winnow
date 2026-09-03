using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.App.Services;
using Winnow.Core.Auth;
using Winnow.Enrich.SteamWeb.Credentials;

namespace Winnow.App.ViewModels;

/// <summary>Epic connection state (mutually exclusive).</summary>
public enum EpicConnection
{
    /// <summary>No session.</summary>
    SignedOut = 0,

    /// <summary>Sign-in in progress.</summary>
    SigningIn,

    /// <summary>Active session.</summary>
    SignedIn,

    /// <summary>A stored session whose refresh token expired while the app was closed.</summary>
    Lapsed,
}

/// <summary>
/// The Platforms settings screen: shows which sources feed the library, what each
/// can see, and sign-in state. Talks to <see cref="IStoreConnections"/> and
/// <see cref="IStoreTitleCounts"/> (§5.1). No Flare on this screen (§2).
/// </summary>
public partial class StoresViewModel : ObservableObject
{
    public const string ConsoleSignInCommand = "dotnet run --project src/Winnow.App -- --epic-login";

    /// <summary>Bindable wrapper for the const (const fields are not bindable).</summary>
    public string ConsoleSignInCommandText => ConsoleSignInCommand;

    private readonly IStoreConnections _connections;
    private readonly IStoreTitleCounts? _counts;
    private readonly IAccountVisibility? _accountVisibility;

    /// <summary>
    /// The Steam sign-in, and the ONLY seam this panel resolves for it. It carries
    /// no token into the view model by construction: <see cref="SteamSignInReport"/>
    /// has no field for one, and <c>ISteamSessionProvider</c> — which does — stays
    /// out of this constructor deliberately (TASK-55 S3).
    /// </summary>
    private readonly SteamSignInService? _steamSignIn;

    /// <summary>
    /// The one place a URI leaves the application, used for the single outbound
    /// link on this screen: Steam's own key registration page.
    /// </summary>
    private readonly IUriDispatcher? _uris;

    /// <summary>Guards against writing the preference back while it is being read.</summary>
    private bool _loadingAccountScope;

    public StoresViewModel(
        IStoreConnections connections,
        IStoreTitleCounts? counts = null,
        IAccountVisibility? accountVisibility = null,
        SteamSignInService? steamSignIn = null,
        IUriDispatcher? uris = null,
        SteamAccountImportViewModel? accountImport = null)
    {
        _connections = connections;
        _counts = counts;
        _accountVisibility = accountVisibility;
        _steamSignIn = steamSignIn;
        _uris = uris;
        AccountImport = accountImport;
    }

    /// <summary>
    /// The purchase/licence import, folded into the Steam card (TASK-59). Null
    /// in a host that composed the panel alone, which hides the section rather
    /// than drawing a dead one.
    /// </summary>
    public SteamAccountImportViewModel? AccountImport { get; }

    /// <summary>Whether the PURCHASE HISTORY section is drawn. False when no
    /// import was composed, so the section is absent rather than dead.</summary>
    public bool ShowPurchaseImport => AccountImport is not null;

    /// <summary>
    /// Re-runs the library query and the feed after the account filter changes.
    /// Set by <see cref="MainWindowViewModel"/>, which is the only type holding
    /// both this panel and the screens the change is visible on.
    ///
    /// <para>Null in a host that composed the panel alone — the preference is
    /// still written, and the next load reads it.</para>
    /// </summary>
    public Func<Task>? ReloadLibrary { get; set; }

    // ══ Rail and header ═════════════════════════════════════════════════════

    public string Title => "Platforms";

    /// <summary>
    /// The segment label for this screen. Bound rather than written into the
    /// XAML so the rename is a fact a test can read, which is how it drifted
    /// back to STORES the last time.
    /// </summary>
    public string SegmentLabel => "PLATFORMS";

    public string SegmentTooltip => SteamConnectionCopy.SegmentTooltip;

    public string IntroMessage =>
        "Where your library comes from. All three read local files; Steam and Epic can also connect for more.";

    // ══ Steam ═══════════════════════════════════════════════════════════════
    //
    // TASK-55 S5. Two peer connection methods, and the screen states plainly what
    // each one gives and what it gives up. The sign-in is presented first as the
    // fuller path; the key is a genuine alternative and is never drawn as a
    // fallback, a disabled control or a smaller card.
    //
    // Everything below reads from ONE credential fact — SteamCredentials, the
    // App-layer projection of the credential inventory — and ONE health value.
    // There is deliberately no second opinion anywhere in this file about whether
    // Steam is connected.

    /// <summary>
    /// What Steam credentials exist right now. One read answers both methods, so
    /// the two cards cannot disagree about what is configured.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(SteamWebApiConfigured), nameof(SteamHasApiKey), nameof(SteamHasSession),
        nameof(SteamApiKeyIsAppManaged), nameof(SteamStatusLabel), nameof(SteamStatusIsLive),
        nameof(SteamStatusNeedsAttention), nameof(SteamConnectionMessage),
        nameof(SteamApiKeyStatusMessage), nameof(SteamApiKeyStateText),
        nameof(ShowSteamBothCredentials), nameof(SteamConnectionSummaryMessage),
        nameof(SteamSignedInAccountText), nameof(ShowSteamSignedInAccount),
        nameof(SteamSessionExpiresText), nameof(ShowSteamSessionExpires),
        nameof(SteamSignInButtonText), nameof(ShowSteamSignedIn), nameof(ShowSteamSignInAction),
        // The disabled toggle's explanation names which credentials are in force,
        // so it has to be redrawn when they change — otherwise a user who has
        // just pasted a key is still being told to get one.
        nameof(AccountScopeBlockedMessage))]
    [NotifyCanExecuteChangedFor(nameof(ClearSteamApiKeyCommand))]
    public partial SteamConnection SteamCredentials { get; set; } = SteamConnection.None;

    /// <summary>
    /// The stored sign-in session's state, read from the session provider through
    /// <see cref="SteamSignInService"/> and rendered rather than re-derived.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(SteamStatusLabel), nameof(SteamStatusIsLive), nameof(SteamStatusNeedsAttention),
        nameof(SteamSessionHealthMessage), nameof(ShowSteamSessionAttention),
        nameof(SteamSignInStateText), nameof(ShowSteamSessionCalmHealth),
        nameof(SteamSignInButtonText), nameof(ShowSteamSignedIn), nameof(ShowSteamSignInAction))]
    public partial SteamSessionHealth SteamSessionState { get; set; } = SteamSessionHealth.NotSignedIn;

    /// <summary>
    /// Whether Steam's Web API can be reached on the user's behalf at all, by
    /// either credential. Kept under its original name because it is the same
    /// question; what changed is that a keyless signed-in user now answers true.
    /// </summary>
    public bool SteamWebApiConfigured => SteamCredentials.HasUsableCredential;

    public bool SteamHasApiKey => SteamCredentials.HasApiKey;

    public bool SteamHasSession => SteamCredentials.HasSession;

    /// <summary>Whether the key in force is the one this screen's own field owns.</summary>
    public bool SteamApiKeyIsAppManaged => SteamCredentials.ApiKeyIsAppManaged;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SteamCountText), nameof(ShowSteamCount))]
    public partial int SteamTitleCount { get; set; }

    public string SteamCountText => SteamTitleCount.ToString("N0");

    public bool ShowSteamCount => SteamTitleCount > 0;

    public string SteamLocalLabel => SteamConnectionCopy.LocalFilesLabel;

    public string SteamLocalMessage => SteamConnectionCopy.LocalFiles;

    /// <summary>Terse state phrase for local files, beside its heading. The
    /// full description is in the disclosure.</summary>
    public string SteamLocalStateText => SteamConnectionCopy.StateLocalAlwaysOn;

    public string SteamConnectionSectionLabel => SteamConnectionCopy.SectionLabel;

    public string SteamConnectionIntroMessage => SteamConnectionCopy.SectionIntro;

    /// <summary>
    /// The one line at the top of the WEB API section. The full three-sentence
    /// introduction (<see cref="SteamConnectionCopy.SectionIntro"/>) is in the
    /// methods disclosure beside it.
    /// </summary>
    public string SteamConnectionSummaryMessage => SteamHasApiKey || SteamHasSession
        ? SteamConnectionCopy.SectionSummaryConnected
        : SteamConnectionCopy.SectionSummaryNothing;

    /// <summary>
    /// What a connection is worth, in the two states it has an answer for: what
    /// is missing while nothing is connected, and what a connection added.
    /// </summary>
    public string SteamConnectionMessage => SteamCredentials.HasAnyCredential
        ? SteamConnectionCopy.ConnectedAdds
        : SteamConnectionCopy.NothingConnectedCost;

    // ── The combined status label ────────────────────────────────────────────
    //
    // Six states, and the sign-in's problems win over the key's presence: a user
    // whose sign-in has expired needs to be told that before they are told a key
    // is set. The sentence under the pill is where the key's continued cover gets
    // stated, and ShowSteamBothCredentials is what states it.

    public string SteamStatusLabel => SteamSessionState switch
    {
        SteamSessionHealth.RenewalDue or SteamSessionHealth.RenewalFailing
            => SteamConnectionCopy.StatusSignInNeedsRenewing,
        SteamSessionHealth.Expired => SteamConnectionCopy.StatusSignInExpired,
        SteamSessionHealth.Live or SteamSessionHealth.NotPersisted => SteamHasApiKey
            ? SteamConnectionCopy.StatusSignedInAndKeySet
            : SteamConnectionCopy.StatusSignedIn,
        _ => SteamHasApiKey
            ? SteamConnectionCopy.StatusKeySet
            : SteamConnectionCopy.StatusNoConnection,
    };

    /// <summary>Volt: something here works right now.</summary>
    public bool SteamStatusIsLive
        => SteamSessionState is SteamSessionHealth.Live or SteamSessionHealth.NotPersisted
            || (SteamSessionState == SteamSessionHealth.NotSignedIn && SteamHasApiKey);

    /// <summary>
    /// Amber, never Flare. Flare marks unread updates and nothing else, so a
    /// broken credential borrows the same attention treatment the Epic card uses
    /// for a lapsed session.
    /// </summary>
    public bool SteamStatusNeedsAttention
        => SteamSessionState is SteamSessionHealth.RenewalFailing or SteamSessionHealth.Expired;

    // ── Session health, all six states ───────────────────────────────────────

    public string SteamSessionHealthMessage => SteamSessionState switch
    {
        SteamSessionHealth.Live => SteamConnectionCopy.HealthLive,
        SteamSessionHealth.RenewalDue => SteamConnectionCopy.HealthRenewalDue,
        SteamSessionHealth.RenewalFailing => SteamConnectionCopy.HealthRenewalFailing,
        SteamSessionHealth.Expired => SteamConnectionCopy.HealthExpired,
        SteamSessionHealth.NotPersisted => SteamConnectionCopy.HealthNotPersisted,
        _ => SteamConnectionCopy.HealthNotSignedIn,
    };

    /// <summary>
    /// Whether the health line wears the Amber edge. A session that cannot be
    /// renewed and one that has died are both states the user has to act on; a
    /// session that was never written is one they need to know about before the
    /// next launch makes it look like a bug.
    /// </summary>
    public bool ShowSteamSessionAttention => SteamSessionState
        is SteamSessionHealth.RenewalFailing
        or SteamSessionHealth.Expired
        or SteamSessionHealth.NotPersisted;

    /// <summary>
    /// Whether the health sentence belongs in the sign-in disclosure rather
    /// than at the top level. A health sentence that is not an attention state
    /// is detail; the three that ARE attention states stay at the top level
    /// under <see cref="ShowSteamSessionAttention"/> and are never drawn only
    /// inside a collapsed panel (ROADMAP §4.7 condition 8).
    /// </summary>
    public bool ShowSteamSessionCalmHealth => !ShowSteamSessionAttention;

    /// <summary>
    /// The sign-in's state as a terse phrase, beside its heading. One value per
    /// <see cref="SteamSessionHealth"/>, so the terse line never collapses two
    /// states the full sentences distinguish.
    /// </summary>
    public string SteamSignInStateText => SteamSessionState switch
    {
        SteamSessionHealth.Live => SteamConnectionCopy.StateSignInLive,
        SteamSessionHealth.RenewalDue => SteamConnectionCopy.StateSignInRenewalDue,
        SteamSessionHealth.RenewalFailing => SteamConnectionCopy.StateSignInRenewalFailing,
        SteamSessionHealth.Expired => SteamConnectionCopy.StateSignInExpired,
        SteamSessionHealth.NotPersisted => SteamConnectionCopy.StateSignInNotPersisted,
        _ => SteamConnectionCopy.StateSignInNone,
    };

    /// <summary>
    /// Both credentials are held, so the user is told which one does what rather
    /// than being left to guess. The decision is the user's own (decision note 2):
    /// scheduled work takes the key because keys do not expire.
    /// </summary>
    public bool ShowSteamBothCredentials => SteamHasApiKey && SteamHasSession;

    public string SteamBothCredentialsMessage => SteamConnectionCopy.BothCredentials;

    // ── Method A: sign in ────────────────────────────────────────────────────

    public string SteamSignInHeading => SteamConnectionCopy.SignInHeading;

    public string SteamSignInGivesMessage => SteamConnectionCopy.SignInGives;

    public string SteamSignInCostsMessage => SteamConnectionCopy.SignInCosts;

    public string SteamSignInButtonText => SteamHasSession
        ? SteamConnectionCopy.SignInAgainButton
        : SteamConnectionCopy.SignInButton;

    public string SteamSignInCancelButtonText => SteamConnectionCopy.SignInCancelButton;

    public string SteamSignInBusyMessage => SteamConnectionCopy.SignInBusy;

    public string SteamSignInUnavailableMessage => SteamConnectionCopy.SignInUnavailable;

    public string SteamSignedInAsLabel => SteamConnectionCopy.SignedInAsLabel;

    public string SteamSignOutButtonText => SteamConnectionCopy.SignOutButton;

    public string SteamSignOutMessage => SteamConnectionCopy.SignOutExplanation;

    /// <summary>
    /// Whether an embedded sign-in can run here. Advisory: the button stays live
    /// either way, because a route drawn as a dead control is a route presented as
    /// second-class, and the runtime can be installed while this screen is open.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSteamSignInUnavailable))]
    public partial bool SteamSignInAvailable { get; set; } = true;

    public bool ShowSteamSignInUnavailable => !SteamSignInAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSteamSignInBusy), nameof(ShowSteamSignInAction))]
    public partial bool IsSigningInToSteam { get; set; }

    public bool ShowSteamSignInBusy => IsSigningInToSteam;

    /// <summary>The signed-in block: the account, the confirmation line and the way out.</summary>
    public bool ShowSteamSignedIn => SteamHasSession;

    /// <summary>
    /// Whether the permission control and the sign-in button are offered.
    ///
    /// <para>Not simply "there is no session". A session that has expired or
    /// whose renewal is failing is exactly the state the decision note requires a
    /// one-click way out of, so the action stays offered there and the button
    /// renames itself; a session that is working has nothing to press, so it is
    /// withdrawn rather than sitting there inviting a pointless second window.</para>
    /// </summary>
    public bool ShowSteamSignInAction
        => !IsSigningInToSteam
            && SteamSessionState is not (SteamSessionHealth.Live or SteamSessionHealth.NotPersisted);

    /// <summary>
    /// The signed-in account's SteamID64. The only identity the token carries —
    /// Steam supplies no display name here — and it renders in the data face with
    /// tabular figures, because it is a number (§8).
    /// </summary>
    public string SteamSignedInAccountText => SteamCredentials.SessionAccount ?? string.Empty;

    public bool ShowSteamSignedInAccount => !string.IsNullOrWhiteSpace(SteamCredentials.SessionAccount);

    /// <summary>When the access token dies, in the user's own time zone.</summary>
    public string SteamSessionExpiresText => SteamCredentials.SessionExpiresAt is { } expiry
        ? expiry.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture)
        : string.Empty;

    public bool ShowSteamSessionExpires => SteamCredentials.SessionExpiresAt is not null;

    /// <summary>
    /// <b>Acceptance criterion 2.</b> A separate, explicit permission, unticked on
    /// every install and after every sign-out, and the ONE value that ever reaches
    /// <see cref="SteamSignInRequest.CapturePurchaseHistory"/>. Declining is a
    /// complete answer: the sign-in still delivers account identity and playtime
    /// backfill, and the account pages are then never navigated to at all.
    /// </summary>
    [ObservableProperty]
    public partial bool CapturePurchaseHistory { get; set; }

    public string CapturePurchaseHistoryLabel => SteamConnectionCopy.PurchaseHistoryPermissionLabel;

    public string CapturePurchaseHistoryMessage
        => SteamConnectionCopy.PurchaseHistoryPermissionExplanation;

    /// <summary>What the last attempt did, stated as a fact. Never wears the Amber edge.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSteamSignInNotice))]
    public partial string? SteamSignInNoticeMessage { get; set; }

    public bool ShowSteamSignInNotice => !string.IsNullOrWhiteSpace(SteamSignInNoticeMessage);

    /// <summary>Something went wrong and the sentence says what. Amber, never Flare.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSteamSignInProblem))]
    public partial string? SteamSignInProblemMessage { get; set; }

    public bool ShowSteamSignInProblem => !string.IsNullOrWhiteSpace(SteamSignInProblemMessage);

    /// <summary>
    /// Whether the last sign-in recorded which account is the user's, taken
    /// straight off <see cref="SteamSignInReport.AccountConfirmed"/> and rendered
    /// rather than re-derived. Null until a sign-in has been attempted.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(ShowSteamSignInAccountConfirmed), nameof(SteamSignInAccountConfirmedMessage))]
    public partial bool? SteamSignInConfirmedAccount { get; set; }

    public bool ShowSteamSignInAccountConfirmed => SteamSignInConfirmedAccount is not null;

    public string SteamSignInAccountConfirmedMessage => SteamSignInConfirmedAccount == true
        ? SteamConnectionCopy.AccountConfirmed
        : SteamConnectionCopy.AccountNotConfirmed;

    // ── Method B: the Web API key ────────────────────────────────────────────

    public string SteamApiKeyHeading => SteamConnectionCopy.ApiKeyHeading;

    public string SteamApiKeyGivesMessage => SteamConnectionCopy.ApiKeyGives;

    public string SteamApiKeyCostsMessage => SteamConnectionCopy.ApiKeyCosts;

    public string SteamApiKeyFieldLabel => SteamConnectionCopy.ApiKeyFieldLabel;

    public string SteamApiKeyWatermark => SteamConnectionCopy.ApiKeyWatermark;

    public string SteamApiKeySaveButtonText => SteamConnectionCopy.ApiKeySaveButton;

    public string SteamApiKeyClearButtonText => SteamConnectionCopy.ApiKeyClearButton;

    public string SteamApiKeyGetButtonText => SteamConnectionCopy.ApiKeyGetButton;

    /// <summary>
    /// Which of the three key states holds: none, one this screen owns, or one
    /// supplied through the environment that this screen may supersede but cannot
    /// delete.
    /// </summary>
    public string SteamApiKeyStatusMessage => !SteamHasApiKey
        ? SteamConnectionCopy.ApiKeyNotSet
        : SteamApiKeyIsAppManaged
            ? SteamConnectionCopy.ApiKeySet
            : SteamConnectionCopy.ApiKeyFromEnvironment;

    /// <summary>
    /// The API key's state as a terse phrase, beside its heading.
    ///
    /// <para>The environment branch is the one terse line that carries a
    /// consequence rather than only a state: the Clear button beside it is
    /// disabled, and a disabled control whose reason sits inside a collapsed
    /// panel reads as a bug. The full sentence is in the disclosure.</para>
    /// </summary>
    public string SteamApiKeyStateText => !SteamHasApiKey
        ? SteamConnectionCopy.StateApiKeyNotSet
        : SteamApiKeyIsAppManaged
            ? SteamConnectionCopy.StateApiKeySet
            : SteamConnectionCopy.StateApiKeyExternal;

    /// <summary>
    /// The field's contents. Emptied the instant the key is saved: a bound
    /// property is the one place a secret would otherwise sit in memory with a
    /// public getter for the rest of the session.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveSteamApiKeyCommand))]
    public partial string SteamApiKeyInput { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSteamApiKeyNotice))]
    public partial string? SteamApiKeyNoticeMessage { get; set; }

    public bool ShowSteamApiKeyNotice => !string.IsNullOrWhiteSpace(SteamApiKeyNoticeMessage);

    // ══ The four disclosures ════════════════════════════════════════════════
    //
    // TASK-61. The top level of the Steam card is the method, its state and its
    // button; everything that explains a method sits under that method's own
    // toggle. The idiom is the filter panel's "Show all 214" — a Button.linky in
    // Azure over content bound to IsVisible — and not an Expander, because that
    // is the progressive disclosure this app already has and because nothing
    // here animates, so a reduced-motion setting has nothing to suppress.
    //
    // Every one of them starts CLOSED and holds only detail. What a user has to
    // act on is never behind one: a failing renewal, an expired session, a
    // sign-in this host cannot encrypt, a failed attempt and a missing WebView2
    // runtime are all drawn at the top level whatever these are set to (§4.7
    // amendment, condition 8).

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SteamLocalDetailsToggleText))]
    public partial bool SteamLocalDetailsOpen { get; set; }

    [RelayCommand]
    private void ToggleSteamLocalDetails() => SteamLocalDetailsOpen = !SteamLocalDetailsOpen;

    public string SteamLocalDetailsToggleText => SteamLocalDetailsOpen
        ? SteamConnectionCopy.DisclosureHide
        : SteamConnectionCopy.DisclosureLocalFiles;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SteamMethodsDetailsToggleText))]
    public partial bool SteamMethodsDetailsOpen { get; set; }

    [RelayCommand]
    private void ToggleSteamMethodsDetails() => SteamMethodsDetailsOpen = !SteamMethodsDetailsOpen;

    public string SteamMethodsDetailsToggleText => SteamMethodsDetailsOpen
        ? SteamConnectionCopy.DisclosureHide
        : SteamConnectionCopy.DisclosureMethods;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SteamSignInDetailsToggleText))]
    public partial bool SteamSignInDetailsOpen { get; set; }

    [RelayCommand]
    private void ToggleSteamSignInDetails() => SteamSignInDetailsOpen = !SteamSignInDetailsOpen;

    public string SteamSignInDetailsToggleText => SteamSignInDetailsOpen
        ? SteamConnectionCopy.DisclosureHide
        : SteamConnectionCopy.DisclosureSignIn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SteamApiKeyDetailsToggleText))]
    public partial bool SteamApiKeyDetailsOpen { get; set; }

    [RelayCommand]
    private void ToggleSteamApiKeyDetails() => SteamApiKeyDetailsOpen = !SteamApiKeyDetailsOpen;

    public string SteamApiKeyDetailsToggleText => SteamApiKeyDetailsOpen
        ? SteamConnectionCopy.DisclosureHide
        : SteamConnectionCopy.DisclosureApiKey;

    // ══ Steam purchase and licence history ══════════════════════════════════
    //
    // TASK-59. The import screen's two routes, folded into the card that owns
    // the Steam connection: one place to connect Steam, one place to import
    // purchase data. The saved-file route is drawn and enabled whatever the
    // credential state is, so a user who declines the browser sign-in is never
    // pushed through it to reach the files they already saved.

    public string SteamPurchaseSectionLabel => SteamConnectionCopy.PurchaseSectionLabel;

    // ══ Steam account visibility ════════════════════════════════════════════
    //
    // Winnow reads every Steam account signed in on the PC and shows them as one
    // library. This is where the user narrows that to their own account.
    //
    // It lives beside the Web API key state and not in the Display popover
    // because the key is what identifies which account is theirs: the toggle is
    // disabled until an API call has answered FOR an account, and the sentence
    // explaining why is about the key sitting immediately above it.

    /// <summary>
    /// Whether Winnow has established which Steam account the key belongs to.
    /// Gate on the toggle: a filter that cannot name the account it keeps would
    /// hide games at random.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(CanChooseAccountScope), nameof(ShowAccountScopeBlocked),
        nameof(AccountScopeBlockedMessage))]
    public partial bool SteamAccountConfirmed { get; set; }

    /// <summary>The preference itself. False — every account — on every install until the user acts.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAccountScopeCaveat))]
    public partial bool ShowOwnAccountOnly { get; set; }

    /// <summary>How many entries the filter removes. Stated beside the toggle.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(AccountScopeHiddenCountText), nameof(AccountScopeHiddenUnitLabel),
        nameof(ShowAccountScopeCount))]
    public partial int AccountScopeHiddenCount { get; set; }

    /// <summary>In-flight save; exposed for tests. The UI never awaits it.</summary>
    public Task PendingAccountScopeSave { get; private set; } = Task.CompletedTask;

    public string AccountScopeSectionLabel => "STEAM ACCOUNTS";

    public string AccountScopeMessage =>
        "Winnow reads every Steam account signed in on this PC and shows all their games as one "
        + "library. You can filter to show only games from your own account.";

    /// <summary>
    /// The toggle's own face. Carries no number: design-system.md renders every
    /// figure in IBM Plex Mono <c>tnum</c>, and a control's content face is the
    /// UI face — so the count lives beside the switch in the data face instead,
    /// the same split this card already uses for its per-store title counts.
    /// The switch position carries the state, so one string serves both.
    /// </summary>
    public string AccountScopeToggleLabel => "Show only your account";

    /// <summary>The figure, in the data face. Same shape as <see cref="SteamCountText"/>.</summary>
    public string AccountScopeHiddenCountText => AccountScopeHiddenCount.ToString("N0");

    /// <summary>
    /// The words after the figure. Deliberately state-neutral — it names where
    /// the games come from rather than whether they are hidden right now, so it
    /// reads correctly both before the toggle is used ("this is what it would
    /// remove") and after ("this is what it removed"), and the switch position
    /// is left to say which.
    /// </summary>
    public string AccountScopeHiddenUnitLabel => AccountScopeHiddenCount == 1
        ? "game from other accounts"
        : "games from other accounts";

    /// <summary>
    /// Hidden at zero rather than rendering "0". With nothing from another
    /// account the toggle has no effect, and an absent line says that better
    /// than a figure the user has no reason to act on.
    /// </summary>
    public bool ShowAccountScopeCount => AccountScopeHiddenCount > 0;

    public bool CanChooseAccountScope => SteamAccountConfirmed;

    public bool ShowAccountScopeBlocked => !SteamAccountConfirmed;

    /// <summary>
    /// Why the toggle is disabled, in the user's terms. THREE genuinely different
    /// states behind one disabled control, and they need different remedies:
    /// "wait, this fixes itself" for a key-only user, "either method fixes this"
    /// for a user who has connected nothing, and "sign in again" for the state
    /// that should not occur.
    ///
    /// <para><b>A signed-in user never reads any of them.</b> The sign-in records
    /// the account from the token's own subject claim before the window has
    /// finished closing (acceptance criterion 4), so the toggle is already live
    /// and <see cref="ShowAccountScopeBlocked"/> is false. The signed-in branch
    /// exists for the one case where that write did not land, and it says so
    /// rather than repeating advice about a key the user did not choose.</para>
    /// </summary>
    public string AccountScopeBlockedMessage => SteamHasSession
        ? SteamConnectionCopy.AccountScopeBlockedSignedIn
        : SteamHasApiKey
            ? SteamConnectionCopy.AccountScopeBlockedKeyOnly
            : SteamConnectionCopy.AccountScopeBlockedNothingConnected;

    public bool ShowAccountScopeCaveat => ShowOwnAccountOnly;

    /// <summary>
    /// Shown only while the filter is on, and it says the two things a user
    /// would otherwise read as bugs: that the filter is deliberately incomplete
    /// rather than leaky, and that the numbers on the tiles have changed meaning.
    /// </summary>
    public string AccountScopeCaveatMessage =>
        "Games Winnow cannot attribute to a specific account stay visible. For games seen on more "
        + "than one account, the playtime and last-played date shown become your own account's "
        + "figures, and categories like Never played and Bounced are derived from those.";

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEpicProblem), nameof(ShowConsoleRoute))]
    public partial StoreSignInProblem EpicProblem { get; set; } = StoreSignInProblem.None;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEpicProblem))]
    public partial string? EpicProblemMessage { get; set; }

    public bool ShowEpicProblem =>
        EpicProblem != StoreSignInProblem.None && !string.IsNullOrWhiteSpace(EpicProblemMessage);

    [ObservableProperty]
    public partial bool EpicSessionNotPersisted { get; set; }

    public string EpicSessionNotPersistedMessage =>
        "This machine can't encrypt the session, so you'll need to sign in again next launch.";

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

    public bool EpicStatusNeedsAttention => EpicState == EpicConnection.Lapsed;

    public string EpicSignInButtonText =>
        EpicState == EpicConnection.Lapsed ? "Sign in again" : "Sign in to Epic";

    public bool ShowEpicAccountLine =>
        EpicState is EpicConnection.SignedIn or EpicConnection.Lapsed
        && !string.IsNullOrWhiteSpace(EpicDisplayName);

    public string EpicAccountLine => EpicDisplayName ?? string.Empty;

    public bool ShowEpicAnonymousLine =>
        EpicState == EpicConnection.SignedIn && string.IsNullOrWhiteSpace(EpicDisplayName);

    public string EpicAnonymousMessage => "Connected. Epic didn't supply a display name for this account.";

    public string EpicLocalMessage =>
        "Always on. Reads owned Epic games and install state from the launcher's local files.";

    public string EpicGapMessage =>
        "Epic writes no playtime or last-played date to disk, so Epic games won't appear in "
        + "playtime-based categories unless you sign in.";

    public string EpicSignInAddsMessage =>
        "Signing in adds playtime and acquisition dates. Last-played dates come from Winnow "
        + "watching your sessions, not from Epic.";

    public string EpicConsentPromiseMessage =>
        "You'll see what Winnow is requesting before anything connects. The credential is stored encrypted on this machine.";

    public string EpicConsoleAlternativeMessage =>
        "Prefer to sign in from a terminal instead?";

    public string EpicSigningInMessage =>
        "Epic's sign-in page is open. Complete it there and this page will update.";

    public string EpicSignOutMessage =>
        "Signing out deletes the stored credential. Your Epic games stay — they come from local files.";

    public string EpicLapsedMessage =>
        "Session expired. Your Epic games are still here from local files; sign in again to resume playtime tracking.";

    public bool ShowConsoleRoute =>
        EpicProblem is StoreSignInProblem.NoPromptAvailable or StoreSignInProblem.NoCodeCaptured;

    // ══ GOG ═════════════════════════════════════════════════════════════════

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GogCountText), nameof(ShowGogCount))]
    public partial int GogTitleCount { get; set; }

    public string GogCountText => GogTitleCount.ToString("N0");

    public bool ShowGogCount => GogTitleCount > 0;

    public string GogLocalMessage =>
        "Always on. Reads owned GOG games, playtime, last-played dates and install state from Galaxy's local database.";

    public string GogNoSignInMessage =>
        "Not needed. Galaxy's local database already provides everything GOG's online API does.";

    public string GogStatusLabel => "LOCAL FILES";

    // ══ Commands ════════════════════════════════════════════════════════════

    /// <summary>Reads connection state and title counts.</summary>
    [RelayCommand]
    private async Task RefreshAsync(CancellationToken ct)
    {
        await RefreshSteamAsync(ct);

        // The folded purchase section needs to know whether the sign-in route
        // can run here before either button is pressed. This opens no window
        // and does no IO; it is the same availability check the standalone
        // screen ran on arrival.
        if (AccountImport is { } import)
        {
            await import.RefreshCommand.ExecuteAsync(null);
        }

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
    /// Re-reads everything the Steam card draws: which credentials are held,
    /// what state the session is in, whether an embedded sign-in can run here,
    /// and the account-scope answer that depends on all three.
    ///
    /// <para>Called after every command that could have changed any of them, so
    /// the card is never showing the state that held before the button was
    /// pressed.</para>
    /// </summary>
    private async Task RefreshSteamAsync(CancellationToken ct)
    {
        SteamCredentials = await _connections.GetSteamConnectionAsync(ct);

        if (_steamSignIn is null)
        {
            // A host composed without the sign-in still shows the key method in
            // full. The sign-in card says it cannot run here rather than
            // vanishing, because a method that disappears is a method the user
            // cannot find out about.
            SteamSessionState = SteamSessionHealth.NotSignedIn;
            SteamSignInAvailable = false;
            await RefreshAccountScopeAsync(ct);
            return;
        }

        SteamSessionState = await _steamSignIn.GetHealthAsync(ct);
        SteamSignInAvailable = await _steamSignIn.IsAvailableAsync(ct);

        await RefreshAccountScopeAsync(ct);
    }

    /// <summary>
    /// Reads the account-visibility state, under the loading guard so seeding
    /// the toggle from storage does not look like the user flipping it.
    /// </summary>
    private async Task RefreshAccountScopeAsync(CancellationToken ct)
    {
        if (_accountVisibility is null)
        {
            return;
        }

        var state = await _accountVisibility.GetAsync(ct);

        _loadingAccountScope = true;
        try
        {
            SteamAccountConfirmed = state.AccountConfirmed;
            ShowOwnAccountOnly = state.OwnAccountOnly;
            AccountScopeHiddenCount = state.HiddenCount;
        }
        finally
        {
            _loadingAccountScope = false;
        }
    }

    /// <summary>
    /// Persists the preference, then redraws everything it changes.
    ///
    /// <para>The reload is not optional politeness. The filter is applied inside
    /// the derived-bucket query, so the grid, the rail counts and the feed all
    /// still hold rows from the other mode until they ask again — and a settings
    /// toggle whose effect appears on the next launch reads as one that did not
    /// work.</para>
    ///
    /// <para>The hidden count is re-read afterwards rather than assumed: it is
    /// the answer to "what would the OTHER mode show", which is a different
    /// question from the one just answered, and on a library where the sync has
    /// been running it can have moved.</para>
    /// </summary>
    partial void OnShowOwnAccountOnlyChanged(bool value)
    {
        if (_loadingAccountScope || _accountVisibility is null)
        {
            return;
        }

        PendingAccountScopeSave = ApplyAsync();

        async Task ApplyAsync()
        {
            await _accountVisibility.SetOwnAccountOnlyAsync(value);

            if (ReloadLibrary is { } reload)
            {
                await reload();
            }

            await RefreshAccountScopeAsync(CancellationToken.None);
        }
    }

    // ── Steam sign-in ────────────────────────────────────────────────────────

    /// <summary>
    /// Runs one embedded Steam sign-in and renders what came back.
    ///
    /// <para><b>This method body is the only place
    /// <see cref="SteamSignInRequest.ConsentGranted"/> and
    /// <see cref="SteamSignInRequest.CapturePurchaseHistory"/> are set.</b>
    /// Pressing the button is the consent, so the mechanism cannot grant itself
    /// the consent that opens a window; and the capture flag is copied from the
    /// permission control and from nothing else, which is what makes acceptance
    /// criterion 2 a property of the code rather than of a convention.</para>
    ///
    /// <para>Awaited throughout. A fire-and-forget sign-in would let the panel
    /// redraw from state the write had not reached yet, which on this screen
    /// means telling a user they are not signed in immediately after they
    /// were.</para>
    /// </summary>
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task SignInToSteamAsync(CancellationToken ct)
    {
        SteamSignInNoticeMessage = null;
        SteamSignInProblemMessage = null;
        SteamSignInConfirmedAccount = null;

        if (_steamSignIn is null)
        {
            SteamSignInProblemMessage = SteamConnectionCopy.OutcomeUnavailable;
            return;
        }

        IsSigningInToSteam = true;
        try
        {
            var report = await _steamSignIn.SignInAsync(
                new SteamSignInRequest
                {
                    ConsentGranted = true,
                    CapturePurchaseHistory = CapturePurchaseHistory,
                },
                ct);

            ApplySteamSignIn(report);
        }
        catch (OperationCanceledException)
        {
            // Backing out is deliberate, so it is stated as a fact and never as a
            // fault — the same posture the Epic card takes.
            SteamSignInNoticeMessage = SteamConnectionCopy.OutcomeCancelled;
        }
        finally
        {
            IsSigningInToSteam = false;

            // CancellationToken.None: the panel has to end up truthful even when
            // the attempt was cancelled, and a cancelled read would leave it
            // showing the state that held before the window opened.
            await RefreshSteamAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Turns one report into the two sentences the card can show, and renders
    /// <see cref="SteamSignInReport.AccountConfirmed"/> rather than re-deriving
    /// it from anything.
    ///
    /// <para>The split follows the Epic card and the import screen: a closed
    /// window and a session nobody signed into are facts and stay neutral; a
    /// refused identity, a missing token and a broken browser are problems and
    /// wear the Amber edge.</para>
    /// </summary>
    private void ApplySteamSignIn(SteamSignInReport report)
    {
        if (report.SignedIn)
        {
            SteamSignInNoticeMessage = report.RefreshTokenCaptured
                ? SteamConnectionCopy.OutcomeSignedIn
                : SteamConnectionCopy.OutcomeNoRefreshToken;

            // Rendered, not re-derived. The sign-in wrote the owned account from
            // the token's subject claim, so the visibility toggle is live now
            // rather than at the next import (acceptance criterion 4).
            SteamSignInConfirmedAccount = report.AccountConfirmed;
            if (report.AccountConfirmed)
            {
                SteamAccountConfirmed = true;
            }

            return;
        }

        switch (report.Outcome)
        {
            case SteamSignInOutcome.Cancelled:
                SteamSignInNoticeMessage = SteamConnectionCopy.OutcomeCancelled;
                break;
            case SteamSignInOutcome.NotSignedIn:
                SteamSignInNoticeMessage = SteamConnectionCopy.OutcomeNotSignedIn;
                break;
            case SteamSignInOutcome.NoToken:
                SteamSignInProblemMessage = SteamConnectionCopy.OutcomeNoToken;
                break;
            case SteamSignInOutcome.IdentityMismatch:
                SteamSignInProblemMessage = SteamConnectionCopy.OutcomeIdentityMismatch;
                break;
            case SteamSignInOutcome.Unavailable:
                SteamSignInProblemMessage = SteamConnectionCopy.OutcomeUnavailable;
                break;
            default:
                SteamSignInProblemMessage = SteamConnectionCopy.OutcomeFailed;
                break;
        }
    }

    /// <summary>
    /// Ends the Steam session and discards the stored credential.
    ///
    /// <para>Awaited, and the panel is re-read afterwards rather than assumed:
    /// signing out also clears an identity the session earned, so the account
    /// filter's own state changes and the toggle above has to answer correctly on
    /// the next draw. The permission control resets with it — a permission
    /// granted for a session that no longer exists must be asked for again.</para>
    /// </summary>
    [RelayCommand]
    private async Task SignOutOfSteamAsync(CancellationToken ct)
    {
        if (_steamSignIn is null)
        {
            return;
        }

        await _steamSignIn.SignOutAsync(ct);

        SteamSignInNoticeMessage = null;
        SteamSignInProblemMessage = null;
        SteamSignInConfirmedAccount = null;
        CapturePurchaseHistory = false;

        await RefreshSteamAsync(ct);
    }

    // ── The Web API key, in the app ──────────────────────────────────────────

    private bool CanSaveSteamApiKey => !string.IsNullOrWhiteSpace(SteamApiKeyInput);

    /// <summary>
    /// Stores the pasted key and puts it into force without a restart.
    ///
    /// <para>The field is emptied on the way out. It is a bound property with a
    /// public getter, and there is no reason for a key to stay in one after the
    /// settings row has it.</para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSaveSteamApiKey))]
    private async Task SaveSteamApiKeyAsync(CancellationToken ct)
    {
        await _connections.SaveSteamApiKeyAsync(SteamApiKeyInput, ct);

        SteamApiKeyInput = string.Empty;
        SteamApiKeyNoticeMessage = SteamConnectionCopy.ApiKeySaved;

        await RefreshSteamAsync(ct);
    }

    /// <summary>
    /// Whether there is a key this screen can actually remove. A key supplied
    /// through the environment is not one, and the status line says so instead of
    /// offering a button that would appear not to work.
    /// </summary>
    private bool CanClearSteamApiKey => SteamHasApiKey && SteamApiKeyIsAppManaged;

    [RelayCommand(CanExecute = nameof(CanClearSteamApiKey))]
    private async Task ClearSteamApiKeyAsync(CancellationToken ct)
    {
        await _connections.ClearSteamApiKeyAsync(ct);

        SteamApiKeyInput = string.Empty;
        SteamApiKeyNoticeMessage = SteamConnectionCopy.ApiKeyCleared;

        await RefreshSteamAsync(ct);
    }

    /// <summary>
    /// Opens Steam's own key registration page through the shared dispatcher —
    /// the one place a URI leaves this application. A platform that declines says
    /// so rather than failing silently.
    /// </summary>
    [RelayCommand]
    private async Task OpenSteamApiKeyPageAsync()
    {
        if (_uris is null
            || !await _uris.OpenAsync(new Uri(SteamConnectionCopy.ApiKeyRegistrationUrl)))
        {
            SteamApiKeyNoticeMessage = SteamConnectionCopy.ApiKeyOpenFailed;
        }
    }

    /// <summary>
    /// Starts an Epic sign-in via <see cref="IStoreConnections"/> (§5.1).
    /// Cancellable; the generated cancel command closes the browser window.
    /// </summary>
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task SignInToEpicAsync(CancellationToken ct)
    {
        // Clear previous failure before the new attempt.
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
            // Belt-and-braces; service normally wraps cancellation in an outcome.
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

        // Failure doesn't change the existing session; redraw from stored state.
        ApplySession(await SafeSessionAsync());
        EpicProblem = outcome.Problem;
        EpicProblemMessage = outcome.Message;
    }

    /// <summary>Ends the Epic session and clears the stored credential.</summary>
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

    /// <summary>Reads the Epic session, swallowing errors so a failure path cannot cascade.</summary>
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
