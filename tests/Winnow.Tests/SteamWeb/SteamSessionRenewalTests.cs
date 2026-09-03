using System.Net;
using System.Text.Json;
using Winnow.Enrich.SteamWeb;
using Winnow.Enrich.SteamWeb.Credentials;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Winnow.Tests.SteamWeb;

/// <summary>
/// A canned <see cref="ISteamSessionRenewer"/>. These tests are about what the
/// provider does with an outcome — single-flight, rotation, latching, health —
/// not about how the outcome was obtained, which
/// <see cref="SteamSessionRenewerTests"/> covers against canned HTTP.
/// </summary>
internal sealed class FakeSteamSessionRenewer : ISteamSessionRenewer
{
    private readonly Func<SteamSession, int, SteamRenewalOutcome> _responder;
    private int _calls;

    public FakeSteamSessionRenewer(Func<SteamSession, int, SteamRenewalOutcome> responder)
        => _responder = responder;

    public FakeSteamSessionRenewer(SteamRenewalOutcome always)
        : this((_, _) => always)
    {
    }

    /// <summary>How many times a refresh token was actually spent.</summary>
    public int Calls => Volatile.Read(ref _calls);

    /// <summary>Completed the moment a renewal is in flight, so a test can join it deterministically.</summary>
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>When set, the renewal hangs until it is completed. This is what "in flight" means here.</summary>
    public TaskCompletionSource? Hold { get; init; }

    /// <summary>The sessions handed in, in order. Rotation asserts on what the second call was given.</summary>
    public List<SteamSession> Seen { get; } = [];

    public async Task<SteamRenewalOutcome> RenewAsync(SteamSession session, CancellationToken ct = default)
    {
        var index = Interlocked.Increment(ref _calls) - 1;

        lock (Seen)
        {
            Seen.Add(session);
        }

        Entered.TrySetResult();

        if (Hold is { } hold)
        {
            await hold.Task.WaitAsync(ct);
        }

        return _responder(session, index);
    }
}

/// <summary>
/// A store that counts, so "the write happened once and carried both secrets"
/// is assertable.
/// </summary>
internal sealed class RecordingSessionStore : ISteamSessionStore
{
    private SteamSession? _session;

    /// <summary>Settable so a test can seed a session and then count only the writes it is asking about.</summary>
    public int Saves { get; set; }

    public int Clears { get; set; }

    public bool CanPersist => true;

    public Task<SteamSession?> LoadAsync(CancellationToken ct = default) => Task.FromResult(_session);

    public Task SaveAsync(SteamSession session, CancellationToken ct = default)
    {
        Saves++;
        _session = session;
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        Clears++;
        _session = null;
        return Task.CompletedTask;
    }
}

