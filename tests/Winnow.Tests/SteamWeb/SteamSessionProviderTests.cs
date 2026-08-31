using Winnow.Enrich.SteamWeb;
using Winnow.Enrich.SteamWeb.Credentials;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Winnow.Tests.SteamWeb;

/// <summary>
/// Reading a token's claims. Nothing here validates a signature, and the tests
/// say so: a token with a nonsense signature must still be read, because Steam
/// is the only party that can decide whether one is good and it does so on every
/// request. What the reader must never do is throw, because every malformed
/// input on this path means "no session" and a crash would take the whole
/// enrichment pass with it.
/// </summary>
public sealed class SteamTokenClaimsTests
{
    [Fact]
    public void A_token_payload_yields_its_expiry_subject_audience_and_issuer()
    {
        var expiry = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

        var claims = SteamTokenClaims.Read(SteamSessionFixtures.AccessToken(expiry));

        Assert.True(claims.Readable);
        Assert.Equal(expiry, claims.ExpiresAt);
        Assert.Equal(SteamSessionFixtures.Subject, claims.Subject);
        Assert.Equal(new[] { "web:store" }, claims.Audiences);
        Assert.Equal("steam", claims.Issuer);
        Assert.Equal(SteamSessionFixtures.Subject, claims.SteamId!.Value.ToString());
    }

    [Fact]
    public void A_single_string_audience_reads_as_one_entry()
    {
        var claims = SteamTokenClaims.Read(SteamSessionFixtures.Jwt("""{"aud":"web"}"""));

        Assert.True(claims.Readable);
        Assert.Equal(new[] { "web" }, claims.Audiences);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-jwt")]
    [InlineData("only.two")]
    [InlineData("a.!!!not-base64!!!.c")]
    [InlineData("a..c")]
    public void A_malformed_token_is_unreadable_rather_than_an_exception(string? token)
    {
        var claims = SteamTokenClaims.Read(token);

        Assert.False(claims.Readable);
        Assert.Null(claims.ExpiresAt);
        Assert.Null(claims.Subject);
        Assert.Null(claims.SteamId);
        Assert.Empty(claims.Audiences);
    }

    [Fact]
    public void A_payload_that_is_valid_base64_but_not_a_json_object_is_unreadable()
    {
        Assert.False(SteamTokenClaims.Read(SteamSessionFixtures.Jwt("[1,2,3]")).Readable);
        Assert.False(SteamTokenClaims.Read(SteamSessionFixtures.Jwt("}{")).Readable);
    }

    [Fact]
    public void Claims_of_the_wrong_json_type_are_absent_rather_than_coerced()
    {
        // A string exp is not an expiry. Coercing it would produce a confident
        // wrong date, which is worse than no date at all.
        var claims = SteamTokenClaims.Read(
            SteamSessionFixtures.Jwt("""{"exp":"soon","sub":12345,"aud":7,"iss":null}"""));

        Assert.True(claims.Readable);
        Assert.Null(claims.ExpiresAt);
        Assert.Null(claims.Subject);
        Assert.Null(claims.Issuer);
        Assert.Empty(claims.Audiences);
    }

    [Fact]
    public void A_subject_outside_the_individual_account_range_yields_no_steam_id()
    {
        var claims = SteamTokenClaims.Read(SteamSessionFixtures.Jwt("""{"sub":"not-a-number"}"""));

        Assert.True(claims.Readable);
        Assert.Null(claims.SteamId);
    }

    [Fact]
    public void The_probe_and_the_product_read_the_same_claims()
    {
        // The probe's reader was promoted rather than copied, so there is one
        // base64url decoder and no way for the diagnostic and the real reader to
        // disagree about a token. S3 deletes the probe; until then this pins it.
        var token = SteamSessionFixtures.AccessToken(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));

        var probe = Winnow.App.Services.SteamSignInProbeFacts.ReadClaims(token);
        var product = SteamTokenClaims.Read(token);

        Assert.Equal(product.Readable, probe.Readable);
        Assert.Equal(product.ExpiresAt, probe.ExpiresAt);
        Assert.Equal(product.Subject, probe.Subject);
        Assert.Equal(product.Issuer, probe.Issuer);
        Assert.Equal(product.Audiences, probe.Audiences);
    }
}

