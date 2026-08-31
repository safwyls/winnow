using Winnow.App.Services;
using Winnow.App.ViewModels;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// Stores panel view model driven by a fake <see cref="IStoreConnections"/>.
/// </summary>
public sealed class StoresViewModelTests
{
    // ── Reading the current state ────────────────────────────────────────────

    [Fact]
    public async Task Signed_out_offers_a_sign_in_and_claims_nothing_else()
    {
        var connections = new FakeStoreConnections();
        var stores = new StoresViewModel(connections);

        await stores.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(EpicConnection.SignedOut, stores.EpicState);
        Assert.True(stores.EpicCanSignIn);
        Assert.False(stores.EpicIsSignedIn);
        Assert.False(stores.ShowEpicAccountLine);
        Assert.Null(stores.EpicDisplayName);

        // A first open is not a failure, so nothing is reported as one.
        Assert.Equal(StoreSignInProblem.None, stores.EpicProblem);
        Assert.False(stores.ShowEpicProblem);
        Assert.Equal("NOT SIGNED IN", stores.EpicStatusLabel);
    }

    [Fact]
    public async Task A_live_session_shows_the_display_name_and_offers_sign_out()
    {
        var connections = new FakeStoreConnections
        {
            Session = new StoreSession(IsLive: true, DisplayName: "wanderer"),
        };
        var stores = new StoresViewModel(connections);

        await stores.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(EpicConnection.SignedIn, stores.EpicState);
        Assert.True(stores.EpicIsSignedIn);
        Assert.False(stores.EpicCanSignIn);
        Assert.True(stores.ShowEpicAccountLine);
        Assert.Equal("wanderer", stores.EpicAccountLine);
        Assert.False(stores.ShowEpicAnonymousLine);
        Assert.Equal("SIGNED IN", stores.EpicStatusLabel);
        Assert.True(stores.EpicStatusIsLive);
        Assert.False(stores.EpicStatusNeedsAttention);
    }

    [Fact]
    public async Task A_session_with_no_display_name_says_so_rather_than_leaving_a_gap()
    {
        var connections = new FakeStoreConnections
        {
            Session = new StoreSession(IsLive: true, DisplayName: null),
        };
        var stores = new StoresViewModel(connections);

        await stores.RefreshCommand.ExecuteAsync(null);

        Assert.True(stores.EpicIsSignedIn);
        Assert.False(stores.ShowEpicAccountLine);
        Assert.True(stores.ShowEpicAnonymousLine);
    }

    /// <summary>
    /// The fourth failure state, and the one that is not an attempt: the app was
    /// closed for long enough that Epic's refresh token lapsed. It must read as
    /// something that happened, not as Winnow having forgotten the user.
    /// </summary>
    [Fact]
    public async Task A_lapsed_session_is_named_as_an_expiry_not_as_an_absence()
    {
        var connections = new FakeStoreConnections
        {
            Session = new StoreSession(IsLive: false, DisplayName: "wanderer"),
        };
        var stores = new StoresViewModel(connections);

        await stores.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(EpicConnection.Lapsed, stores.EpicState);
        Assert.True(stores.EpicIsLapsed);
        Assert.Equal("SESSION EXPIRED", stores.EpicStatusLabel);

        // Amber, never Flare — Flare marks unread updates and nothing else.
        Assert.True(stores.EpicStatusNeedsAttention);
        Assert.False(stores.EpicStatusIsLive);

        // The way back is offered, and it is named for what it is.
        Assert.True(stores.EpicCanSignIn);
        Assert.Equal("Sign in again", stores.EpicSignInButtonText);

        // The account is still remembered, which is what makes the copy specific.
        Assert.True(stores.ShowEpicAccountLine);
        Assert.Equal("wanderer", stores.EpicAccountLine);
    }

