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

    // ── The Web API key save, and its refusal (TASK-78) ───────────────────────

    /// <summary>
    /// A stored key clears the field and says it is in use — the pre-TASK-78
    /// behaviour, kept because the happy path did not change.
    /// </summary>
    [Fact]
    public async Task A_saved_key_clears_the_field_and_says_it_is_in_use()
    {
        var stores = new StoresViewModel(new FakeStoreConnections())
        {
            SteamApiKeyInput = "0123456789ABCDEF",
        };

        await stores.SaveSteamApiKeyCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, stores.SteamApiKeyInput);
        Assert.Equal(SteamConnectionCopy.ApiKeySaved, stores.SteamApiKeyNoticeMessage);
        Assert.True(stores.ShowSteamApiKeyNotice);
    }

    /// <summary>
    /// A host that cannot encrypt refuses the save. The panel must not say the
    /// key was stored when it was not, and must not destroy the input the user
    /// now has to take somewhere else.
    /// </summary>
    [Fact]
    public async Task A_refused_save_says_so_and_keeps_the_input()
    {
        var stores = new StoresViewModel(new FakeStoreConnections { RefuseApiKeySaves = true })
        {
            SteamApiKeyInput = "0123456789ABCDEF",
        };

        await stores.SaveSteamApiKeyCommand.ExecuteAsync(null);

        Assert.Equal("0123456789ABCDEF", stores.SteamApiKeyInput);
        Assert.Equal(SteamConnectionCopy.ApiKeySaveRefused, stores.SteamApiKeyNoticeMessage);
    }

    // ── The settings segment's label (TASK-60) ───────────────────────────────

    /// <summary>
    /// The rename went in once and was reverted, because the label existed only
    /// as a literal in a XAML attribute and nothing could read it. It is a
    /// property now, and this is the thing that reads it.
    /// </summary>
    [Fact]
    public void The_settings_segment_reads_platforms()
    {
        var stores = new StoresViewModel(new FakeStoreConnections());

        Assert.Equal("PLATFORMS", stores.SegmentLabel);
        Assert.Equal("Platforms", stores.Title);

        Assert.DoesNotContain("STORES", stores.SegmentLabel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("STORES", stores.SegmentTooltip, StringComparison.OrdinalIgnoreCase);
    }

    // ── The folded purchase import (TASK-59) ─────────────────────────────────

    /// <summary>
    /// One place to connect Steam and one place to import purchase data. The
    /// import is a section of this panel now rather than a settings screen
    /// beside it.
    /// </summary>
    [Fact]
    public void The_purchase_import_is_a_section_of_this_panel()
    {
        var import = DetachedAccountImport.Create();
        var stores = new StoresViewModel(
            new FakeStoreConnections(), null, null, null, null, import);

        Assert.True(stores.ShowPurchaseImport);
        Assert.Same(import, stores.AccountImport);
    }

    // ── The Steam card's modals (TASK-61) ────────────────────────────────────

    /// <summary>
    /// All three start closed, only one can be open at a time, and Close shuts
    /// whichever is. They replaced disclosures, so nothing may be open on
    /// arrival.
    /// </summary>
    [Fact]
    public void The_three_modals_start_closed_and_are_mutually_exclusive()
    {
        var stores = new StoresViewModel(new FakeStoreConnections());

        Assert.False(stores.IsMethodsModalOpen);
        Assert.False(stores.IsSignInConsentOpen);
        Assert.False(stores.IsPurchaseModalOpen);
        Assert.False(stores.IsAnyModalOpen);

        stores.OpenMethodsModalCommand.Execute(null);
        Assert.True(stores.IsMethodsModalOpen);
        Assert.True(stores.IsAnyModalOpen);

        // Opening another closes the first rather than stacking on it.
        stores.OpenPurchaseModalCommand.Execute(null);
        Assert.False(stores.IsMethodsModalOpen);
        Assert.True(stores.IsPurchaseModalOpen);

        stores.OpenSignInConsentCommand.Execute(null);
        Assert.False(stores.IsPurchaseModalOpen);
        Assert.True(stores.IsSignInConsentOpen);

        stores.CloseModalCommand.Execute(null);
        Assert.False(stores.IsAnyModalOpen);
    }

    /// <summary>
    /// <b>ROADMAP §4.7 condition 3, made structural.</b> The card's sign-in
    /// button no longer signs in: it opens the consent surface, and only
    /// Continue starts the flow. The paragraph a user must read before acting
    /// is therefore on the near side of the act rather than beside it, and no
    /// reading order can skip it.
    /// </summary>
    [Fact]
    public void The_card_button_opens_consent_and_does_not_sign_in()
    {
        var connections = new FakeStoreConnections();
        var stores = new StoresViewModel(connections);

        stores.OpenSignInConsentCommand.Execute(null);

        Assert.True(stores.IsSignInConsentOpen);

        // Nothing was started. The Steam sign-in seam was never resolved here,
        // and the Epic one — the only one this fake counts — is untouched.
        Assert.Equal(0, connections.SignInCalls);

        // Backing out starts nothing either.
        stores.CloseModalCommand.Execute(null);
        Assert.False(stores.IsAnyModalOpen);
        Assert.Equal(0, connections.SignInCalls);
    }

    /// <summary>
    /// The permission rides in the consent modal now, and it is still unticked
    /// on arrival: opening the surface that explains it must not pre-answer it.
    /// </summary>
    [Fact]
    public void Opening_consent_leaves_the_purchase_permission_unticked()
    {
        var stores = new StoresViewModel(new FakeStoreConnections());

        Assert.False(stores.CapturePurchaseHistory);

        stores.OpenSignInConsentCommand.Execute(null);

        Assert.True(stores.IsSignInConsentOpen);
        Assert.False(stores.CapturePurchaseHistory);
    }

    /// <summary>
    /// The two state words the card leads with. LOCAL FILES is always On; WEB
    /// API names which credential is carrying the calls, and the key wins when
    /// both are held because keys do not expire.
    /// </summary>
    [Fact]
    public void The_two_state_words_say_what_is_on_and_which_credential_carries_it()
    {
        var stores = new StoresViewModel(new FakeStoreConnections());

        Assert.Equal("On", stores.SteamLocalStateText);

        Assert.Equal(SteamConnectionCopy.StateWebApiOff, stores.SteamWebApiStateText);
        Assert.False(stores.SteamWebApiIsOn);

        stores.SteamCredentials = SteamConnection.None with { HasSession = true };
        Assert.Equal(SteamConnectionCopy.StateWebApiOnLogin, stores.SteamWebApiStateText);
        Assert.True(stores.SteamWebApiIsOn);

        stores.SteamCredentials = SteamConnection.None with { HasApiKey = true };
        Assert.Equal(SteamConnectionCopy.StateWebApiOnApi, stores.SteamWebApiStateText);

        // Both held: the key is what does the scheduled work, so it is what the
        // line names.
        stores.SteamCredentials = SteamConnection.None with { HasApiKey = true, HasSession = true };
        Assert.Equal(SteamConnectionCopy.StateWebApiOnApi, stores.SteamWebApiStateText);
        Assert.True(stores.SteamWebApiIsOn);
    }

    /// <summary>
    /// The IN USE marker, and the property that makes it honest: it names the
    /// same credential the WEB API state word names, from the same rule, so the
    /// line at the top of the section and the marker beside the method cannot
    /// disagree.
    ///
    /// <para>It is deliberately not a radio. The two methods are peers and
    /// holding both is supported, so nothing on this card may claim that
    /// choosing one deselects the other.</para>
    /// </summary>
    [Fact]
    public void The_in_use_marker_names_the_same_credential_the_state_word_does()
    {
        var stores = new StoresViewModel(new FakeStoreConnections());

        // Neither held: nothing is in use, and nothing is marked.
        Assert.False(stores.SteamSignInIsInUse);
        Assert.False(stores.SteamApiKeyIsInUse);
        Assert.Equal(SteamConnectionCopy.StateWebApiOff, stores.SteamWebApiStateText);

        // Session only.
        stores.SteamCredentials = SteamConnection.None with { HasSession = true };
        Assert.True(stores.SteamSignInIsInUse);
        Assert.False(stores.SteamApiKeyIsInUse);
        Assert.Equal(SteamConnectionCopy.StateWebApiOnLogin, stores.SteamWebApiStateText);

        // Key only.
        stores.SteamCredentials = SteamConnection.None with { HasApiKey = true };
        Assert.False(stores.SteamSignInIsInUse);
        Assert.True(stores.SteamApiKeyIsInUse);
        Assert.Equal(SteamConnectionCopy.StateWebApiOnApi, stores.SteamWebApiStateText);

        // Both: the key wins, exactly one marker is drawn, and the tooltip
        // becomes the sentence that explains the split rather than implying
        // the sign-in is idle.
        stores.SteamCredentials = SteamConnection.None with { HasApiKey = true, HasSession = true };
        Assert.False(stores.SteamSignInIsInUse);
        Assert.True(stores.SteamApiKeyIsInUse);
        Assert.Equal(SteamConnectionCopy.StateWebApiOnApi, stores.SteamWebApiStateText);
        Assert.Equal(SteamConnectionCopy.BothCredentials, stores.MethodInUseTooltip);

        // Never both at once, in any state.
        Assert.False(stores.SteamSignInIsInUse && stores.SteamApiKeyIsInUse);
    }

    /// <summary>
    /// The accounts caveat moved off the card into a modal reached from the
    /// summary line, and it is one of the four the card now has.
    /// </summary>
    [Fact]
    public void The_accounts_caveat_is_reachable_from_the_summary_line()
    {
        var stores = new StoresViewModel(new FakeStoreConnections());

        Assert.False(stores.IsAccountsModalOpen);

        stores.OpenAccountsModalCommand.Execute(null);

        Assert.True(stores.IsAccountsModalOpen);
        Assert.True(stores.IsAnyModalOpen);

        // The text that used to be a note under the toggle is what it holds.
        Assert.Contains(
            "cannot attribute", stores.AccountScopeCaveatMessage, StringComparison.OrdinalIgnoreCase);

        // Still mutually exclusive with the other three.
        stores.OpenMethodsModalCommand.Execute(null);
        Assert.False(stores.IsAccountsModalOpen);

        stores.CloseModalCommand.Execute(null);
        Assert.False(stores.IsAnyModalOpen);
    }

    // ── The STEAM ACCOUNTS summary (TASK-61) ─────────────────────────────────

    /// <summary>
    /// The section says two figures and nothing else. No per-account breakdown
    /// is offered because no account NAME is stored anywhere — AccountFacts
    /// records presence, never identity — so the honest unit is the total and
    /// the count.
    /// </summary>
    [Theory]
    [InlineData(1247, 2, "1,247 games across 2 accounts")]
    [InlineData(1, 1, "1 game across 1 account")]
    [InlineData(0, 0, "0 games across 0 accounts")]
    public void The_accounts_section_states_the_total_and_the_account_count(
        int titles, int accounts, string expected)
    {
        var stores = new StoresViewModel(new FakeStoreConnections())
        {
            SteamTitleCount = titles,
            SteamAccountCount = accounts,
        };

        Assert.Equal(expected, stores.SteamAccountsSummaryText);

        // Absent until there is something to count, so a fresh install draws
        // no line rather than a line of zeroes.
        Assert.Equal(titles > 0 && accounts > 0, stores.ShowSteamAccountsSummary);
    }

    /// <summary>
    /// The clarifier that replaced the note. A disabled toggle is already
    /// visible; what a user cannot see is that the answer is coming rather than
    /// missing, and that is the one thing this adds.
    /// </summary>
    [Fact]
    public void An_unconfirmed_account_marks_the_section_as_pending()
    {
        var stores = new StoresViewModel(new FakeStoreConnections());

        Assert.False(stores.SteamAccountConfirmed);
        Assert.True(stores.ShowSteamAccountsPending);
        Assert.False(stores.CanChooseAccountScope);

        // The full sentence did not disappear with the note; it rides the
        // toggle tooltip.
        Assert.NotEmpty(stores.AccountScopeMessage);

        stores.SteamAccountConfirmed = true;

        Assert.False(stores.ShowSteamAccountsPending);
        Assert.True(stores.CanChooseAccountScope);
    }

    // ── The platform tabs (TASK-61) ──────────────────────────────────────────

    /// <summary>
    /// One card per screen. Steam is the default because it is the source most
    /// libraries are mostly made of, and because landing on an empty GOG card
    /// would say nothing about the library.
    /// </summary>
    [Fact]
    public void The_platform_tabs_open_on_steam()
    {
        var stores = new StoresViewModel(new FakeStoreConnections());

        Assert.Equal(StorePlatform.Steam, stores.SelectedPlatform);
        Assert.True(stores.IsSteamVisible);
        Assert.False(stores.IsEpicVisible);
        Assert.False(stores.IsGogVisible);

        // Bound rather than literal, for the reason TASK-60 recorded: a label
        // that lives only in a XAML attribute is one no test can see.
        Assert.Equal("STEAM", stores.SteamTabLabel);
        Assert.Equal("EPIC", stores.EpicTabLabel);
        Assert.Equal("GOG", stores.GogTabLabel);
    }

    /// <summary>
    /// Exactly one card is drawn at a time, whichever tab is chosen and in any
    /// order. Two visible at once would be the stacked screen this replaced.
    /// </summary>
    [Fact]
    public void Exactly_one_platform_card_is_visible_at_a_time()
    {
        var stores = new StoresViewModel(new FakeStoreConnections());

        foreach (var (command, expected) in new (System.Windows.Input.ICommand, StorePlatform)[]
        {
            (stores.ShowEpicCommand, StorePlatform.Epic),
            (stores.ShowGogCommand, StorePlatform.Gog),
            (stores.ShowSteamCommand, StorePlatform.Steam),
            (stores.ShowGogCommand, StorePlatform.Gog),
        })
        {
            command.Execute(null);

            Assert.Equal(expected, stores.SelectedPlatform);
            Assert.Single(
                new[] { stores.IsSteamVisible, stores.IsEpicVisible, stores.IsGogVisible },
                visible => visible);
        }
    }

    /// <summary>
    /// The cost of putting a card off screen, and the rule that pays it. A
    /// failing Steam session must stay legible (ROADMAP §4.7 condition 8), and
    /// a lapsed Epic session is the same shape of fact. The condition was
    /// written when every card was drawn at once; once a card can be hidden
    /// behind a tab, only the tab can carry the signal. Amber, never Flare.
    /// </summary>
    [Fact]
    public async Task A_platform_needing_attention_marks_its_tab_from_another_tab()
    {
        var connections = new FakeStoreConnections
        {
            Session = new StoreSession(IsLive: false, DisplayName: "wanderer"),
        };
        var stores = new StoresViewModel(connections);

        await stores.RefreshCommand.ExecuteAsync(null);

        // Epic has lapsed while Steam is the selected tab.
        Assert.True(stores.IsSteamVisible);
        Assert.False(stores.IsEpicVisible);
        Assert.True(stores.EpicTabNeedsAttention);

        // A dying Steam session marks its own tab from anywhere else.
        stores.SteamSessionState = Winnow.Enrich.SteamWeb.Credentials.SteamSessionHealth.RenewalFailing;
        stores.ShowGogCommand.Execute(null);

        Assert.False(stores.IsSteamVisible);
        Assert.True(stores.SteamTabNeedsAttention);

        // GOG has nothing to sign into, so it has no state to miss.
        Assert.False(stores.GogTabNeedsAttention);
    }

    /// <summary>
    /// TASK-61. PURCHASE HISTORY sits behind one button, closed on arrival,
    /// because it was the largest block on a card that overflowed its viewport
    /// and it is the one section a user need not visit to have a working
    /// library. The section label stays at the top level, so the card still
    /// names what is behind the button.
    /// </summary>
    [Fact]
    public void The_purchase_import_arrives_closed_behind_one_button()
    {
        var stores = new StoresViewModel(
            new FakeStoreConnections(), null, null, null, null, DetachedAccountImport.Create());

        Assert.False(stores.IsPurchaseModalOpen);

        // The section is still announced at the top level; only its body moved.
        // The label is now the whole of the row beside the button: the summary
        // line went because the modal opens on the same sentence.
        Assert.Equal(SteamConnectionCopy.PurchaseSectionLabel, stores.SteamPurchaseSectionLabel);

        stores.OpenPurchaseModalCommand.Execute(null);

        Assert.True(stores.IsPurchaseModalOpen);
    }

    /// <summary>
    /// ROADMAP §4.7 condition 3, which is why the toggle holds the WHOLE
    /// section and not the prose inside it. Each route's paragraph is the
    /// transparency surface read before acting, and the two routes are equal
    /// peers; a panel that hid one paragraph, or opened one route without the
    /// other, would break that. Both explanations therefore remain properties
    /// of the import itself, reached through one button, neither nearer than
    /// the other.
    /// </summary>
    [Fact]
    public void Neither_import_route_has_its_explanation_hidden_behind_the_other()
    {
        var import = DetachedAccountImport.Create();
        var stores = new StoresViewModel(
            new FakeStoreConnections(), null, null, null, null, import);

        stores.OpenPurchaseModalCommand.Execute(null);
        Assert.True(stores.IsPurchaseModalOpen);

        // One modal holds both, so the two paragraphs are always in the same
        // state as each other: there is no per-route disclosure to disagree.
        Assert.Equal(
            SteamAccountImportCopy.SignInRouteExplanation, import.SignInRouteExplanation);
        Assert.Equal(
            SteamAccountImportCopy.SavedPagesRouteExplanation, import.SavedPagesRouteExplanation);

        // Only the saved-page HINTS keep a disclosure of their own, and it is
        // closed by default. The explanation above it is not behind it.
        Assert.False(import.SavedPagesHintsOpen);
    }

    /// <summary>
    /// A host that composed the panel without the import hides the section
    /// rather than drawing a dead one.
    /// </summary>
    [Fact]
    public void A_panel_composed_without_the_import_draws_no_purchase_section()
    {
        var stores = new StoresViewModel(new FakeStoreConnections());

        Assert.False(stores.ShowPurchaseImport);
        Assert.Null(stores.AccountImport);
    }

    /// <summary>
    /// Arriving on this panel asks the embedded browser whether it could run
    /// here — the question the standalone screen used to ask on arrival, which
    /// opens no window and does no IO. Without it the folded section would draw
    /// its default answer instead of this machine's.
    /// </summary>
    [Fact]
    public async Task Refreshing_the_panel_refreshes_the_folded_import()
    {
        // No harvester behind it, so the honest answer is "not here" and the
        // default of true is what a refresh that never ran would leave.
        var import = DetachedAccountImport.Create();
        var stores = new StoresViewModel(
            new FakeStoreConnections(), null, null, null, null, import);

        Assert.True(import.SignInRouteAvailable);

        await stores.RefreshCommand.ExecuteAsync(null);

        Assert.False(import.SignInRouteAvailable);
        Assert.True(import.ShowSignInUnavailable);
    }

    /// <summary>
    /// <b>Acceptance criterion 3.</b> A user who declines the browser sign-in
    /// still reaches the files they saved themselves. Nothing about the
    /// saved-file route is gated on a credential, a session or a harvester, so
    /// the panel in its emptiest state still offers it.
    /// </summary>
    [Fact]
    public async Task The_saved_file_import_is_reachable_with_no_sign_in_and_no_key()
    {
        var import = DetachedAccountImport.Create();
        var stores = new StoresViewModel(
            new FakeStoreConnections { SteamConfigured = false },
            null,
            null,
            null,
            null,
            import);

        await stores.RefreshCommand.ExecuteAsync(null);

        // Nothing is connected, and the embedded route cannot run here.
        Assert.False(stores.SteamHasSession);
        Assert.False(stores.SteamHasApiKey);
        Assert.False(import.SignInRouteAvailable);

        // The saved-file route is offered anyway.
        Assert.True(stores.ShowPurchaseImport);
        Assert.True(import.ImportFromSavedPagesCommand.CanExecute(null));
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

        // The regression this guards against is GOG growing a sign-in
        // affordance. It used to assert that NO command named for GOG existed,
        // which was the same thing while the screen had no per-platform
        // navigation; TASK-61's tabs added ShowGogCommand, which selects a card
        // and connects to nothing. The rule is therefore stated as what it
        // always meant — no GOG sign-in, sign-out or connect command — and the
        // tab is named as the one permitted exception so a second one cannot
        // arrive unnoticed.
        var gogCommands = typeof(StoresViewModel).GetProperties()
            .Select(p => p.Name)
            .Where(n => n.Contains("Gog", StringComparison.Ordinal)
                && n.EndsWith("Command", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(["ShowGogCommand"], gogCommands);

        Assert.DoesNotContain(
            gogCommands,
            n => n.Contains("SignIn", StringComparison.OrdinalIgnoreCase)
                || n.Contains("SignOut", StringComparison.OrdinalIgnoreCase)
                || n.Contains("Connect", StringComparison.OrdinalIgnoreCase));
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

    /// <summary>Whether the next save is refused — the host-that-cannot-encrypt branch.</summary>
    public bool RefuseApiKeySaves { get; set; }

    public Task<Winnow.Enrich.SteamWeb.Credentials.SteamApiKeySaveOutcome> SaveSteamApiKeyAsync(
        string? key, CancellationToken ct = default)
    {
        ApiKeySaves++;
        SavedApiKey = key;
        Steam = Steam with { HasApiKey = !string.IsNullOrWhiteSpace(key), ApiKeyIsAppManaged = true };
        return Task.FromResult(RefuseApiKeySaves
            ? Winnow.Enrich.SteamWeb.Credentials.SteamApiKeySaveOutcome.Refused
            : Winnow.Enrich.SteamWeb.Credentials.SteamApiKeySaveOutcome.Stored);
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