/// <summary>
/// Building a session out of what a sign-in actually hands over: two opaque
/// strings and the moment they arrived. Every other field is read out of the
/// tokens, so no caller can assert an expiry or an account the token does not
/// claim.
/// </summary>
public sealed class SteamSessionCreationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Expiry_and_account_come_from_the_token_rather_than_from_the_caller()
    {
        var expiry = Now.AddHours(24).AddMinutes(22);

        var session = SteamSession.TryCreate(
            SteamSessionFixtures.AccessToken(expiry),
            SteamSessionFixtures.RefreshToken(Now.AddDays(207)),
            Now);

        Assert.NotNull(session);
        Assert.Equal(expiry, session.ExpiresAt);
        Assert.Equal(SteamSessionFixtures.Subject, session.SteamId.ToString());
        Assert.Equal(Now, session.MintedAt);
        Assert.Equal(Now.AddDays(207), session.RefreshExpiresAt);
        Assert.Equal(new[] { "web:store" }, session.Audience);
        Assert.Equal("steam", session.Issuer);
    }

    [Fact]
    public void A_refresh_token_that_does_not_decode_leaves_its_expiry_unknown()
    {
        // Never a guess. Writing the measured 207 days here would turn "not
        // known" into a date, and a wrong date either retires a working session
        // early or keeps a dead one on the books.
        var session = SteamSession.TryCreate(
            SteamSessionFixtures.AccessToken(Now.AddHours(24)), "an-opaque-value-that-is-not-a-jwt", Now);

        Assert.NotNull(session);
        Assert.Null(session.RefreshExpiresAt);

        // Unknown counts as usable: Steam is the authority on a refresh token,
        // and refusing to try one for lack of a claim throws away a working
        // session.
        Assert.True(session.IsRefreshUsable(Now.AddYears(5), TimeSpan.Zero));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    public void A_malformed_access_token_yields_no_session(string? accessToken)
        => Assert.Null(SteamSession.TryCreate(accessToken, SteamSessionFixtures.RefreshToken(Now), Now));

    [Fact]
    public void A_token_stating_no_expiry_or_no_account_yields_no_session()
    {
        Assert.Null(SteamSession.TryCreate(
            SteamSessionFixtures.Jwt($$"""{"sub":"{{SteamSessionFixtures.Subject}}"}"""),
            SteamSessionFixtures.RefreshToken(Now),
            Now));

        Assert.Null(SteamSession.TryCreate(
            SteamSessionFixtures.Jwt("""{"exp":1900000000}"""),
            SteamSessionFixtures.RefreshToken(Now),
            Now));
    }

    [Fact]
    public void A_missing_refresh_token_yields_no_session()
        => Assert.Null(SteamSession.TryCreate(
            SteamSessionFixtures.AccessToken(Now.AddHours(24)), null, Now));
}