    /// <summary>
    /// Opening the panel must never start a flow. A status read that could open
    /// a browser would be the worst possible surprise on a screen whose whole
    /// job is to explain itself before anything happens.
    /// </summary>
    [Fact]
    public async Task Opening_the_panel_never_starts_a_sign_in()
    {
        var connections = new FakeStoreConnections();
        var stores = new StoresViewModel(connections);

        await stores.RefreshCommand.ExecuteAsync(null);
        await stores.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(0, connections.SignInCalls);
        Assert.Equal(0, connections.SignOutCalls);
    }

    // ── Signing in ───────────────────────────────────────────────────────────

    [Fact]
    public async Task A_sign_in_in_progress_shows_progress_and_hides_the_button()
    {
        var connections = new FakeStoreConnections { Gate = new TaskCompletionSource() };
        var stores = new StoresViewModel(connections);

        var running = stores.SignInToEpicCommand.ExecuteAsync(null);

        Assert.Equal(EpicConnection.SigningIn, stores.EpicState);
        Assert.True(stores.EpicIsSigningIn);
        Assert.False(stores.EpicCanSignIn);
        Assert.False(stores.EpicIsSignedIn);

        // A second click cannot open a second browser window: AsyncRelayCommand
        // disallows concurrent execution by default, and this pins that the
        // default has not been overridden.
        Assert.False(stores.SignInToEpicCommand.CanExecute(null));

        connections.Gate.SetResult();
        await running;
    }

    /// <summary>
    /// The flow waits on a person finding a password manager and a phone, so the
    /// way out has to be a real one — not a window the user has to hunt for.
    /// </summary>
    [Fact]
    public async Task Cancelling_a_running_sign_in_ends_it_and_changes_nothing()
    {
        var connections = new FakeStoreConnections { Gate = new TaskCompletionSource() };
        var stores = new StoresViewModel(connections);

        var running = stores.SignInToEpicCommand.ExecuteAsync(null);
        Assert.True(stores.EpicIsSigningIn);

        stores.SignInToEpicCancelCommand.Execute(null);
        await running;

        Assert.Equal(EpicConnection.SignedOut, stores.EpicState);
        Assert.Equal(StoreSignInProblem.Cancelled, stores.EpicProblem);
        Assert.Equal(StoreSignInMessages.Cancelled, stores.EpicProblemMessage);

        // Backing out is deliberate, so the console route is not pushed at
        // someone who simply changed their mind.
        Assert.False(stores.ShowConsoleRoute);
    }

    [Fact]
    public async Task A_prompt_that_reports_a_cancel_is_worded_as_a_fact_not_a_fault()
    {
        var connections = new FakeStoreConnections
        {
            Outcome = Failure(StoreSignInProblem.Cancelled, StoreSignInMessages.Cancelled),
        };
        var stores = new StoresViewModel(connections);

        await stores.SignInToEpicCommand.ExecuteAsync(null);

        Assert.Equal(EpicConnection.SignedOut, stores.EpicState);
        Assert.Equal(StoreSignInProblem.Cancelled, stores.EpicProblem);
        Assert.True(stores.ShowEpicProblem);
        Assert.False(stores.ShowConsoleRoute);
    }

    [Fact]
    public async Task A_successful_sign_in_shows_the_account_and_clears_the_button()
    {
        var connections = new FakeStoreConnections
        {
            Outcome = new StoreSignInOutcome(true, "wanderer", Persisted: true, StoreSignInProblem.None, "Signed in."),
        };
        var stores = new StoresViewModel(connections);

        await stores.SignInToEpicCommand.ExecuteAsync(null);

        Assert.Equal(EpicConnection.SignedIn, stores.EpicState);
        Assert.Equal("wanderer", stores.EpicAccountLine);
        Assert.False(stores.EpicCanSignIn);
        Assert.False(stores.ShowEpicProblem);
        Assert.False(stores.EpicSessionNotPersisted);
    }