/// <summary>
/// S6's renewal, at the level that decides when to spend a refresh token and
/// what to do with the answer.
/// </summary>
public sealed class SteamSessionRenewalTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_token_well_short_of_expiry_is_not_renewed()
    {
        // Proactive, not eager. The default lead is an hour against a measured
        // 24 h 22 m token, so twelve hours in there is nothing owed.
        var renewer = new FakeSteamSessionRenewer(Renewed(Now.AddHours(36)));
        var (provider, store, clock) = Build(renewer);

        await store.SaveAsync(SteamRenewalFixtures.RenewableSession(Now));
        clock.Advance(TimeSpan.FromHours(12));

        var session = (await provider.GetAsync())!;

        Assert.False(provider.IsRenewalDue(session));
        Assert.Equal(0, renewer.Calls);
        Assert.Equal(SteamSessionHealth.Live, await provider.GetHealthAsync());
    }

    [Fact]
    public async Task A_token_inside_its_renewal_lead_is_renewed_before_it_dies()
    {
        var replacement = SteamRenewalFixtures.AccessToken(Now.AddHours(48));
        var renewer = new FakeSteamSessionRenewer(SteamRenewalOutcome.Renewed(replacement, null));
        var (provider, store, clock) = Build(renewer);

        var original = SteamRenewalFixtures.RenewableSession(Now);
        await store.SaveAsync(original);

        // 23 h 30 m into a 24 h token: alive, and inside the one-hour lead. The
        // whole point of the lead is that the renewal happens while the old token
        // still works, so a failure costs nothing yet.
        clock.Advance(TimeSpan.FromMinutes((23 * 60) + 30));

        var session = (await provider.GetAsync())!;
        Assert.True(session.IsAccessUsable(clock.GetUtcNow(), SteamCredential.DefaultSkew));
        Assert.True(provider.IsRenewalDue(session));

        var renewed = await provider.RenewAsync(session);

        Assert.Equal(1, renewer.Calls);
        Assert.NotNull(renewed);
        Assert.Equal(replacement, renewed.AccessToken);
        Assert.Equal(Now.AddHours(48), renewed.ExpiresAt);
        Assert.Equal(clock.GetUtcNow(), renewed.LastRenewedAt);
        Assert.Equal(0, renewed.RenewalFailures);
        Assert.Equal(SteamSessionRenewalFailure.None, renewed.LastFailureKind);

        // And it landed on disk, not just in memory.
        Assert.Equal(replacement, (await store.LoadAsync())!.AccessToken);
        Assert.Equal(SteamSessionHealth.Live, await provider.GetHealthAsync());
    }

    [Fact]
    public async Task A_token_only_session_is_never_renewed_because_there_is_nothing_to_spend()
    {
        var renewer = new FakeSteamSessionRenewer(Renewed(Now.AddHours(48)));
        var (provider, store, clock) = Build(renewer);

        await store.SaveAsync(SteamSession.TryCreate(
            SteamRenewalFixtures.AccessToken(Now.AddHours(24)), refreshToken: null, Now)!);

        clock.Advance(TimeSpan.FromMinutes((23 * 60) + 30));

        var session = (await provider.GetAsync())!;

        Assert.False(session.HasRefreshToken);
        Assert.False(provider.IsRenewalDue(session));

        await provider.RenewAsync(session);
        Assert.Equal(0, renewer.Calls);

        // And it is never reported as owing a renewal nothing can pay: Live until
        // its token dies, then Expired.
        Assert.Equal(SteamSessionHealth.Live, await provider.GetHealthAsync());
    }

    [Fact]
    public async Task Two_concurrent_callers_spend_the_refresh_token_once()
    {
        // Rotation makes this a correctness requirement rather than politeness:
        // spending a refresh token can invalidate the previous one, so a double
        // spend is a self-inflicted sign-out.
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var replacement = SteamRenewalFixtures.AccessToken(Now.AddHours(48));
        var renewer = new FakeSteamSessionRenewer(SteamRenewalOutcome.Renewed(replacement, null))
        {
            Hold = hold,
        };

        var (provider, store, clock) = Build(renewer);

        var original = SteamRenewalFixtures.RenewableSession(Now);
        await store.SaveAsync(original);
        store.Saves = 0;
        clock.Advance(TimeSpan.FromMinutes((23 * 60) + 30));

        var stale = (await provider.GetAsync())!;

        var first = Task.Run(() => provider.RenewAsync(stale));

        // The first caller is demonstrably inside the renewer before the second
        // one starts, so this is a real race rather than a lucky ordering.
        await renewer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var second = Task.Run(() => provider.RenewAsync(stale));

        hold.SetResult();

        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, renewer.Calls);
        Assert.All(results, r => Assert.Equal(replacement, r!.AccessToken));

        // The second caller got the first caller's answer rather than an error or
        // a stale token: that is what makes the single-flight useful rather than
        // merely safe.
        Assert.Equal(1, store.Saves);
    }

    [Fact]
    public async Task A_caller_holding_an_already_replaced_session_is_handed_the_replacement()
    {
        var replacement = SteamRenewalFixtures.AccessToken(Now.AddHours(48));
        var renewer = new FakeSteamSessionRenewer(SteamRenewalOutcome.Renewed(replacement, null));
        var (provider, store, clock) = Build(renewer);

        await store.SaveAsync(SteamRenewalFixtures.RenewableSession(Now));
        clock.Advance(TimeSpan.FromMinutes((23 * 60) + 30));

        var stale = (await provider.GetAsync())!;

        Assert.Equal(replacement, (await provider.RenewAsync(stale))!.AccessToken);

        // The same stale session, offered again after somebody else replaced it.
        // No second exchange: the value it would spend no longer exists.
        var again = await provider.RenewAsync(stale);

        Assert.Equal(1, renewer.Calls);
        Assert.Equal(replacement, again!.AccessToken);
    }

    [Fact]
    public async Task A_rotated_refresh_token_replaces_the_stored_one_atomically()
    {
        var replacement = SteamRenewalFixtures.AccessToken(Now.AddHours(48));
        var rotated = SteamRenewalFixtures.BareRefreshToken(Now.AddDays(300));
        var renewer = new FakeSteamSessionRenewer(SteamRenewalOutcome.Renewed(replacement, rotated));
        var (provider, store, clock) = Build(renewer);

        var original = SteamRenewalFixtures.RenewableSession(Now);
        await store.SaveAsync(original);
        store.Saves = 0;
        clock.Advance(TimeSpan.FromMinutes((23 * 60) + 30));

        var renewed = await provider.RenewAsync(await provider.GetAsync());

        Assert.NotNull(renewed);
        Assert.Equal(rotated, renewed.RefreshToken);
        Assert.NotEqual(original.RefreshToken, renewed.RefreshToken);

        // The new refresh token's OWN expiry, read from the token rather than
        // carried over from the value it replaced.
        Assert.Equal(Now.AddDays(300), renewed.RefreshExpiresAt);

        // One write, carrying both secrets. The stored unit is the whole session,
        // so there is no window in which the new access token is on disk beside
        // the refresh token it superseded.
        Assert.Equal(1, store.Saves);

        var reloaded = (await store.LoadAsync())!;
        Assert.Equal(replacement, reloaded.AccessToken);
        Assert.Equal(rotated, reloaded.RefreshToken);
    }

    [Fact]
    public async Task A_renewal_that_rotates_nothing_keeps_the_refresh_token_it_had()
    {
        // Null means "Steam did not replace it", never "you now have none".
        var renewer = new FakeSteamSessionRenewer(Renewed(Now.AddHours(48)));
        var (provider, store, clock) = Build(renewer);

        var original = SteamRenewalFixtures.RenewableSession(Now);
        await store.SaveAsync(original);
        clock.Advance(TimeSpan.FromMinutes((23 * 60) + 30));

        var renewed = await provider.RenewAsync(await provider.GetAsync());

        Assert.Equal(original.RefreshToken, renewed!.RefreshToken);
        Assert.Equal(original.RefreshExpiresAt, renewed.RefreshExpiresAt);
    }

    [Fact]
    public async Task A_rejection_discards_the_refresh_token_latches_and_is_not_tried_again()
    {
        var renewer = new FakeSteamSessionRenewer(SteamRenewalOutcome.Rejected("finalizelogin returned 403"));
        var (provider, store, clock) = Build(renewer);

        var original = SteamRenewalFixtures.RenewableSession(Now);
        await store.SaveAsync(original);
        clock.Advance(TimeSpan.FromMinutes((23 * 60) + 30));

        var lapsed = await provider.RenewAsync(await provider.GetAsync());

        Assert.NotNull(lapsed);
        Assert.Equal(1, renewer.Calls);

        // The unspendable secret is gone from memory and from disk.
        Assert.Null(lapsed.RefreshToken);
        Assert.False(lapsed.HasRefreshToken);
        Assert.Null(lapsed.RefreshExpiresAt);
        Assert.Null((await store.LoadAsync())!.RefreshToken);

        // The record itself is kept, so the screen still says the sign-in ended
        // rather than that it never happened. RenewalFailing while the access
        // token is alive; Expired once it is not.
        Assert.Equal(SteamSessionRenewalFailure.Rejected, lapsed.LastFailureKind);
        Assert.Equal(SteamSessionHealth.RenewalFailing, await provider.GetHealthAsync());

        // Latched: no second attempt in this pass, or any later one, until a
        // fresh sign-in.
        Assert.False(provider.IsRenewalDue(lapsed));
        await provider.RenewAsync(lapsed);
        await provider.RenewAsync(null);
        Assert.Equal(1, renewer.Calls);

        clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(SteamSessionHealth.Expired, await provider.GetHealthAsync());
    }

    [Fact]
    public async Task A_503_keeps_the_session_and_counts_one_failure()
    {
        var renewer = new FakeSteamSessionRenewer(SteamRenewalOutcome.Transient("finalizelogin returned 503"));
        var (provider, store, clock) = Build(renewer);

        var original = SteamRenewalFixtures.RenewableSession(Now);
        await store.SaveAsync(original);
        clock.Advance(TimeSpan.FromMinutes((23 * 60) + 30));

        var kept = await provider.RenewAsync(await provider.GetAsync());

        Assert.NotNull(kept);
        Assert.Equal(original.AccessToken, kept.AccessToken);
        Assert.Equal(original.RefreshToken, kept.RefreshToken);
        Assert.Equal(1, kept.RenewalFailures);
        Assert.Equal(SteamSessionRenewalFailure.Transient, kept.LastFailureKind);

        // Nothing latched: the next tick tries again, which is the whole
        // difference from a rejection.
        Assert.True(provider.IsRenewalDue(kept));

        await provider.RenewAsync(kept);
        Assert.Equal(2, renewer.Calls);
    }

    [Fact]
    public async Task Repeated_failures_reach_renewal_failing_while_the_token_is_still_alive()
    {
        // Condition 8: the warning has to arrive before the credential dies, not
        // with it. Three failed attempts inside the lead window, and the Stores
        // screen is already saying so with half an hour of token left.
        var renewer = new FakeSteamSessionRenewer(SteamRenewalOutcome.Transient("unreachable"));
        var (provider, store, clock) = Build(renewer);

        await store.SaveAsync(SteamRenewalFixtures.RenewableSession(Now));
        clock.Advance(TimeSpan.FromMinutes((23 * 60) + 30));

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await provider.RenewAsync(await provider.GetAsync());
            clock.Advance(TimeSpan.FromMinutes(5));
        }

        var session = (await provider.GetAsync())!;

        Assert.Equal(3, renewer.Calls);
        Assert.Equal(3, session.RenewalFailures);

        // Still usable. The user is being warned about a credential that has not
        // failed them yet.
        Assert.True(session.IsAccessUsable(clock.GetUtcNow(), SteamCredential.DefaultSkew));
        Assert.Equal(SteamSessionHealth.RenewalFailing, await provider.GetHealthAsync());

        // And a success clears the count rather than leaving a permanent warning.
        var recovering = Build(new FakeSteamSessionRenewer(Renewed(Now.AddHours(48))));
        await recovering.Store.SaveAsync(session);
        var renewed = await recovering.Provider.RenewAsync(await recovering.Provider.GetAsync());
        Assert.Equal(0, renewed!.RenewalFailures);
    }

    [Fact]
    public async Task An_audience_change_is_adopted_and_stored_rather_than_lapsing()
    {
        // This was a hard lapse until the full-feature review priced it. The
        // sign-in token is aud ["web:store"] and NOBODY has observed what the
        // pointssummary renewal route mints; if it differs at all, lapsing would
        // permanently sign out every signed-in user on their FIRST renewal, on a
        // guess, and discard a refresh token that cannot be recovered.
        var replacement = SteamRenewalFixtures.AccessToken(Now.AddHours(48), audience: "web:community");
        var renewer = new FakeSteamSessionRenewer(SteamRenewalOutcome.Renewed(replacement, null));

        var (provider, store, clock) = Build(renewer);

        var original = SteamRenewalFixtures.RenewableSession(Now);
        Assert.Equal(new[] { "web:store" }, original.Audience);

        await store.SaveAsync(original);
        clock.Advance(TimeSpan.FromMinutes((23 * 60) + 30));

        var renewed = await provider.RenewAsync(await provider.GetAsync());

        Assert.NotNull(renewed);
        Assert.Equal(replacement, renewed.AccessToken);
        Assert.Equal(0, renewed.RenewalFailures);
        Assert.Equal(SteamSessionRenewalFailure.None, renewed.LastFailureKind);

        // The refresh token survives, which is the whole point: an audience the
        // Web API refuses still fails honestly, as a 401, and a 401 is
        // recoverable. A discarded refresh token is not.
        Assert.Equal(original.RefreshToken, renewed.RefreshToken);
        Assert.Equal(SteamSessionHealth.Live, await provider.GetHealthAsync());

        // The NEW audience is stored, so a subsequent change is visible rather
        // than warned about forever against a value nothing uses any more.
        Assert.Equal(new[] { "web:community" }, renewed.Audience);
        Assert.Equal(new[] { "web:community" }, (await store.LoadAsync())!.Audience);
    }

    [Fact]
    public async Task An_adopted_audience_is_adopted_once_and_does_not_churn()
    {
        // The anti-loop intent the old refusal was written for, kept without the
        // lapse: the adoption is unconditional rather than retried, so the second
        // renewal at the same audience is an ordinary quiet success.
        var renewer = new FakeSteamSessionRenewer((_, index) => SteamRenewalOutcome.Renewed(
            SteamRenewalFixtures.AccessToken(Now.AddHours(48 + index), audience: "web:community"), null));

        var (provider, store, clock) = Build(renewer);

        await store.SaveAsync(SteamRenewalFixtures.RenewableSession(Now));
        clock.Advance(TimeSpan.FromMinutes((23 * 60) + 30));

        await provider.RenewAsync(await provider.GetAsync());

        clock.Advance(TimeSpan.FromHours(48));
        var again = await provider.RenewAsync(await provider.GetAsync());

        Assert.Equal(2, renewer.Calls);
        Assert.Equal(new[] { "web:community" }, again!.Audience);
        Assert.Equal(0, again.RenewalFailures);
    }

    [Fact]
    public async Task A_renewed_token_that_states_no_audience_leaves_the_stored_one_alone()
    {
        // An absent claim is a fact nobody has, not a change, and must not blank
        // the value the next comparison is made against.
        var renewer = new FakeSteamSessionRenewer(SteamRenewalOutcome.Renewed(
            SteamSessionFixtures.Jwt($$"""
                {"iss":"steam","sub":"{{SteamSessionFixtures.Subject}}","exp":{{Now.AddHours(48).ToUnixTimeSeconds()}}}
                """),
            null));

        var (provider, store, clock) = Build(renewer);

        await store.SaveAsync(SteamRenewalFixtures.RenewableSession(Now));
        clock.Advance(TimeSpan.FromMinutes((23 * 60) + 30));

        var renewed = await provider.RenewAsync(await provider.GetAsync());

        Assert.Equal(new[] { "web:store" }, renewed!.Audience);
        Assert.Equal(0, renewed.RenewalFailures);
    }

    [Fact]
    public async Task A_renewal_naming_a_different_account_lapses()
    {
        var renewer = new FakeSteamSessionRenewer(SteamRenewalOutcome.Renewed(
            SteamRenewalFixtures.AccessToken(Now.AddHours(48), subject: "76561198000009999"), null));

        var (provider, store, clock) = Build(renewer);

        var original = SteamRenewalFixtures.RenewableSession(Now);
        await store.SaveAsync(original);
        clock.Advance(TimeSpan.FromMinutes((23 * 60) + 30));

        var lapsed = await provider.RenewAsync(await provider.GetAsync());

        // Adopting it would silently re-point the whole library at somebody
        // else's account.
        Assert.Equal(original.SteamId, lapsed!.SteamId);
        Assert.Equal(original.AccessToken, lapsed.AccessToken);
        Assert.Equal(SteamSessionRenewalFailure.Rejected, lapsed.LastFailureKind);
    }

    [Fact]
    public async Task A_renewed_token_that_does_not_decode_is_transient_rather_than_a_sign_out()
    {
        var renewer = new FakeSteamSessionRenewer(SteamRenewalOutcome.Renewed("not-a-jwt", null));
        var (provider, store, clock) = Build(renewer);

        var original = SteamRenewalFixtures.RenewableSession(Now);
        await store.SaveAsync(original);
        clock.Advance(TimeSpan.FromMinutes((23 * 60) + 30));

        var kept = await provider.RenewAsync(await provider.GetAsync());

        Assert.Equal(original.RefreshToken, kept!.RefreshToken);
        Assert.Equal(SteamSessionRenewalFailure.Transient, kept.LastFailureKind);
    }

    [Fact]
    public async Task A_refresh_token_past_its_own_expiry_is_never_spent()
    {
        // The refresh token dies before the access token does — the arrangement a
        // reactive 401 can walk into, where the session still has a token to send
        // and nothing left to replace it with.
        var renewer = new FakeSteamSessionRenewer(Renewed(Now.AddHours(48)));
        var (provider, store, clock) = Build(renewer);

        await store.SaveAsync(SteamRenewalFixtures.RenewableSession(
            Now, accessLife: TimeSpan.FromHours(24), refreshLife: TimeSpan.FromHours(1)));

        clock.Advance(TimeSpan.FromHours(2));

        var session = (await provider.GetAsync())!;
        Assert.True(session.IsAccessUsable(clock.GetUtcNow(), SteamCredential.DefaultSkew));
        Assert.False(provider.IsRenewalDue(session));

        var lapsed = await provider.RenewAsync(session);

        // Steam told us when this would lapse and it has. No request is sent —
        // there is nothing to ask — and the kind is recorded as its own thing,
        // because this failure was predictable and a rejection is not.
        Assert.Equal(0, renewer.Calls);
        Assert.Equal(SteamSessionRenewalFailure.Expired, lapsed!.LastFailureKind);
        Assert.Null(lapsed.RefreshToken);
        Assert.Equal(SteamSessionHealth.RenewalFailing, await provider.GetHealthAsync());
    }

    [Fact]
    public async Task A_sign_in_that_lands_mid_exchange_is_not_written_over()
    {
        // The cost of not holding one lock across the exchange, paid explicitly.
        // A sign-in can now land while a renewal is in flight; its session is
        // newer than anything the renewal knows about, so the renewal's outcome
        // is dropped rather than written over it.
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renewer = new FakeSteamSessionRenewer(SteamRenewalOutcome.Renewed(
            SteamRenewalFixtures.AccessToken(Now.AddHours(48)), null))
        {
            Hold = hold,
        };

        var (provider, store, clock) = Build(renewer);

        await store.SaveAsync(SteamRenewalFixtures.RenewableSession(Now));
        clock.Advance(TimeSpan.FromMinutes((23 * 60) + 30));

        var stale = await provider.GetAsync();
        var renewal = Task.Run(() => provider.RenewAsync(stale));
        await renewer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // The sign-in completes while the exchange is still open, which is only
        // possible because the exchange does not hold the state lock.
        var freshSignIn = SteamRenewalFixtures.RenewableSession(clock.GetUtcNow());
        await provider.SaveAsync(freshSignIn).WaitAsync(TimeSpan.FromSeconds(10));

        hold.SetResult();
        var result = await renewal;

        Assert.Equal(freshSignIn.AccessToken, result!.AccessToken);
        Assert.Equal(freshSignIn.AccessToken, (await store.LoadAsync())!.AccessToken);
    }

    [Fact]
    public async Task A_rejection_that_arrives_after_a_fresh_sign_in_does_not_latch_it_off()
    {
        // The same race with the worst possible outcome: a rejection landing on
        // a credential the user has just re-earned. Latching there would switch
        // off a working sign-in seconds after they made it.
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renewer = new FakeSteamSessionRenewer(SteamRenewalOutcome.Rejected("finalizelogin returned 403"))
        {
            Hold = hold,
        };

        var (provider, store, clock) = Build(renewer);

        await store.SaveAsync(SteamRenewalFixtures.RenewableSession(Now));
        clock.Advance(TimeSpan.FromMinutes((23 * 60) + 30));

        var stale = await provider.GetAsync();
        var renewal = Task.Run(() => provider.RenewAsync(stale));
        await renewer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var freshSignIn = SteamRenewalFixtures.RenewableSession(clock.GetUtcNow());
        await provider.SaveAsync(freshSignIn).WaitAsync(TimeSpan.FromSeconds(10));

        hold.SetResult();
        var result = await renewal;

        Assert.Equal(freshSignIn.AccessToken, result!.AccessToken);
        Assert.Equal(freshSignIn.RefreshToken, result.RefreshToken);
        Assert.Equal(SteamSessionHealth.Live, await provider.GetHealthAsync());

        // And the latch did not close, so the new session can still renew.
        Assert.True(provider.IsRenewalDue(
            SteamRenewalFixtures.RenewableSession(clock.GetUtcNow().AddHours(-23.5))));
    }

    [Fact]
    public async Task The_stored_key_set_is_unchanged_by_a_renewal()
    {
        // Condition 2's audit is a comparison against a closed list of eleven
        // names, and it must not depend on whether a renewal has happened, or on
        // whether that renewal rotated the refresh token.
        var settings = new InMemorySettingsRepository();
        var protector = new SteamSessionFixtures.ReversibleProtector();
        var store = new SettingsSteamSessionStore(settings, protector);
        var clock = new FakeTimeProvider(Now);

        var renewer = new FakeSteamSessionRenewer(SteamRenewalOutcome.Renewed(
            SteamRenewalFixtures.AccessToken(Now.AddHours(48)),
            SteamRenewalFixtures.BareRefreshToken(Now.AddDays(300))));

        var provider = new SteamSessionProvider(store, new SteamWebOptions(), clock, null, renewer);

        await provider.SaveAsync(SteamRenewalFixtures.RenewableSession(Now));
        var before = StoredFields(settings, protector);

        clock.Advance(TimeSpan.FromMinutes((23 * 60) + 30));
        await provider.RenewAsync(await provider.GetAsync());

        var after = StoredFields(settings, protector);

        Assert.Equal(PermittedFields.Order(StringComparer.Ordinal), before.Order(StringComparer.Ordinal));
        Assert.Equal(PermittedFields.Order(StringComparer.Ordinal), after.Order(StringComparer.Ordinal));

        // And the same after a hard lapse, where refresh_token goes to null but
        // the FIELD is still emitted.
        var lapsing = new SteamSessionProvider(
            store,
            new SteamWebOptions(),
            clock,
            null,
            new FakeSteamSessionRenewer(SteamRenewalOutcome.Rejected("finalizelogin returned 403")));

        await lapsing.RenewAsync(await lapsing.GetAsync());

        Assert.Equal(
            PermittedFields.Order(StringComparer.Ordinal),
            StoredFields(settings, protector).Order(StringComparer.Ordinal));

        var json = protector.Unprotect(
            (await settings.GetAsync(SettingsSteamSessionStore.SessionSetting))!)!;

        Assert.Contains("\"refresh_token\":null", json, StringComparison.Ordinal);

        // Nothing from the exchange itself reached disk.
        foreach (var forbidden in new[] { "steamLoginSecure", "steamRefresh_steam", "sessionid", "nonce" })
        {
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Nothing_about_a_renewal_reaches_even_a_trace_level_log()
    {
        var replacement = SteamRenewalFixtures.AccessToken(Now.AddHours(48));
        var rotated = SteamRenewalFixtures.BareRefreshToken(Now.AddDays(300));

        using var host = new SteamWebTestHost(
            SteamWebTestHost.DefaultResponder(),
            apiKey: null,
            now: Now,
            renewalResponder: SteamRenewalFixtures.HappyPath(replacement, rotated));

        var provider = host.Resolve<ISteamSessionProvider>();
        var original = SteamRenewalFixtures.RenewableSession(Now);
        await provider.SaveAsync(original);

        host.Clock.Advance(TimeSpan.FromMinutes((23 * 60) + 30));
        await provider.RenewAsync(await provider.GetAsync());

        var log = host.Logs.AllText;

        foreach (var secret in new[]
                 {
                     original.AccessToken, original.RefreshToken!, replacement, rotated,
                     "transferredCookieValue", "steamLoginSecure",
                 })
        {
            Assert.DoesNotContain(secret, log, StringComparison.Ordinal);
        }

        // Nor any JWT segment of either token, which is what a partial leak would
        // look like.
        foreach (var segment in original.RefreshToken!.Split('.'))
        {
            Assert.DoesNotContain(segment, log, StringComparison.Ordinal);
        }

        // The account is a real person and is absent too.
        Assert.DoesNotContain(SteamSessionFixtures.Subject, log, StringComparison.Ordinal);

        // But the fact that a renewal happened is there, because a support
        // question about a failing session has to be answerable.
        Assert.Contains("Renewed the Steam session", log, StringComparison.Ordinal);
    }

    private static readonly string[] PermittedFields =
    [
        "access_token",
        "expires_at",
        "audience",
        "issuer",
        "steamid64",
        "refresh_token",
        "refresh_expires_at",
        "minted_at",
        "last_renewed_at",
        "renewal_failures",
        "last_failure_kind",
    ];

    private static SteamRenewalOutcome Renewed(DateTimeOffset expiresAt)
        => SteamRenewalOutcome.Renewed(SteamRenewalFixtures.AccessToken(expiresAt), null);

    private static string[] StoredFields(
        InMemorySettingsRepository settings, SteamSessionFixtures.ReversibleProtector protector)
    {
        var stored = settings.GetAsync(SettingsSteamSessionStore.SessionSetting)
            .GetAwaiter().GetResult()!;

        using var document = JsonDocument.Parse(protector.Unprotect(stored)!);
        return document.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
    }

    private static (SteamSessionProvider Provider, RecordingSessionStore Store, FakeTimeProvider Clock) Build(
        FakeSteamSessionRenewer renewer)
    {
        var store = new RecordingSessionStore();
        var clock = new FakeTimeProvider(Now);
        return (new SteamSessionProvider(store, new SteamWebOptions(), clock, null, renewer), store, clock);
    }
}

/// <summary>
/// Where renewal meets the rest of Winnow: the reactive 401, and the scheduler
/// rule that keeps a renewal off the unattended path entirely.
/// </summary>
public sealed class SteamSessionRenewalSeamTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_401_renews_once_and_sends_the_request_again_with_the_new_token()
    {
        var replacement = SteamRenewalFixtures.AccessToken(Now.AddHours(48));

        using var host = new SteamWebTestHost(
            (request, prior) => request.Endpoint == SteamWebTestHost.GetOwnedGames && prior == 0
                ? FakeSteamWebHandler.Json(HttpStatusCode.Unauthorized, "{}")
                : FakeSteamWebHandler.Json(HttpStatusCode.OK, SteamWebFixtures.CapturedResponse()),
            apiKey: null,
            now: Now,
            renewalResponder: SteamRenewalFixtures.HappyPath(replacement));

        var original = SteamRenewalFixtures.RenewableSession(Now);
        await host.Resolve<ISteamSessionProvider>().SaveAsync(original);

        var library = await host.Client.GetOwnedGamesAsync(SteamId.FromSteamId64(76561198000000001UL)!.Value);

        Assert.True(library.Succeeded);

        var requests = host.Handler.Requests;
        Assert.Equal(2, requests.Count);

        // The first carried the token Steam refused; the second carries its
        // replacement. The credential lives in the query string, so a retry is a
        // different request rather than a different header.
        Assert.Equal(original.AccessToken, requests[0].Parameter(SteamCredential.SessionTokenParameter));
        Assert.Equal(replacement, requests[1].Parameter(SteamCredential.SessionTokenParameter));

        // Exactly one renewal exchange: three requests, not six.
        Assert.Equal(3, host.RenewalHandler.Requests.Count);
    }

    [Fact]
    public async Task A_second_401_ends_the_pass_rather_than_renewing_again()
    {
        using var host = new SteamWebTestHost(
            (_, _) => FakeSteamWebHandler.Json(HttpStatusCode.Unauthorized, "{}"),
            apiKey: null,
            now: Now,
            renewalResponder: SteamRenewalFixtures.HappyPath(
                SteamRenewalFixtures.AccessToken(Now.AddHours(48))));

        await host.Resolve<ISteamSessionProvider>().SaveAsync(SteamRenewalFixtures.RenewableSession(Now));

        var library = await host.Client.GetOwnedGamesAsync(SteamId.FromSteamId64(76561198000000001UL)!.Value);

        Assert.False(library.Succeeded);

        // Two API requests and one renewal. One reactive renewal per pass, then
        // give up: the alternative is a loop that spends a refresh token per
        // request.
        Assert.Equal(2, host.Handler.Requests.Count);
        Assert.Equal(3, host.RenewalHandler.Requests.Count);
    }

    [Fact]
    public async Task A_401_against_an_api_key_is_not_retried_and_the_retry_policy_never_saw_it()
    {
        // Two claims in one: a rejected key has no renewal path, and 401 is
        // absent from SteamWebResilienceHandler's transient list — so exactly one
        // request reaches the transport.
        using var host = new SteamWebTestHost(
            (_, _) => FakeSteamWebHandler.Json(HttpStatusCode.Unauthorized, "{}"),
            now: Now);

        var library = await host.Client.GetOwnedGamesAsync(SteamId.FromSteamId64(76561198000000001UL)!.Value);

        Assert.False(library.Succeeded);
        Assert.Single(host.Handler.Requests);
        Assert.Empty(host.RenewalHandler.Requests);
    }

    [Fact]
    public async Task An_unattended_pass_with_a_key_takes_the_key_without_waiting_on_a_renewal()
    {
        // Decision note 2, made structural. The renewal below never completes,
        // and the tick does not care: with a key in force the session is not
        // consulted at all on an unattended pass, so a renewal that is in flight,
        // slow or failing cannot delay one.
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renewer = new FakeSteamSessionRenewer(SteamRenewalOutcome.Transient("unreachable"))
        {
            Hold = hold,
        };

        using var host = new SteamWebTestHost(
            SteamWebTestHost.DefaultResponder(), now: Now, renewer: renewer);

        var provider = host.Resolve<ISteamSessionProvider>();
        await provider.SaveAsync(SteamRenewalFixtures.RenewableSession(Now));

        // Deep inside the lead window, and already carrying failures: the state
        // where a naive implementation would block a scheduler tick.
        host.Clock.Advance(TimeSpan.FromMinutes((23 * 60) + 45));

        var credentials = host.Resolve<ISteamCredentialProvider>();

        var unattended = await credentials.GetAsync(SteamCredentialPurpose.Unattended)
            .AsTask().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.NotNull(unattended);
        Assert.Equal(SteamCredentialKind.ApiKey, unattended.Kind);
        Assert.Equal(0, renewer.Calls);

        // The inventory the Stores screen reads is the same: a plain read, never
        // a renewal, because §5.1 forbids enrichment blocking a user-facing path.
        var inventory = await credentials.GetInventoryAsync()
            .AsTask().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(inventory.HasApiKey);
        Assert.True(inventory.HasSession);
        Assert.Equal(0, renewer.Calls);

        // And a user-initiated call is where the renewal is actually paid for.
        var userInitiated = credentials.GetAsync(SteamCredentialPurpose.UserInitiated).AsTask();
        await renewer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        hold.SetResult();
        await userInitiated;

        Assert.Equal(1, renewer.Calls);
    }

    [Fact]
    public async Task The_stores_screen_reads_the_inventory_WHILE_a_renewal_is_held_open()
    {
        // The gate split, as a behaviour rather than as a comment. Before it,
        // one lock served both the in-memory state and the renewal exchange, so
        // a reader that missed the unlocked fast path — which needs a USABLE
        // access token, and this one's has expired — waited behind three HTTP
        // requests and their Polly backoff. Reachable: keyless user, expired
        // token, unattended pass renewing, user opens the Stores screen. §5.1
        // forbids that, and "does not renew" was never the same claim as "does
        // not wait".
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renewer = new FakeSteamSessionRenewer(SteamRenewalOutcome.Renewed(
            SteamRenewalFixtures.AccessToken(Now.AddHours(48)), null))
        {
            Hold = hold,
        };

        using var host = new SteamWebTestHost(
            SteamWebTestHost.DefaultResponder(), apiKey: null, now: Now, renewer: renewer);

        var provider = host.Resolve<ISteamSessionProvider>();
        await provider.SaveAsync(SteamRenewalFixtures.RenewableSession(Now));

        // Past the access token's own expiry, so every read below takes the
        // state lock rather than the fast path.
        host.Clock.Advance(TimeSpan.FromHours(25));

        var credentials = host.Resolve<ISteamCredentialProvider>();

        // Start the renewal and wait until it is provably inside the exchange
        // and holding the renewal lock. It never completes.
        var renewal = Task.Run(() => provider.RenewAsync(null));
        await renewer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Every user-facing read answers while that is still in flight.
        var inventory = await credentials.GetInventoryAsync()
            .AsTask().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(inventory.HasSession);
        Assert.False(inventory.SessionUsable);

        var health = await provider.GetHealthAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(SteamSessionHealth.RenewalDue, health);

        Assert.NotNull(await provider.GetAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)));

        // Only now let it finish, so the assertions above cannot have been
        // satisfied by a renewal that had already completed.
        hold.SetResult();
        Assert.NotNull(await renewal);
        Assert.Equal(1, renewer.Calls);
    }

    [Fact]
    public async Task A_keyless_unattended_pass_renews_because_the_session_is_all_there_is()
    {
        var replacement = SteamRenewalFixtures.AccessToken(Now.AddHours(48));
        var renewer = new FakeSteamSessionRenewer(SteamRenewalOutcome.Renewed(replacement, null));

        using var host = new SteamWebTestHost(
            SteamWebTestHost.DefaultResponder(), apiKey: null, now: Now, renewer: renewer);

        await host.Resolve<ISteamSessionProvider>().SaveAsync(SteamRenewalFixtures.RenewableSession(Now));
        host.Clock.Advance(TimeSpan.FromMinutes((23 * 60) + 45));

        var chosen = await host.Resolve<ISteamCredentialProvider>()
            .GetAsync(SteamCredentialPurpose.Unattended);

        Assert.NotNull(chosen);
        Assert.Equal(SteamCredentialKind.SessionToken, chosen.Kind);
        Assert.Equal(replacement, chosen.Value);
        Assert.Equal(1, renewer.Calls);
    }
}
