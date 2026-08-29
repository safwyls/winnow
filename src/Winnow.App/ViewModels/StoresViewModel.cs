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

    public StoresViewModel(IStoreConnections connections, IStoreTitleCounts? counts = null)
    {
        _connections = connections;
        _counts = counts;
    }

    // ══ Rail and header ═════════════════════════════════════════════════════

    public string Title => "Platforms";

    public string IntroMessage =>
        "Where your library comes from. All three read local files; two can also sign in for more.";

    // ══ Steam ═══════════════════════════════════════════════════════════════

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(SteamStatusLabel), nameof(SteamStatusIsLive),
        nameof(SteamAddsMessage), nameof(ShowSteamKeyHint))]
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