    /// <summary>
    /// A host that cannot encrypt at rest signs in for this run only. The
    /// consequence is invisible until the next launch, where it looks exactly
    /// like a bug, so the panel says it while the user is still here.
    /// </summary>
    [Fact]
    public async Task A_session_that_could_not_be_stored_says_so()
    {
        var connections = new FakeStoreConnections
        {
            Outcome = new StoreSignInOutcome(true, "wanderer", Persisted: false, StoreSignInProblem.None, "Signed in."),
        };
        var stores = new StoresViewModel(connections);

        await stores.SignInToEpicCommand.ExecuteAsync(null);

        Assert.True(stores.EpicIsSignedIn);
        Assert.True(stores.EpicSessionNotPersisted);
    }

    // ── Failures, each with its own remedy ───────────────────────────────────

    /// <summary>
    /// No WebView2 runtime and no console. The console peer is the answer, and
    /// it is exactly why that peer exists (<c>embedded-auth.md</c> §8).
    /// </summary>
    [Fact]
    public async Task No_prompt_could_run_here_offers_the_console_route()
    {
        var stores = await FailWith(StoreSignInProblem.NoPromptAvailable);

        Assert.True(stores.ShowEpicProblem);
        Assert.True(stores.ShowConsoleRoute);
        Assert.Contains("--epic-login", stores.ConsoleSignInCommandText, StringComparison.Ordinal);

        // Still offerable: the user may install the runtime and come back.
        Assert.True(stores.EpicCanSignIn);
    }

    /// <summary>
    /// The realistic failure mode: Epic changed its sign-in page and no capture
    /// route fired. Same remedy — the manual flow works while it is fixed.
    /// </summary>
    [Fact]
    public async Task A_changed_epic_page_offers_the_console_route()
    {
        var stores = await FailWith(StoreSignInProblem.NoCodeCaptured);

        Assert.True(stores.ShowEpicProblem);
        Assert.True(stores.ShowConsoleRoute);
    }

    /// <summary>
    /// A rejected code wants another attempt, not a different flow — codes are
    /// single-use and die in minutes, so the console route would fail the same
    /// way. Offering it here would send the user to the wrong remedy.
    /// </summary>
    [Fact]
    public async Task A_rejected_code_does_not_offer_the_console_route()
    {
        var stores = await FailWith(StoreSignInProblem.CodeRejected);

        Assert.True(stores.ShowEpicProblem);
        Assert.False(stores.ShowConsoleRoute);
        Assert.True(stores.EpicCanSignIn);
    }

    [Fact]
    public async Task An_unreachable_epic_does_not_offer_the_console_route()
    {
        var stores = await FailWith(StoreSignInProblem.Unreachable);

        Assert.True(stores.ShowEpicProblem);
        Assert.False(stores.ShowConsoleRoute);
    }

    [Fact]
    public async Task Rejected_client_credentials_are_reported_separately_from_a_rejected_code()
    {
        var stores = await FailWith(StoreSignInProblem.ClientRejected);

        Assert.Equal(StoreSignInProblem.ClientRejected, stores.EpicProblem);
        Assert.False(stores.ShowConsoleRoute);
    }

    /// <summary>
    /// Every documented failure leaves the existing session untouched, so a
    /// failed renewal of a lapsed session must still read "expired" — not
    /// "signed out", which would claim the attempt destroyed something.
    /// </summary>
    [Fact]
    public async Task A_failed_attempt_leaves_the_session_it_found_alone()
    {
        var connections = new FakeStoreConnections
        {
            Session = new StoreSession(IsLive: false, DisplayName: "wanderer"),
            Outcome = Failure(StoreSignInProblem.Unreachable, "Could not reach Epic."),
        };
        var stores = new StoresViewModel(connections);
        await stores.RefreshCommand.ExecuteAsync(null);

        await stores.SignInToEpicCommand.ExecuteAsync(null);

        Assert.Equal(EpicConnection.Lapsed, stores.EpicState);
        Assert.Equal("wanderer", stores.EpicAccountLine);
        Assert.True(stores.ShowEpicProblem);
    }