/// <summary>
/// The provider: one read of the store, expiry arithmetic against a clock the
/// test moves by hand, and the health value the Stores screen will render.
///
/// <para>Nothing here renews, because nothing in S2 does. The states these
/// tests pin are the ones a user can actually reach today, and the reason they
/// are pinned now is section 4.7's eighth binding condition: a session that
/// cannot renew has to say so before it dies, which means the difference between
/// "never connected" and "connected and lapsed" has to survive all the way to
/// the UI.</para>
/// </summary>
public sealed class SteamSessionProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task No_stored_session_reads_as_not_signed_in()
    {
        var provider = Build(out _, out _);

        Assert.Null(await provider.GetAsync());
        Assert.Equal(SteamSessionHealth.NotSignedIn, await provider.GetHealthAsync());
    }

    [Fact]
    public async Task A_live_session_reads_as_live_and_the_store_is_read_once()
    {
        var provider = Build(out var store, out _);
        await store.SaveAsync(SteamSessionFixtures.Session(Now));

        Assert.NotNull(await provider.GetAsync());
        Assert.Equal(SteamSessionHealth.Live, await provider.GetHealthAsync());

        // Repeated reads take the unlocked fast path and never touch the store
        // again. A provider that re-read per call would hit the settings table
        // on every enrichment request.
        for (var i = 0; i < 5; i++)
        {
            Assert.NotNull(await provider.GetAsync());
        }

        Assert.Equal(1, store.Loads);
    }

    [Fact]
    public async Task A_session_inside_the_renewal_lead_window_reads_as_renewal_due()
    {
        var provider = Build(out var store, out var clock);
        await store.SaveAsync(SteamSessionFixtures.Session(Now));

        // 23 h 30 m into a 24 h token: alive, but inside the one-hour lead.
        clock.Advance(TimeSpan.FromMinutes(23 * 60 + 30));

        var session = await provider.GetAsync();
        Assert.NotNull(session);
        Assert.True(session.IsAccessUsable(clock.GetUtcNow(), SteamCredential.DefaultSkew));
        Assert.Equal(SteamSessionHealth.RenewalDue, await provider.GetHealthAsync());
    }

    [Fact]
    public async Task An_expired_access_token_with_a_live_refresh_token_reads_as_renewal_due()
    {
        var provider = Build(out var store, out var clock);
        await store.SaveAsync(SteamSessionFixtures.Session(Now));

        clock.Advance(TimeSpan.FromHours(25));

        // Still returned, not null: the UI has to be able to say what happened,
        // and the selector already refuses to send a dead token.
        var session = await provider.GetAsync();
        Assert.NotNull(session);
        Assert.False(session.IsAccessUsable(clock.GetUtcNow(), SteamCredential.DefaultSkew));
        Assert.Equal(SteamSessionHealth.RenewalDue, await provider.GetHealthAsync());
    }

    [Fact]
    public async Task An_expired_access_token_with_a_dead_refresh_token_reads_as_expired()
    {
        var provider = Build(out var store, out var clock);
        await store.SaveAsync(SteamSessionFixtures.Session(
            Now, accessLife: TimeSpan.FromHours(24), refreshLife: TimeSpan.FromDays(30)));

        clock.Advance(TimeSpan.FromDays(31));

        Assert.NotNull(await provider.GetAsync());
        Assert.Equal(SteamSessionHealth.Expired, await provider.GetHealthAsync());

        // And it stays Expired rather than becoming NotSignedIn: the lapse is
        // latched in memory, and the store is deliberately NOT cleared, because
        // "you were signed in and the session died" is the sentence the user
        // needs and "you never connected" is not.
        Assert.Equal(SteamSessionHealth.Expired, await provider.GetHealthAsync());
        Assert.NotNull(await store.LoadAsync());
    }

    [Fact]
    public void A_recorded_renewal_failure_reads_as_renewal_failing()
    {
        // S2 never produces this state, since nothing renews yet, but the mapping is
        // pinned now so S6 lands against a classifier that already agrees with
        // the UI it will drive.
        var session = SteamSessionFixtures.Session(Now)
            .WithRenewalFailure(SteamSessionRenewalFailure.Transient);

        Assert.Equal(
            SteamSessionHealth.RenewalFailing,
            SteamSessionProvider.Classify(session, Now.AddHours(1)));

        // A successful renewal clears both the count and the kind.
        var renewed = session.WithRenewedAccess("new-token", Now.AddHours(25), Now.AddHours(1));
        Assert.Equal(0, renewed.RenewalFailures);
        Assert.Equal(SteamSessionRenewalFailure.None, renewed.LastFailureKind);
        Assert.Equal(SteamSessionHealth.Live, SteamSessionProvider.Classify(renewed, Now.AddHours(1)));
    }

    [Fact]
    public async Task A_live_session_on_a_host_that_cannot_encrypt_reads_as_not_persisted()
    {
        var clock = new FakeTimeProvider(Now);
        var store = new InMemorySteamSessionStore();
        var provider = new SteamSessionProvider(store, new SteamWebOptions(), clock);

        await provider.SaveAsync(SteamSessionFixtures.Session(Now));

        Assert.False(store.CanPersist);
        Assert.Equal(SteamSessionHealth.NotPersisted, await provider.GetHealthAsync());
    }

    [Fact]
    public async Task Signing_out_forgets_the_session_in_memory_and_on_disk()
    {
        var provider = Build(out var store, out _);
        await provider.SaveAsync(SteamSessionFixtures.Session(Now));

        Assert.NotNull(await provider.GetAsync());

        await provider.SignOutAsync();

        Assert.Null(await provider.GetAsync());
        Assert.Null(await store.LoadAsync());
        Assert.Equal(SteamSessionHealth.NotSignedIn, await provider.GetHealthAsync());
    }

    [Fact]
    public async Task Concurrent_first_reads_load_the_store_exactly_once()
    {
        var provider = Build(out var store, out _);
        await store.SaveAsync(SteamSessionFixtures.Session(Now));
        store.Loads = 0;

        await Task.WhenAll(Enumerable.Range(0, 32)
            .Select(_ => Task.Run(async () => Assert.NotNull(await provider.GetAsync()))));

        Assert.Equal(1, store.Loads);
    }

    private static ISteamSessionProvider Build(out CountingSessionStore store, out FakeTimeProvider clock)
    {
        store = new CountingSessionStore();
        clock = new FakeTimeProvider(Now);
        return new SteamSessionProvider(store, new SteamWebOptions(), clock);
    }

    /// <summary>
    /// An <see cref="InMemorySteamSessionStore"/> that counts loads and claims it
    /// can persist, so the provider's caching and its NotPersisted branch can be
    /// tested independently of each other.
    /// </summary>
    private sealed class CountingSessionStore : ISteamSessionStore
    {
        private SteamSession? _session;

        public int Loads { get; set; }

        public bool CanPersist => true;

        public Task<SteamSession?> LoadAsync(CancellationToken ct = default)
        {
            Loads++;
            return Task.FromResult(_session);
        }

        public Task SaveAsync(SteamSession session, CancellationToken ct = default)
        {
            _session = session;
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken ct = default)
        {
            _session = null;
            return Task.CompletedTask;
        }
    }
}

