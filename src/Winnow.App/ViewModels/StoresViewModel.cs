using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.App.Services;

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

    /// <summary>Guards against writing the preference back while it is being read.</summary>
    private bool _loadingAccountScope;

    public StoresViewModel(
        IStoreConnections connections,
        IStoreTitleCounts? counts = null,
        IAccountVisibility? accountVisibility = null)
    {
        _connections = connections;
        _counts = counts;
        _accountVisibility = accountVisibility;
    }

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

    public string IntroMessage =>
        "Where your library comes from. All three read local files; two can also sign in for more.";

    // ══ Steam ═══════════════════════════════════════════════════════════════

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(SteamStatusLabel), nameof(SteamStatusIsLive),
        nameof(SteamAddsMessage), nameof(ShowSteamKeyHint),
        // The disabled toggle's explanation names the key state, so it has to
        // be redrawn when that changes — otherwise a user who has just set a key
        // is still being told to set one.
        nameof(AccountScopeBlockedMessage))]
    public partial bool SteamWebApiConfigured { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SteamCountText), nameof(ShowSteamCount))]
    public partial int SteamTitleCount { get; set; }

    public string SteamCountText => SteamTitleCount.ToString("N0");

    public bool ShowSteamCount => SteamTitleCount > 0;

    public string SteamLocalMessage =>
        "Always on. Reads installed and played games, playtime and last-played dates from Steam's local files.";

    public string SteamStatusLabel => SteamWebApiConfigured ? "WEB API KEY SET" : "NO WEB API KEY";

    public bool SteamStatusIsLive => SteamWebApiConfigured;

    public string SteamAddsMessage => SteamWebApiConfigured
        ? "Also reads your full owned list from Steam, including games never installed on this PC."
        : "Local files only include games installed or played on this PC. Set an API key to see your full library.";

    public bool ShowSteamKeyHint => !SteamWebApiConfigured;

    public string SteamKeyHintMessage =>
        "Set Steam__ApiKey as an environment variable, or in appsettings.local.json beside the executable.";

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
    /// Why the toggle is disabled, in the user's terms. Two genuinely different
    /// states behind one disabled control, and they need different remedies —
    /// one is "go and get a key", the other is "wait, this fixes itself".
    /// Collapsing them into a single sentence would send half the users looking
    /// for a setting that is already correct.
    /// </summary>
    public string AccountScopeBlockedMessage => SteamWebApiConfigured
        ? "Your API key is set but Winnow has not confirmed which account it belongs to yet. "
          + "This happens automatically during the next Steam import."
        : "Set a Steam Web API key first. Winnow needs it to identify which account is yours.";

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