    [Fact]
    public async Task A_new_attempt_clears_the_previous_failure_before_it_starts()
    {
        var connections = new FakeStoreConnections
        {
            Outcome = Failure(StoreSignInProblem.CodeRejected, "Epic rejected the code."),
        };
        var stores = new StoresViewModel(connections);
        await stores.SignInToEpicCommand.ExecuteAsync(null);
        Assert.True(stores.ShowEpicProblem);

        connections.Gate = new TaskCompletionSource();
        var running = stores.SignInToEpicCommand.ExecuteAsync(null);

        // Leaving the old reason up while a new attempt runs would read as a
        // report on the attempt in progress.
        Assert.False(stores.ShowEpicProblem);
        Assert.Equal(StoreSignInProblem.None, stores.EpicProblem);

        connections.Gate.SetResult();
        await running;
    }

    // ── Signing out ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Signing_out_forgets_the_account_and_offers_the_way_back()
    {
        var connections = new FakeStoreConnections
        {
            Session = new StoreSession(IsLive: true, DisplayName: "wanderer"),
        };
        var stores = new StoresViewModel(connections);
        await stores.RefreshCommand.ExecuteAsync(null);

        await stores.SignOutOfEpicCommand.ExecuteAsync(null);

        Assert.Equal(1, connections.SignOutCalls);
        Assert.Equal(EpicConnection.SignedOut, stores.EpicState);
        Assert.Null(stores.EpicDisplayName);
        Assert.False(stores.ShowEpicAccountLine);
        Assert.True(stores.EpicCanSignIn);
        Assert.Equal("Sign in to Epic", stores.EpicSignInButtonText);
    }

    // ── Refresh while a flow is running ──────────────────────────────────────

    /// <summary>
    /// The panel can be closed and reopened while Epic's window is up. A refresh
    /// that reset the row would make a live attempt look like it had failed.
    /// </summary>
    [Fact]
    public async Task Refreshing_does_not_disturb_a_sign_in_in_progress()
    {
        var connections = new FakeStoreConnections { Gate = new TaskCompletionSource() };
        var stores = new StoresViewModel(connections);

        var running = stores.SignInToEpicCommand.ExecuteAsync(null);
        await stores.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(EpicConnection.SigningIn, stores.EpicState);

        connections.Gate.SetResult();
        await running;
    }

    // ── Steam ────────────────────────────────────────────────────────────────

    /// <summary>
    /// The point of the row: with nothing connected the user is looking at a
    /// smaller library than they own, and only this screen can tell them.
    /// </summary>
    [Fact]
    public async Task Steam_states_what_connecting_nothing_costs()
    {
        var stores = new StoresViewModel(new FakeStoreConnections { SteamConfigured = false });
        await stores.RefreshCommand.ExecuteAsync(null);

        Assert.False(stores.SteamWebApiConfigured);
        Assert.False(stores.SteamStatusIsLive);
        Assert.Equal(SteamConnectionCopy.StatusNoConnection, stores.SteamStatusLabel);
        Assert.Equal(SteamConnectionCopy.NothingConnectedCost, stores.SteamConnectionMessage);
    }

    [Fact]
    public async Task Steam_with_a_key_says_what_the_key_added()
    {
        var stores = new StoresViewModel(new FakeStoreConnections { SteamConfigured = true });
        await stores.RefreshCommand.ExecuteAsync(null);

        Assert.True(stores.SteamWebApiConfigured);
        Assert.True(stores.SteamStatusIsLive);
        Assert.Equal(SteamConnectionCopy.StatusKeySet, stores.SteamStatusLabel);
        Assert.Equal(SteamConnectionCopy.ConnectedAdds, stores.SteamConnectionMessage);
    }

    // ── GOG ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// The requirement is negative and therefore easy to regress: GOG must never
    /// grow a sign-in affordance, disabled or otherwise. The finding is stated
    /// in words instead, because it is a measurement and not a gap.
    /// </summary>
    [Fact]
    public void Gog_states_that_there_is_nothing_to_sign_into()
    {
        var stores = new StoresViewModel(new FakeStoreConnections());

        Assert.Contains("Not needed", stores.GogNoSignInMessage, StringComparison.OrdinalIgnoreCase);

        // The panel exposes exactly two sign-in commands and both are Epic's.
        // A GOG one appearing here is the regression this asserts against.
        var commands = typeof(StoresViewModel).GetProperties()
            .Select(p => p.Name)
            .Where(n => n.Contains("Gog", StringComparison.Ordinal) && n.EndsWith("Command", StringComparison.Ordinal));
        Assert.Empty(commands);
    }

