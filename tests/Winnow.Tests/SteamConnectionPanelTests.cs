using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Core.Auth;
using Winnow.Core.Repositories;
using Winnow.Enrich.SteamWeb;
using Winnow.Enrich.SteamWeb.Credentials;
using Winnow.Tests.SteamWeb;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// TASK-55 S5. The Stores screen's Steam card, where the two connection methods
/// are presented as peers.
///
/// <para>Everything here is asserted at the view-model level, against the copy
/// constants rather than against pasted sentences: a screen that is almost
/// entirely prose is one where a test quoting the prose becomes a second place
/// the prose is written, and the two then drift.</para>
/// </summary>
public class SteamConnectionPanelTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 9, 0, 0, TimeSpan.Zero);

    // ══ The four credential states ══════════════════════════════════════════

    [Fact]
    public async Task Nothing_connected_reads_as_no_connection()
    {
        var panel = await Panel(Credentials(key: false, session: false));

        Assert.False(panel.SteamWebApiConfigured);
        Assert.False(panel.SteamHasApiKey);
        Assert.False(panel.SteamHasSession);
        Assert.Equal(SteamConnectionCopy.StatusNoConnection, panel.SteamStatusLabel);
        Assert.Equal(SteamConnectionCopy.NothingConnectedCost, panel.SteamConnectionMessage);
        Assert.Equal(SteamConnectionCopy.HealthNotSignedIn, panel.SteamSessionHealthMessage);

        // A connection nobody has made is not an error and must not wear the
        // error treatment. TASK-80 is to give that state a named role.
        Assert.False(panel.SteamStatusIsLive);
        Assert.False(panel.SteamStatusNeedsAttention);
        Assert.False(panel.ShowSteamBothCredentials);
    }

    [Fact]
    public async Task A_key_alone_reads_as_key_set()
    {
        var panel = await Panel(Credentials(key: true, session: false));

        Assert.True(panel.SteamWebApiConfigured);
        Assert.Equal(SteamConnectionCopy.StatusKeySet, panel.SteamStatusLabel);
        Assert.Equal(SteamConnectionCopy.ConnectedAdds, panel.SteamConnectionMessage);
        Assert.Equal(SteamConnectionCopy.ApiKeySet, panel.SteamApiKeyStatusMessage);
        Assert.True(panel.SteamStatusIsLive);
        Assert.False(panel.ShowSteamBothCredentials);

        // The sign-in is still offered in full. A key does not make the other
        // method disappear, or the presentation stops being a choice.
        Assert.True(panel.ShowSteamSignInAction);
        Assert.False(panel.ShowSteamSignedIn);
    }

    /// <summary>
    /// The keyless signed-in user, and the S1-noted gap this stage closes: the
    /// "is Steam configured" answer used to read the key chain alone, so this
    /// user — who holds a working credential — was told they had none.
    /// </summary>
    [Fact]
    public async Task A_sign_in_alone_reads_as_signed_in_and_as_configured()
    {
        var panel = await Panel(
            Credentials(key: false, session: true), SteamSessionHealth.Live);

        Assert.True(panel.SteamWebApiConfigured);
        Assert.False(panel.SteamHasApiKey);
        Assert.True(panel.SteamHasSession);
        Assert.Equal(SteamConnectionCopy.StatusSignedIn, panel.SteamStatusLabel);
        Assert.Equal(SteamConnectionCopy.ApiKeyNotSet, panel.SteamApiKeyStatusMessage);
        Assert.True(panel.SteamStatusIsLive);
        Assert.False(panel.ShowSteamBothCredentials);

        // Working: the account and the way out, and nothing to press.
        Assert.True(panel.ShowSteamSignedIn);
        Assert.False(panel.ShowSteamSignInAction);
    }

    [Fact]
    public async Task Both_credentials_read_as_both_and_state_the_scheduler_rule()
    {
        var panel = await Panel(
            Credentials(key: true, session: true), SteamSessionHealth.Live);

        Assert.Equal(SteamConnectionCopy.StatusSignedInAndKeySet, panel.SteamStatusLabel);

        // The decision note's rule, said out loud rather than left to be
        // inferred from a sync that kept working after a sign-in lapsed.
        Assert.True(panel.ShowSteamBothCredentials);
        Assert.Equal(SteamConnectionCopy.BothCredentials, panel.SteamBothCredentialsMessage);
    }

    // ══ Session health, all six states ══════════════════════════════════════

    [Theory]
    [InlineData(SteamSessionHealth.NotSignedIn, false)]
    [InlineData(SteamSessionHealth.Live, false)]
    [InlineData(SteamSessionHealth.RenewalDue, false)]
    [InlineData(SteamSessionHealth.RenewalFailing, true)]
    [InlineData(SteamSessionHealth.Expired, true)]
    [InlineData(SteamSessionHealth.NotPersisted, true)]
    public async Task Every_health_state_has_its_own_message(
        SteamSessionHealth health, bool attention)
    {
        var panel = await Panel(Credentials(key: false, session: true), health);

        Assert.Equal(Expected(health), panel.SteamSessionHealthMessage);
        Assert.Equal(attention, panel.ShowSteamSessionAttention);

        static string Expected(SteamSessionHealth health) => health switch
        {
            SteamSessionHealth.Live => SteamConnectionCopy.HealthLive,
            SteamSessionHealth.RenewalDue => SteamConnectionCopy.HealthRenewalDue,
            SteamSessionHealth.RenewalFailing => SteamConnectionCopy.HealthRenewalFailing,
            SteamSessionHealth.Expired => SteamConnectionCopy.HealthExpired,
            SteamSessionHealth.NotPersisted => SteamConnectionCopy.HealthNotPersisted,
            _ => SteamConnectionCopy.HealthNotSignedIn,
        };
    }

    /// <summary>
    /// The six messages are six distinct sentences. Collapsing any two would
    /// leave a user unable to tell "renewal is failing" from "this is dead",
    /// which is the silent degradation the decision note forbids.
    /// </summary>
    [Fact]
    public void The_six_health_messages_are_distinct()
    {
        string[] messages =
        [
            SteamConnectionCopy.HealthNotSignedIn,
            SteamConnectionCopy.HealthLive,
            SteamConnectionCopy.HealthRenewalDue,
            SteamConnectionCopy.HealthRenewalFailing,
            SteamConnectionCopy.HealthExpired,
            SteamConnectionCopy.HealthNotPersisted,
        ];

        Assert.Equal(messages.Length, messages.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// The six combined labels are likewise six, and the two that carry a
    /// problem are the only two on the Amber treatment. Flare appears nowhere on
    /// this screen: it marks unread updates and nothing else (§2).
    /// </summary>
    [Theory]
    [InlineData(SteamSessionHealth.RenewalDue, false, false)]
    [InlineData(SteamSessionHealth.RenewalFailing, true, false)]
    [InlineData(SteamSessionHealth.Expired, true, false)]
    [InlineData(SteamSessionHealth.Live, false, true)]
    [InlineData(SteamSessionHealth.NotPersisted, false, true)]
    public async Task Only_a_failing_or_dead_sign_in_takes_the_attention_treatment(
        SteamSessionHealth health, bool attention, bool live)
    {
        var panel = await Panel(Credentials(key: true, session: true), health);

        Assert.Equal(attention, panel.SteamStatusNeedsAttention);
        Assert.Equal(live, panel.SteamStatusIsLive);
    }

    [Theory]
    [InlineData(SteamSessionHealth.RenewalDue)]
    [InlineData(SteamSessionHealth.RenewalFailing)]
    public async Task A_sign_in_that_needs_renewing_says_so_and_offers_the_way_back(
        SteamSessionHealth health)
    {
        var panel = await Panel(Credentials(key: false, session: true), health);

        Assert.Equal(SteamConnectionCopy.StatusSignInNeedsRenewing, panel.SteamStatusLabel);

        // One click back in, and the button renames itself rather than pretending
        // this is a first connection.
        Assert.True(panel.ShowSteamSignInAction);
        Assert.Equal(SteamConnectionCopy.SignInAgainButton, panel.SteamSignInButtonText);
    }

    [Fact]
    public async Task An_expired_sign_in_is_named_as_an_expiry_not_as_an_absence()
    {
        var panel = await Panel(
            Credentials(key: false, session: true), SteamSessionHealth.Expired);

        Assert.Equal(SteamConnectionCopy.StatusSignInExpired, panel.SteamStatusLabel);
        Assert.True(panel.SteamStatusNeedsAttention);

        // The account is still remembered, which is what makes it an expiry
        // rather than Winnow having forgotten them.
        Assert.True(panel.ShowSteamSignedIn);
        Assert.True(panel.ShowSteamSignedInAccount);
        Assert.Equal(SteamSessionFixtures.Subject, panel.SteamSignedInAccountText);
    }

    // ══ Acceptance criterion 2: the purchase-history permission ═════════════

    [Fact]
    public async Task The_permission_is_unticked_and_a_declined_sign_in_still_succeeds()
    {
        var host = Host(Minted());
        await host.Panel.RefreshCommand.ExecuteAsync(null);

        // Nobody has touched it, so nothing was granted.
        Assert.False(host.Panel.CapturePurchaseHistory);

        await host.Panel.SignInToSteamCommand.ExecuteAsync(null);

        Assert.False(host.Session.Requested!.CapturePurchaseHistory);

        // Declining is a complete answer: the sign-in is fully functional, the
        // account was recorded, and the pages were never asked for.
        Assert.True(host.Session.Requested.ConsentGranted);
        Assert.Equal(SteamConnectionCopy.OutcomeSignedIn, host.Panel.SteamSignInNoticeMessage);
        Assert.False(host.Panel.ShowSteamSignInProblem);
        Assert.True(host.Panel.SteamHasSession);
        Assert.True(host.Panel.SteamSignInConfirmedAccount);
    }

    /// <summary>
    /// <b>The proof that the control is the only thing that sets the flag.</b>
    /// Every other writable property on the panel is driven to a non-default
    /// value first — including the ones a careless refactor would be tempted to
    /// wire the request up from — and the request still carries false. Then the
    /// control alone is ticked, and it carries true.
    /// </summary>
    [Fact]
    public async Task Only_the_permission_control_sets_the_capture_flag()
    {
        var host = Host(Minted());
        await host.Panel.RefreshCommand.ExecuteAsync(null);

        foreach (var property in typeof(StoresViewModel).GetProperties()
                     .Where(p => p.CanWrite && p.CanRead && p.SetMethod is { IsPublic: true }
                         && p.Name != nameof(StoresViewModel.CapturePurchaseHistory)))
        {
            Disturb(host.Panel, property);
        }

        await host.Panel.SignInToSteamCommand.ExecuteAsync(null);
        Assert.False(host.Session.Requested!.CapturePurchaseHistory);

        host.Panel.CapturePurchaseHistory = true;
        await host.Panel.SignInToSteamCommand.ExecuteAsync(null);
        Assert.True(host.Session.Requested.CapturePurchaseHistory);
    }

    [Fact]
    public async Task Signing_out_withdraws_the_permission()
    {
        var host = Host(Minted());
        host.Panel.CapturePurchaseHistory = true;
        await host.Panel.SignInToSteamCommand.ExecuteAsync(null);

        await host.Panel.SignOutOfSteamCommand.ExecuteAsync(null);

        // A permission granted for a session that no longer exists has to be
        // asked for again.
        Assert.False(host.Panel.CapturePurchaseHistory);
    }

    // ══ Sign-in and sign-out ════════════════════════════════════════════════

    [Fact]
    public async Task Opening_the_panel_never_starts_a_steam_sign_in()
    {
        var host = Host(Minted());

        await host.Panel.RefreshCommand.ExecuteAsync(null);
        await host.Panel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(0, host.Session.SignInCalls);
    }

    /// <summary>
    /// Acceptance criterion 4, rendered rather than re-derived: the report says
    /// the account was recorded, and the visibility toggle is live on the same
    /// frame — no import, no Year in Review call, no waiting.
    /// </summary>
    [Fact]
    public async Task A_successful_sign_in_renders_the_reports_own_account_confirmation()
    {
        var host = Host(Minted());

        await host.Panel.SignInToSteamCommand.ExecuteAsync(null);

        Assert.True(host.Panel.SteamSignInConfirmedAccount);
        Assert.True(host.Panel.ShowSteamSignInAccountConfirmed);
        Assert.Equal(SteamConnectionCopy.AccountConfirmed, host.Panel.SteamSignInAccountConfirmedMessage);
        Assert.True(host.Panel.SteamAccountConfirmed);
        Assert.False(host.Panel.ShowAccountScopeBlocked);
    }

    [Fact]
    public async Task A_sign_in_with_no_refresh_token_is_reported_rather_than_dressed_up()
    {
        var host = Host(Minted(withRefresh: false));

        await host.Panel.SignInToSteamCommand.ExecuteAsync(null);

        Assert.Equal(SteamConnectionCopy.OutcomeNoRefreshToken, host.Panel.SteamSignInNoticeMessage);
        Assert.False(host.Panel.ShowSteamSignInProblem);
        Assert.True(host.Panel.SteamHasSession);
    }

    [Theory]
    [InlineData(SteamSignInOutcome.NoToken)]
    [InlineData(SteamSignInOutcome.IdentityMismatch)]
    [InlineData(SteamSignInOutcome.Unavailable)]
    [InlineData(SteamSignInOutcome.Failed)]
    public async Task A_refused_sign_in_is_reported_as_a_problem(SteamSignInOutcome outcome)
    {
        var host = Host(Barren(outcome));

        await host.Panel.SignInToSteamCommand.ExecuteAsync(null);

        Assert.True(host.Panel.ShowSteamSignInProblem);
        Assert.False(host.Panel.ShowSteamSignInNotice);
        Assert.False(host.Panel.SteamHasSession);
    }

    /// <summary>
    /// Backing out is deliberate, so it is stated as a fact and never as a
    /// fault — the same posture the Epic card and the import screen take.
    /// </summary>
    [Theory]
    [InlineData(SteamSignInOutcome.Cancelled)]
    [InlineData(SteamSignInOutcome.NotSignedIn)]
    public async Task A_closed_window_is_a_fact_not_a_fault(SteamSignInOutcome outcome)
    {
        var host = Host(Barren(outcome));

        await host.Panel.SignInToSteamCommand.ExecuteAsync(null);

        Assert.True(host.Panel.ShowSteamSignInNotice);
        Assert.False(host.Panel.ShowSteamSignInProblem);
    }

    [Fact]
    public async Task A_sign_in_in_progress_shows_progress_and_holds_the_button()
    {
        var host = Host(Minted());
        host.Session.Gate = new TaskCompletionSource();

        var running = host.Panel.SignInToSteamCommand.ExecuteAsync(null);

        Assert.True(host.Panel.ShowSteamSignInBusy);
        Assert.False(host.Panel.ShowSteamSignInAction);
        Assert.False(host.Panel.SignInToSteamCommand.CanExecute(null));

        host.Session.Gate.SetResult();
        await running;

        Assert.False(host.Panel.ShowSteamSignInBusy);
    }

    [Fact]
    public async Task Cancelling_a_running_sign_in_ends_it_and_stores_nothing()
    {
        var host = Host(Minted());
        host.Session.Gate = new TaskCompletionSource();

        var running = host.Panel.SignInToSteamCommand.ExecuteAsync(null);
        host.Panel.SignInToSteamCancelCommand.Execute(null);
        await running;

        Assert.Equal(SteamConnectionCopy.OutcomeCancelled, host.Panel.SteamSignInNoticeMessage);
        Assert.False(host.Panel.ShowSteamSignInProblem);
        Assert.Null(await host.Sessions.GetAsync());
    }

    /// <summary>
    /// Sign-out clears the identity the session earned, so the account filter
    /// answers correctly on the next draw rather than at the next backfill pass.
    /// </summary>
    [Fact]
    public async Task Signing_out_clears_the_identity_the_session_earned()
    {
        var host = Host(Minted());
        await host.Panel.SignInToSteamCommand.ExecuteAsync(null);
        Assert.True(host.Panel.SteamAccountConfirmed);

        await host.Panel.SignOutOfSteamCommand.ExecuteAsync(null);

        Assert.Null(await host.Sessions.GetAsync());
        Assert.False(host.Panel.SteamHasSession);
        Assert.Equal(SteamSessionHealth.NotSignedIn, host.Panel.SteamSessionState);
        Assert.Null(host.Panel.SteamSignInConfirmedAccount);
        Assert.False(host.Panel.SteamAccountConfirmed);
        Assert.True(host.Panel.ShowAccountScopeBlocked);
    }

    [Fact]
    public async Task A_host_with_no_sign_in_still_offers_the_key_and_says_the_window_cannot_open()
    {
        // No SteamSignInService at all: the sign-in card says it cannot run here
        // rather than vanishing, because a method that disappears is a method the
        // user cannot find out about.
        var connections = new FakeStoreConnections { Steam = Credentials(key: true, session: false) };
        var panel = new StoresViewModel(connections);

        await panel.RefreshCommand.ExecuteAsync(null);

        Assert.True(panel.ShowSteamSignInUnavailable);
        Assert.Equal(SteamConnectionCopy.StatusKeySet, panel.SteamStatusLabel);

        await panel.SignInToSteamCommand.ExecuteAsync(null);
        Assert.Equal(SteamConnectionCopy.OutcomeUnavailable, panel.SteamSignInProblemMessage);
    }

    // ══ The in-app key field ════════════════════════════════════════════════

    [Fact]
    public void The_save_command_refuses_an_empty_field()
    {
        var host = Host(Minted());

        Assert.False(host.Panel.SaveSteamApiKeyCommand.CanExecute(null));

        host.Panel.SteamApiKeyInput = "   ";
        Assert.False(host.Panel.SaveSteamApiKeyCommand.CanExecute(null));

        host.Panel.SteamApiKeyInput = "ABCDEF";
        Assert.True(host.Panel.SaveSteamApiKeyCommand.CanExecute(null));
    }

    [Fact]
    public async Task Saving_a_key_hands_it_over_empties_the_field_and_says_it_is_in_use()
    {
        var host = Host(Minted());
        await host.Panel.RefreshCommand.ExecuteAsync(null);

        host.Panel.SteamApiKeyInput = "0123456789ABCDEF0123456789ABCDEF";
        await host.Panel.SaveSteamApiKeyCommand.ExecuteAsync(null);

        Assert.Equal(1, host.Connections.ApiKeySaves);
        Assert.Equal("0123456789ABCDEF0123456789ABCDEF", host.Connections.SavedApiKey);

        // A bound property with a public getter is no place for a bearer
        // credential once the settings row has it.
        Assert.Equal(string.Empty, host.Panel.SteamApiKeyInput);

        Assert.Equal(SteamConnectionCopy.ApiKeySaved, host.Panel.SteamApiKeyNoticeMessage);
        Assert.True(host.Panel.SteamHasApiKey);
        Assert.Equal(SteamConnectionCopy.StatusKeySet, host.Panel.SteamStatusLabel);
    }

    [Fact]
    public async Task Clearing_removes_the_key_and_withdraws_the_clear_command()
    {
        var host = Host(Minted(), Credentials(key: true, session: false));
        await host.Panel.RefreshCommand.ExecuteAsync(null);

        Assert.True(host.Panel.ClearSteamApiKeyCommand.CanExecute(null));

        await host.Panel.ClearSteamApiKeyCommand.ExecuteAsync(null);

        Assert.Equal(1, host.Connections.ApiKeyClears);
        Assert.Equal(SteamConnectionCopy.ApiKeyCleared, host.Panel.SteamApiKeyNoticeMessage);
        Assert.False(host.Panel.SteamHasApiKey);
        Assert.Equal(SteamConnectionCopy.ApiKeyNotSet, host.Panel.SteamApiKeyStatusMessage);
        Assert.False(host.Panel.ClearSteamApiKeyCommand.CanExecute(null));
    }

    /// <summary>
    /// A key from <c>Steam__ApiKey</c> is not this screen's to delete. The Clear
    /// button is disabled rather than hidden, and the status line has already
    /// said why — a Clear that appeared not to work would be read as a bug.
    /// </summary>
    [Fact]
    public async Task A_key_from_the_environment_cannot_be_cleared_here_and_says_so()
    {
        var host = Host(
            Minted(),
            SteamConnection.None with { HasApiKey = true, ApiKeyIsAppManaged = false });

        await host.Panel.RefreshCommand.ExecuteAsync(null);

        Assert.True(host.Panel.SteamHasApiKey);
        Assert.False(host.Panel.SteamApiKeyIsAppManaged);
        Assert.Equal(SteamConnectionCopy.ApiKeyFromEnvironment, host.Panel.SteamApiKeyStatusMessage);
        Assert.False(host.Panel.ClearSteamApiKeyCommand.CanExecute(null));

        // Saving one here is still allowed: the settings table wins the chain.
        host.Panel.SteamApiKeyInput = "MINE";
        Assert.True(host.Panel.SaveSteamApiKeyCommand.CanExecute(null));
    }

    [Fact]
    public async Task The_key_page_is_opened_through_the_shared_dispatcher()
    {
        var host = Host(Minted());

        await host.Panel.OpenSteamApiKeyPageCommand.ExecuteAsync(null);

        var opened = Assert.Single(host.Uris.Opened);
        Assert.Equal(SteamConnectionCopy.ApiKeyRegistrationUrl, opened.ToString());
        Assert.False(host.Panel.ShowSteamApiKeyNotice);
    }

    [Fact]
    public async Task A_platform_that_declines_the_link_says_so_rather_than_failing_silently()
    {
        var host = Host(Minted());
        host.Uris.Succeed = false;

        await host.Panel.OpenSteamApiKeyPageCommand.ExecuteAsync(null);

        Assert.Equal(SteamConnectionCopy.ApiKeyOpenFailed, host.Panel.SteamApiKeyNoticeMessage);
    }

    // ══ The account-scope message's three branches ══════════════════════════

    [Fact]
    public async Task A_signed_in_user_never_sees_the_blocked_message()
    {
        var host = Host(Minted());
        await host.Panel.SignInToSteamCommand.ExecuteAsync(null);

        Assert.True(host.Panel.SteamAccountConfirmed);
        Assert.True(host.Panel.CanChooseAccountScope);
        Assert.False(host.Panel.ShowAccountScopeBlocked);
    }

    [Fact]
    public async Task A_key_only_unconfirmed_user_is_told_the_next_import_settles_it()
    {
        var panel = await Panel(Credentials(key: true, session: false));

        Assert.True(panel.ShowAccountScopeBlocked);
        Assert.Equal(SteamConnectionCopy.AccountScopeBlockedKeyOnly, panel.AccountScopeBlockedMessage);
    }

    [Fact]
    public async Task A_user_with_neither_credential_is_told_both_routes_fix_it()
    {
        var panel = await Panel(Credentials(key: false, session: false));

        Assert.True(panel.ShowAccountScopeBlocked);
        Assert.Equal(
            SteamConnectionCopy.AccountScopeBlockedNothingConnected, panel.AccountScopeBlockedMessage);
    }

    /// <summary>
    /// The third branch, for the state that should not occur: a session exists
    /// and the account was never recorded. It says "sign in again" rather than
    /// repeating advice about a key the user did not choose.
    /// </summary>
    [Fact]
    public async Task A_signed_in_user_whose_account_was_not_recorded_is_told_to_sign_in_again()
    {
        var panel = await Panel(
            Credentials(key: false, session: true), SteamSessionHealth.Live);

        Assert.True(panel.ShowAccountScopeBlocked);
        Assert.Equal(SteamConnectionCopy.AccountScopeBlockedSignedIn, panel.AccountScopeBlockedMessage);
    }

    /// <summary>The three branches are three sentences, not one wearing three hats.</summary>
    [Fact]
    public void The_three_blocked_branches_are_distinct()
    {
        string[] branches =
        [
            SteamConnectionCopy.AccountScopeBlockedKeyOnly,
            SteamConnectionCopy.AccountScopeBlockedNothingConnected,
            SteamConnectionCopy.AccountScopeBlockedSignedIn,
        ];

        Assert.Equal(branches.Length, branches.Distinct(StringComparer.Ordinal).Count());
    }

    // ══ The copy says what the code does ════════════════════════════════════
    //
    // This screen is almost entirely prose, and prose is the one part of it a
    // compiler cannot check. These assert the load-bearing FACTS rather than the
    // wording: each one is a sentence that would be a lie if the code changed
    // underneath it.

    /// <summary>
    /// Decision note 2, in the copy. Both halves have to be there — which
    /// credential does the scheduled work, and why — or the sentence stops being
    /// the reason the dual state is legible.
    /// </summary>
    [Fact]
    public void The_both_credentials_sentence_names_the_key_and_the_reason()
    {
        Assert.Contains("key", SteamConnectionCopy.BothCredentials, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Scheduled", SteamConnectionCopy.BothCredentials, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not expire", SteamConnectionCopy.BothCredentials, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// S6 shipped automatic renewal. The copy must state that renewal is
    /// automatic, name the caveat that it is untested against live servers,
    /// and point to the API-key alternative. HealthRenewalDue must not tell
    /// the user to sign in again, because renewal handles it.
    /// </summary>
    [Fact]
    public void Renewal_copy_states_automatic_renewal_and_names_its_limits()
    {
        Assert.Contains(
            "renews it automatically", SteamConnectionCopy.SignInCosts, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "may not work", SteamConnectionCopy.SignInCosts, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "API key", SteamConnectionCopy.SignInCosts, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "sign in again", SteamConnectionCopy.HealthRenewalDue, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The environment-key sentence has to carry all three parts, because the
    /// user who presses a disabled Clear has already read it: where the key came
    /// from, that saving here wins, and that clearing here does not.
    /// </summary>
    [Fact]
    public void The_environment_key_sentence_names_the_variable_and_both_consequences()
    {
        Assert.Contains(
            ConfigurationApiKeySource.SectionName + "__" + ConfigurationApiKeySource.ApiKeyName,
            SteamConnectionCopy.ApiKeyFromEnvironment,
            StringComparison.Ordinal);
        Assert.Contains(
            "precedence", SteamConnectionCopy.ApiKeyFromEnvironment, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "cannot remove", SteamConnectionCopy.ApiKeyFromEnvironment, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The permission's explanation has to say that declining is complete,
    /// because that is acceptance criterion 2 stated to the person it is for. It
    /// also has to be true, and it is: the pages are behind the flag in
    /// <c>WebView2SteamSignInSession</c>, so an unticked box means they are never
    /// navigated to.
    /// </summary>
    [Fact]
    public void The_permission_says_that_declining_is_a_complete_answer()
    {
        Assert.Contains(
            "complete answer",
            SteamConnectionCopy.PurchaseHistoryPermissionExplanation,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "never opened",
            SteamConnectionCopy.PurchaseHistoryPermissionExplanation,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A user whose browser will not open still has to be able to reach the
    /// page, so the address is in the sentence rather than only behind the
    /// button that just failed.
    /// </summary>
    [Fact]
    public void A_link_that_will_not_open_still_gives_the_address()
    {
        Assert.Contains(
            SteamConnectionCopy.ApiKeyRegistrationUrl,
            SteamConnectionCopy.ApiKeyOpenFailed,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The two costs are the whole reason this is a choice rather than a default
    /// with an escape hatch, so each card has to state its own.
    /// </summary>
    [Fact]
    public void Each_method_states_what_it_gives_up()
    {
        // The sign-in gives up durability.
        Assert.Contains("about a day", SteamConnectionCopy.SignInCosts, StringComparison.OrdinalIgnoreCase);

        // The key gives up identity and purchase history.
        Assert.Contains("account filter", SteamConnectionCopy.ApiKeyCosts, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("purchase history", SteamConnectionCopy.ApiKeyCosts, StringComparison.OrdinalIgnoreCase);
    }

    // ══ The condensed top level (TASK-61) ═══════════════════════════════════
    //
    // The card's top level is now the method, its state and its control; the
    // depth sits behind four disclosures. These pin the two halves of that: the
    // top level still answers "what can I do and where am I" in every credential
    // state, and the one thing a disclosure may never hold is a session the user
    // has to act on.

    /// <summary>
    /// All four credential combinations, and in each of them the top level names
    /// every method's state and offers every method's control. A condensed
    /// screen that stopped saying what a method's state was would be a smaller
    /// screen, not a clearer one.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Every_credential_combination_shows_each_methods_state_and_control(
        bool key, bool session)
    {
        var panel = await Panel(
            Credentials(key, session),
            session ? SteamSessionHealth.Live : SteamSessionHealth.NotSignedIn);

        // Three states, one per method, and none of them blank.
        Assert.False(string.IsNullOrWhiteSpace(panel.SteamLocalStateText));
        Assert.False(string.IsNullOrWhiteSpace(panel.SteamSignInStateText));
        Assert.False(string.IsNullOrWhiteSpace(panel.SteamApiKeyStateText));
        Assert.False(string.IsNullOrWhiteSpace(panel.SteamStatusLabel));
        Assert.False(string.IsNullOrWhiteSpace(panel.SteamConnectionSummaryMessage));

        // The sign-in always offers a control: a way out once there is a
        // session, and a way in until there is one.
        Assert.Equal(session, panel.ShowSteamSignedIn);
        Assert.Equal(!session, panel.ShowSteamSignInAction);

        // The key's field is always live, and Clear is offered only when there
        // is a key this screen owns.
        Assert.Equal(key, panel.ClearSteamApiKeyCommand.CanExecute(null));

        // The disclosures are shut, and the top level held up anyway.
        Assert.False(panel.SteamLocalDetailsOpen);
        Assert.False(panel.SteamMethodsDetailsOpen);
        Assert.False(panel.SteamSignInDetailsOpen);
        Assert.False(panel.SteamApiKeyDetailsOpen);
    }

    /// <summary>
    /// <b>The §4.7 amendment's condition 8, and the constraint that outranks
    /// brevity.</b> A session that cannot renew must say so before it dies. All
    /// three of those states raise the top-level attention treatment with their
    /// full sentence, with every disclosure shut — and the sentence is NOT one
    /// of the calm ones the sign-in disclosure carries.
    /// </summary>
    [Theory]
    [InlineData(SteamSessionHealth.RenewalFailing)]
    [InlineData(SteamSessionHealth.Expired)]
    [InlineData(SteamSessionHealth.NotPersisted)]
    public async Task A_session_that_cannot_renew_surfaces_at_the_top_level(
        SteamSessionHealth health)
    {
        var panel = await Panel(Credentials(key: true, session: true), health);

        Assert.False(panel.SteamSignInDetailsOpen);

        // Drawn at the top level with the attention edge, whatever is collapsed.
        Assert.True(panel.ShowSteamSessionAttention);
        Assert.False(panel.ShowSteamSessionCalmHealth);
        Assert.False(string.IsNullOrWhiteSpace(panel.SteamSessionHealthMessage));

        // And a control is at the top level with it, so the warning is never a
        // dead end. Which one depends on what the state needs: a fresh sign-in
        // for the two that have stopped working, and — for a session that works
        // but was never written to disk — the sign-out that ends the one it
        // has, since there is nothing to repair until the next launch.
        Assert.True(panel.ShowSteamSignInAction || panel.ShowSteamSignedIn);

        if (health is not SteamSessionHealth.NotPersisted)
        {
            Assert.True(panel.ShowSteamSignInAction);
            Assert.Equal(SteamConnectionCopy.SignInAgainButton, panel.SteamSignInButtonText);
        }
    }

    /// <summary>
    /// The complement, and the actual goal: the three states that are facts
    /// rather than faults do NOT raise the top-level treatment, and their
    /// sentences are what the disclosure carries.
    /// </summary>
    [Theory]
    [InlineData(SteamSessionHealth.NotSignedIn)]
    [InlineData(SteamSessionHealth.Live)]
    [InlineData(SteamSessionHealth.RenewalDue)]
    public async Task A_calm_session_keeps_its_sentence_in_the_disclosure(
        SteamSessionHealth health)
    {
        var panel = await Panel(Credentials(key: false, session: true), health);

        Assert.False(panel.ShowSteamSessionAttention);
        Assert.True(panel.ShowSteamSessionCalmHealth);
        Assert.False(string.IsNullOrWhiteSpace(panel.SteamSessionHealthMessage));
    }

    /// <summary>
    /// Every disclosure starts shut, every one opens, and every one names what
    /// it holds when shut and how to shut it when open. A toggle whose label did
    /// not change would leave the user pressing a control with no visible
    /// effect on it.
    /// </summary>
    [Fact]
    public async Task The_four_disclosures_start_shut_and_open_on_their_own_command()
    {
        var panel = await Panel(Credentials(key: true, session: true), SteamSessionHealth.Live);

        (Func<bool> Open, Action Toggle, Func<string> Label)[] disclosures =
        [
            (() => panel.SteamLocalDetailsOpen,
                () => panel.ToggleSteamLocalDetailsCommand.Execute(null),
                () => panel.SteamLocalDetailsToggleText),
            (() => panel.SteamMethodsDetailsOpen,
                () => panel.ToggleSteamMethodsDetailsCommand.Execute(null),
                () => panel.SteamMethodsDetailsToggleText),
            (() => panel.SteamSignInDetailsOpen,
                () => panel.ToggleSteamSignInDetailsCommand.Execute(null),
                () => panel.SteamSignInDetailsToggleText),
            (() => panel.SteamApiKeyDetailsOpen,
                () => panel.ToggleSteamApiKeyDetailsCommand.Execute(null),
                () => panel.SteamApiKeyDetailsToggleText),
        ];

        foreach (var (open, toggle, label) in disclosures)
        {
            Assert.False(open());
            var shut = label();

            toggle();
            Assert.True(open());
            Assert.NotEqual(shut, label());

            toggle();
            Assert.False(open());
            Assert.Equal(shut, label());
        }
    }

    /// <summary>
    /// Nothing was deleted. Every sentence the old top level printed is still a
    /// value this panel produces; what changed is which of them the card draws
    /// before you ask. Asserted against the copy constants rather than pasted
    /// prose, for the reason stated at the top of this file.
    /// </summary>
    [Fact]
    public async Task The_disclosures_still_carry_everything_that_left_the_top_level()
    {
        var panel = await Panel(Credentials(key: true, session: true), SteamSessionHealth.Live);

        Assert.Equal(SteamConnectionCopy.LocalFiles, panel.SteamLocalMessage);
        Assert.Equal(SteamConnectionCopy.ConnectedAdds, panel.SteamConnectionMessage);
        Assert.Equal(SteamConnectionCopy.SectionIntro, panel.SteamConnectionIntroMessage);
        Assert.Equal(SteamConnectionCopy.BothCredentials, panel.SteamBothCredentialsMessage);
        Assert.Equal(SteamConnectionCopy.SignInGives, panel.SteamSignInGivesMessage);
        Assert.Equal(SteamConnectionCopy.SignInCosts, panel.SteamSignInCostsMessage);
        Assert.Equal(SteamConnectionCopy.SignOutExplanation, panel.SteamSignOutMessage);
        Assert.Equal(
            SteamConnectionCopy.PurchaseHistoryPermissionExplanation,
            panel.CapturePurchaseHistoryMessage);
        Assert.Equal(SteamConnectionCopy.ApiKeyGives, panel.SteamApiKeyGivesMessage);
        Assert.Equal(SteamConnectionCopy.ApiKeyCosts, panel.SteamApiKeyCostsMessage);
        Assert.Equal(SteamConnectionCopy.ApiKeySet, panel.SteamApiKeyStatusMessage);
    }

    /// <summary>
    /// The terse lines are as many as the states they stand for. Collapsing two
    /// of them would undo at the top level exactly what the six distinct health
    /// sentences were written to prevent.
    /// </summary>
    [Fact]
    public void The_terse_state_lines_are_one_per_state()
    {
        string[] signIn =
        [
            SteamConnectionCopy.StateSignInNone,
            SteamConnectionCopy.StateSignInLive,
            SteamConnectionCopy.StateSignInRenewalDue,
            SteamConnectionCopy.StateSignInRenewalFailing,
            SteamConnectionCopy.StateSignInExpired,
            SteamConnectionCopy.StateSignInNotPersisted,
        ];
        Assert.Equal(signIn.Length, signIn.Distinct(StringComparer.Ordinal).Count());

        string[] key =
        [
            SteamConnectionCopy.StateApiKeyNotSet,
            SteamConnectionCopy.StateApiKeySet,
            SteamConnectionCopy.StateApiKeyExternal,
        ];
        Assert.Equal(key.Length, key.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// Every health value has a terse line of its own, taken from the same
    /// switch the full sentences use, so the two can never disagree about which
    /// state is showing.
    /// </summary>
    [Theory]
    [InlineData(SteamSessionHealth.NotSignedIn)]
    [InlineData(SteamSessionHealth.Live)]
    [InlineData(SteamSessionHealth.RenewalDue)]
    [InlineData(SteamSessionHealth.RenewalFailing)]
    [InlineData(SteamSessionHealth.Expired)]
    [InlineData(SteamSessionHealth.NotPersisted)]
    public async Task Every_health_state_has_a_terse_line_too(SteamSessionHealth health)
    {
        var panel = await Panel(Credentials(key: false, session: true), health);

        Assert.False(string.IsNullOrWhiteSpace(panel.SteamSignInStateText));
    }

    /// <summary>
    /// The one terse line that carries a consequence rather than only a state.
    /// The Clear button beside it is disabled, and a disabled control whose
    /// reason is inside a collapsed panel reads as a bug — so the top-level line
    /// says it cannot be cleared here, and the disclosure carries the rest.
    /// </summary>
    [Fact]
    public async Task An_environment_key_says_at_the_top_level_that_it_cannot_be_cleared_here()
    {
        var panel = await Panel(
            SteamConnection.None with { HasApiKey = true, ApiKeyIsAppManaged = false });

        Assert.Equal(SteamConnectionCopy.StateApiKeyExternal, panel.SteamApiKeyStateText);
        Assert.False(panel.ClearSteamApiKeyCommand.CanExecute(null));
        Assert.False(panel.SteamApiKeyDetailsOpen);

        // The state line is the one place a user reads this without opening
        // anything, so it has to say the consequence and not only the state.
        Assert.Contains("clear", SteamConnectionCopy.StateApiKeyExternal, StringComparison.OrdinalIgnoreCase);
    }

    // ══ Helpers ═════════════════════════════════════════════════════════════

    private static SteamConnection Credentials(bool key, bool session) => new(
        HasApiKey: key,
        ApiKeyIsAppManaged: key,
        HasSession: session,
        SessionUsable: session,
        SessionExpiresAt: session ? Now.AddHours(24) : null,
        SessionAccount: session ? SteamSessionFixtures.Subject : null);

    /// <summary>A panel in one named state, with no sign-in service behind it.</summary>
    private static async Task<StoresViewModel> Panel(
        SteamConnection credentials, SteamSessionHealth health = SteamSessionHealth.NotSignedIn)
    {
        var panel = new StoresViewModel(new FakeStoreConnections { Steam = credentials });
        await panel.RefreshCommand.ExecuteAsync(null);

        // Set after the refresh, which a service-less host answers NotSignedIn
        // for. The health mapping is the thing under test here, so it is stated
        // rather than manufactured out of a clock and a token.
        panel.SteamSessionState = health;
        return panel;
    }

    private static SteamSignInResult Minted(bool withRefresh = true)
        => SteamSignInResult.SignedIn(
            SteamSessionFixtures.AccessToken(Now.AddHours(24)),
            Now.AddHours(24),
            SteamSessionFixtures.Subject,
            ["web:store"],
            "steam",
            withRefresh ? SteamSessionFixtures.RefreshToken(Now.AddDays(207)) : null);

    private static SteamSignInResult Barren(SteamSignInOutcome outcome) => outcome switch
    {
        SteamSignInOutcome.NoToken => SteamSignInResult.NoToken("no page minted a token"),
        SteamSignInOutcome.NotSignedIn => SteamSignInResult.NotSignedIn("nobody signed in"),
        SteamSignInOutcome.IdentityMismatch => SteamSignInResult.IdentityMismatch("the two disagree"),
        SteamSignInOutcome.Cancelled => SteamSignInResult.Cancelled("the window was closed"),
        SteamSignInOutcome.Unavailable => SteamSignInResult.Unavailable("no embedded browser"),
        _ => SteamSignInResult.Failed("the browser broke"),
    };

    /// <summary>
    /// A panel wired to a real <see cref="SteamSignInService"/> over a scripted
    /// browser session and an in-memory session store, so the command path is the
    /// product's and only the browser is faked.
    /// </summary>
    private static (
        StoresViewModel Panel,
        FakeStoreConnections Connections,
        RecordingSignInSession Session,
        ISteamSessionProvider Sessions,
        RecordingUriDispatcher Uris)
        Host(SteamSignInResult result, SteamConnection? credentials = null)
    {
        var browser = new RecordingSignInSession(result);
        var clock = new FixedClock(Now);
        var sessions = new SteamSessionProvider(new InMemorySteamSessionStore(), new SteamWebOptions(), clock);
        var settings = new InMemorySettingsRepository();
        var confirmation = new SteamAccountConfirmation(settings, keys: null, sessions: sessions);
        var service = new SteamSignInService(
            browser, sessions, clock, log: null, confirmation: confirmation);

        var connections = new FakeStoreConnections
        {
            Steam = credentials ?? SteamConnection.None,
            Sessions = sessions,
            Clock = clock,
        };

        var uris = new RecordingUriDispatcher();
        var visibility = new SettingsAccountVisibility(settings);

        return (new StoresViewModel(connections, null, visibility, service, uris),
            connections, browser, sessions, uris);
    }

    /// <summary>
    /// Drives one writable property to something that is not its default, so the
    /// capture-flag proof covers the whole public surface rather than the members
    /// the author happened to think of.
    /// </summary>
    private static void Disturb(StoresViewModel panel, System.Reflection.PropertyInfo property)
    {
        object? value = property.PropertyType switch
        {
            var t when t == typeof(bool) => !(bool)(property.GetValue(panel) ?? false),
            var t when t == typeof(bool?) => true,
            var t when t == typeof(int) => 7,
            var t when t == typeof(string) => "disturbed",
            var t when t == typeof(SteamSessionHealth) => SteamSessionHealth.RenewalFailing,
            var t when t == typeof(SteamConnection) => Credentials(key: true, session: true),
            var t when t == typeof(EpicConnection) => EpicConnection.Lapsed,
            var t when t == typeof(StoreSignInProblem) => StoreSignInProblem.Unreachable,
            _ => null,
        };

        if (value is not null)
        {
            property.SetValue(panel, value);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

/// <summary>
/// A browser sign-in that returns a scripted result and remembers exactly what
/// it was asked for. The request it records is what acceptance criterion 2 is
/// asserted against.
/// </summary>
internal sealed class RecordingSignInSession(SteamSignInResult result) : ISteamSignInSession
{
    /// <summary>Held open to keep a sign-in "in progress" for as long as a test needs.</summary>
    public TaskCompletionSource? Gate { get; set; }

    public SteamSignInRequest? Requested { get; private set; }

    public int SignInCalls { get; private set; }

    public string Name => "test:recording";

    public ValueTask<bool> IsAvailableAsync(CancellationToken ct = default)
        => ValueTask.FromResult(true);

    public async Task<SteamSignInResult> SignInAsync(
        SteamSignInRequest request, CancellationToken ct = default)
    {
        SignInCalls++;
        Requested = request;

        if (Gate is not null)
        {
            await Gate.Task.WaitAsync(ct);
        }

        return result;
    }
}

/// <summary>Records the URIs the panel handed to the OS, and can decline them.</summary>
internal sealed class RecordingUriDispatcher : IUriDispatcher
{
    public List<Uri> Opened { get; } = [];

    public bool Succeed { get; set; } = true;

    public Task<bool> OpenAsync(Uri uri)
    {
        Opened.Add(uri);
        return Task.FromResult(Succeed);
    }
}

/// <summary>
/// The account-visibility seam over a settings table alone: it answers whether
/// an account has been confirmed, which is the half these tests need, and counts
/// nothing because there is no library behind it.
/// </summary>
internal sealed class SettingsAccountVisibility(ISettingsRepository settings) : IAccountVisibility
{
    public async Task<AccountVisibilityState> GetAsync(CancellationToken ct = default)
        => new(
            Winnow.Core.Queries.SteamOwnedAccount.Clean(
                await settings.GetAsync(Winnow.Core.Queries.SteamOwnedAccount.RefSettingKey, ct)) is not null,
            OwnAccountOnly: false,
            HiddenCount: 0);

    public Task SetOwnAccountOnlyAsync(bool ownAccountOnly, CancellationToken ct = default)
        => Task.CompletedTask;
}