/// <summary>
/// The registration S1 left a hole for, and the proof that filling it changed
/// nothing for a user who has not signed in.
/// </summary>
public sealed class SteamSessionRegistrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_session_source_and_provider_are_registered_as_singletons()
    {
        using var host = new SteamWebTestHost(SteamWebTestHost.DefaultResponder());

        // Two providers would each hold their own cached session, and two stores
        // would each decide separately whether the host can persist.
        Assert.Same(host.Resolve<ISteamSessionProvider>(), host.Resolve<ISteamSessionProvider>());
        Assert.Same(host.Resolve<ISteamSessionStore>(), host.Resolve<ISteamSessionStore>());
        Assert.IsType<SteamSessionCredentialSource>(host.Resolve<ISteamSessionCredentialSource>());
    }

    [Fact]
    public async Task With_no_session_stored_the_key_path_is_exactly_what_it_was()
    {
        // The whole behavioural claim of S2: registering a session source is
        // invisible until S3 writes a session.
        using var host = new SteamWebTestHost(SteamWebTestHost.DefaultResponder());

        var credentials = host.Resolve<ISteamCredentialProvider>();

        Assert.Null(await host.Resolve<ISteamSessionCredentialSource>().TryGetAsync());

        var chosen = await credentials.GetAsync(SteamCredentialPurpose.Unattended);
        Assert.NotNull(chosen);
        Assert.Equal(SteamCredentialKind.ApiKey, chosen.Kind);

        var inventory = await credentials.GetInventoryAsync();
        Assert.True(inventory.HasApiKey);
        Assert.False(inventory.HasSession);
        Assert.Null(inventory.SessionAccount);
    }

    [Fact]
    public async Task Once_a_session_is_stored_the_selector_sees_it()
    {
        using var host = new SteamWebTestHost(
            SteamWebTestHost.DefaultResponder(), apiKey: null, now: Now);

        await host.Resolve<ISteamSessionProvider>().SaveAsync(SteamSessionFixtures.Session(Now));

        var credentials = host.Resolve<ISteamCredentialProvider>();

        // Keyless: the session is the only credential, and it is chosen for both
        // purposes because there is nothing else.
        var chosen = await credentials.GetAsync(SteamCredentialPurpose.UserInitiated);
        Assert.NotNull(chosen);
        Assert.Equal(SteamCredentialKind.SessionToken, chosen.Kind);
        Assert.Equal(SteamCredential.SessionTokenParameter, chosen.ParameterName);

        var inventory = await credentials.GetInventoryAsync();
        Assert.True(inventory.HasSession);
        Assert.True(inventory.SessionUsable);
        Assert.Equal(SteamSessionFixtures.Subject, inventory.SessionAccount!.Value.ToString());
    }

    [Fact]
    public async Task An_expired_session_is_registered_but_not_usable()
    {
        // The distinction the Stores screen renders: something is connected, and
        // it cannot be sent. Collapsing this into "no session" is the silent
        // degradation §4.7's eighth condition forbids.
        using var host = new SteamWebTestHost(
            SteamWebTestHost.DefaultResponder(), apiKey: null, now: Now);

        await host.Resolve<ISteamSessionProvider>().SaveAsync(
            SteamSessionFixtures.Session(Now, refreshLife: TimeSpan.FromDays(30)));

        host.Clock.Advance(TimeSpan.FromDays(31));

        var credentials = host.Resolve<ISteamCredentialProvider>();

        Assert.Null(await credentials.GetAsync(SteamCredentialPurpose.Unattended));

        var inventory = await credentials.GetInventoryAsync();
        Assert.True(inventory.HasSession);
        Assert.False(inventory.SessionUsable);
        Assert.True(inventory.HasAnyCredential);
        Assert.False(inventory.HasUsableCredential);
    }

    [Fact]
    public async Task Nothing_about_a_session_reaches_even_a_trace_level_log()
    {
        using var host = new SteamWebTestHost(
            SteamWebTestHost.DefaultResponder(), apiKey: null, now: Now);

        var session = SteamSessionFixtures.Session(Now);
        var provider = host.Resolve<ISteamSessionProvider>();

        await provider.SaveAsync(session);
        await provider.GetAsync();
        await provider.GetHealthAsync();
        await provider.SignOutAsync();

        var log = host.Logs.AllText;

        Assert.DoesNotContain(session.AccessToken, log, StringComparison.Ordinal);
        Assert.DoesNotContain(session.RefreshToken, log, StringComparison.Ordinal);
        Assert.DoesNotContain(SteamSessionFixtures.Subject, log, StringComparison.Ordinal);
    }
}