    // ── Counts ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Counts_come_from_the_whole_library_and_render_tabular()
    {
        var stores = new StoresViewModel(
            new FakeStoreConnections(),
            new FakeStoreTitleCounts { ["steam"] = 1247, ["epic"] = 67, ["gog"] = 14 });

        await stores.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(1247, stores.SteamTitleCount);
        Assert.Equal("1,247", stores.SteamCountText);
        Assert.True(stores.ShowSteamCount);
        Assert.Equal("67", stores.EpicCountText);
        Assert.Equal("14", stores.GogCountText);
    }

    /// <summary>
    /// A source's silence recorded as an answer is this codebase's recurring
    /// failure mode. Before the library has loaded there is no count, and a zero
    /// would be a claim that the store contributed nothing.
    /// </summary>
    [Fact]
    public async Task A_store_with_no_titles_shows_no_count_rather_than_a_zero()
    {
        var stores = new StoresViewModel(
            new FakeStoreConnections(),
            new FakeStoreTitleCounts { ["steam"] = 616 });

        await stores.RefreshCommand.ExecuteAsync(null);

        Assert.True(stores.ShowSteamCount);
        Assert.False(stores.ShowEpicCount);
        Assert.False(stores.ShowGogCount);
    }

    [Fact]
    public async Task The_panel_composes_without_a_count_source()
    {
        var stores = new StoresViewModel(new FakeStoreConnections());

        await stores.RefreshCommand.ExecuteAsync(null);

        Assert.False(stores.ShowSteamCount);
        Assert.False(stores.ShowEpicCount);
        Assert.False(stores.ShowGogCount);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static StoreSignInOutcome Failure(StoreSignInProblem problem, string message)
        => new(false, null, false, problem, message);

    private static async Task<StoresViewModel> FailWith(StoreSignInProblem problem)
    {
        var stores = new StoresViewModel(new FakeStoreConnections
        {
            Outcome = Failure(problem, "Something went wrong, and this sentence says what to do."),
        });

        await stores.SignInToEpicCommand.ExecuteAsync(null);
        return stores;
    }
}

/// <summary>
/// The seam, faked. Deliberately dumb: it holds the answers the panel will get
/// and counts the calls, so a test names one state and asserts what the panel
/// draws for it.
/// </summary>
internal sealed class FakeStoreConnections : IStoreConnections
{
    /// <summary>Held open to keep a sign-in "in progress" for as long as a test needs.</summary>
    public TaskCompletionSource? Gate { get; set; }

    /// <summary>What the panel will be told exists on the Steam side.</summary>
    public SteamConnection Steam { get; set; } = SteamConnection.None;

    /// <summary>
    /// Shorthand for the commonest two shapes: a settings-table key and nothing
    /// else, or nothing at all. Written as a property so the tests that predate
    /// the two-method Steam card still read the way they were written.
    /// </summary>
    public bool SteamConfigured
    {
        get => Steam.HasApiKey;
        set => Steam = value
            ? SteamConnection.None with { HasApiKey = true, ApiKeyIsAppManaged = true }
            : SteamConnection.None;
    }

    /// <summary>The last key handed to <see cref="SaveSteamApiKeyAsync"/>, and the counts either way.</summary>
    public string? SavedApiKey { get; private set; }

    public int ApiKeySaves { get; private set; }

    public int ApiKeyClears { get; private set; }

    public StoreSession? Session { get; set; }

    public StoreSignInOutcome Outcome { get; set; }
        = new(false, null, false, StoreSignInProblem.Cancelled, StoreSignInMessages.Cancelled);

    public int SignInCalls { get; private set; }

    public int SignOutCalls { get; private set; }

    /// <summary>
    /// When set, the session half of the Steam answer is computed from a real
    /// session provider, so a sign-in a test just ran is visible to the panel on
    /// its next refresh. This is the join the real
    /// <see cref="StoreConnections"/> makes over the credential inventory; the
    /// key half stays scripted because no test here has a key chain.
    /// </summary>
    public Winnow.Enrich.SteamWeb.Credentials.ISteamSessionProvider? Sessions { get; set; }

    public TimeProvider Clock { get; set; } = TimeProvider.System;

    public async ValueTask<bool> IsSteamWebApiConfiguredAsync(CancellationToken ct = default)
        => (await GetSteamConnectionAsync(ct)).HasUsableCredential;

    public async ValueTask<SteamConnection> GetSteamConnectionAsync(CancellationToken ct = default)
    {
        if (Sessions is null)
        {
            return Steam;
        }

        var session = await Sessions.GetAsync(ct);

        return Steam with
        {
            HasSession = session is not null,
            SessionUsable = session?.IsAccessUsable(
                Clock.GetUtcNow(),
                Winnow.Enrich.SteamWeb.Credentials.SteamCredential.DefaultSkew) ?? false,
            SessionExpiresAt = session?.ExpiresAt,
            SessionAccount = session?.SteamId.Value.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    public Task SaveSteamApiKeyAsync(string? key, CancellationToken ct = default)
    {
        ApiKeySaves++;
        SavedApiKey = key;
        Steam = Steam with { HasApiKey = !string.IsNullOrWhiteSpace(key), ApiKeyIsAppManaged = true };
        return Task.CompletedTask;
    }

    public Task ClearSteamApiKeyAsync(CancellationToken ct = default)
    {
        ApiKeyClears++;
        SavedApiKey = null;
        Steam = Steam with { HasApiKey = false, ApiKeyIsAppManaged = false };
        return Task.CompletedTask;
    }

    public ValueTask<StoreSession?> GetEpicSessionAsync(CancellationToken ct = default)
        => ValueTask.FromResult(Session);

    public async Task<StoreSignInOutcome> SignInToEpicAsync(CancellationToken ct = default)
    {
        SignInCalls++;

        if (Gate is not null)
        {
            // WaitAsync throws on cancellation, which is exactly what a real
            // cancelled sign-in does — EpicSignInService rethrows rather than
            // dressing a cancellation as a failure — so the view model's own
            // handling of it is what gets exercised.
            await Gate.Task.WaitAsync(ct);
        }

        if (Outcome.Succeeded)
        {
            Session = new StoreSession(true, Outcome.DisplayName);
        }

        return Outcome;
    }

    public Task SignOutOfEpicAsync(CancellationToken ct = default)
    {
        SignOutCalls++;
        Session = null;
        return Task.CompletedTask;
    }
}

/// <summary>Per-store title counts, without a library or a database behind them.</summary>
internal sealed class FakeStoreTitleCounts : IStoreTitleCounts
{
    private readonly Dictionary<string, int> _counts = new(StringComparer.OrdinalIgnoreCase);

    public int this[string store]
    {
        get => _counts.GetValueOrDefault(store);
        set => _counts[store] = value;
    }

    public IReadOnlyDictionary<string, int> TitlesByStore() => _counts;
}

/// <summary>
/// A Stores panel for tests that need one only because
/// <see cref="MainWindowViewModel"/> requires it. Nothing is connected, which
/// is the state every such test wants.
/// </summary>
internal static class DetachedStores
{
    public static StoresViewModel Create() => new(new FakeStoreConnections());
}

/// <summary>
/// An Appearance screen for tests that need one only because
/// <see cref="MainWindowViewModel"/> requires it. No settings store, so nothing
/// is read and nothing is written; the theme service still resolves its palette
/// and reports the default, which is the state every such test wants.
/// </summary>
internal static class DetachedAppearance
{
    public static Winnow.App.ViewModels.AppearanceViewModel Create()
        => new(new Winnow.App.Services.ThemeService());
}
