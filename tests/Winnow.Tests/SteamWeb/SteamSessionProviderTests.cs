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
    public void The_browser_and_the_product_read_the_same_claims()
    {
        // One base64url decoder in the codebase, and no way for the two callers
        // to disagree about a token. The reader moved down to Core in S3 because
        // the sign-in session decodes a token the moment a store page hands it
        // over and cannot see this assembly; this pins that the projection above
        // it does not quietly drop a claim.
        var token = SteamSessionFixtures.AccessToken(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));

        var browser = Winnow.Core.Auth.SteamJwtClaims.Read(token);
        var product = SteamTokenClaims.Read(token);

        Assert.Equal(product.Readable, browser.Readable);
        Assert.Equal(product.ExpiresAt, browser.ExpiresAt);
        Assert.Equal(product.Subject, browser.Subject);
        Assert.Equal(product.Issuer, browser.Issuer);
        Assert.Equal(product.Audiences, browser.Audiences);
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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_refresh_token_still_yields_a_session(string? refreshToken)
    {
        // S2 required both secrets, which meant a sign-in that captured no
        // steamRefresh_steam cookie — the ordinary outcome when the user does not
        // tick remember-me on Steam's own form — persisted NOTHING and threw away
        // a working 24-hour access token. §4.7 condition 2 caps what may be
        // stored at two secrets; it does not require two.
        var session = SteamSession.TryCreate(
            SteamSessionFixtures.AccessToken(Now.AddHours(24)), refreshToken, Now);

        Assert.NotNull(session);
        Assert.False(session.HasRefreshToken);
        Assert.Null(session.RefreshToken);
        Assert.Null(session.RefreshExpiresAt);

        // The access token is exactly as usable as any other session's.
        Assert.True(session.IsAccessUsable(Now, TimeSpan.Zero));

        // And there is no renewal path, ever. Whitespace is not a credential.
        Assert.False(session.IsRefreshUsable(Now, TimeSpan.Zero));
    }
}

/// <summary>
/// The two kinds of session, told apart honestly.
///
/// <para>A session holding a refresh token is the renewable kind S6 will drive.
/// A token-only session is <see cref="SteamSessionHealth.Live"/> until its token
/// expires and <see cref="SteamSessionHealth.Expired"/> after, with nothing in
/// between — because there is no renewal for it to be due, and saying there is
/// would name a remedy nothing can apply.</para>
/// </summary>
public sealed class SteamSessionKindTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    private static SteamSession TokenOnly(TimeSpan? accessLife = null)
        => SteamSession.TryCreate(
            SteamSessionFixtures.AccessToken(Now + (accessLife ?? TimeSpan.FromHours(24))),
            refreshToken: null,
            Now)!;

    [Fact]
    public void A_token_only_session_is_live_for_its_whole_life()
    {
        var session = TokenOnly();

        Assert.Equal(SteamSessionHealth.Live, SteamSessionProvider.Classify(session, Now));

        // Deep inside the renewal lead window a renewable session would read
        // RenewalDue. This one has nothing to renew from, so it is still simply
        // working.
        Assert.Equal(
            SteamSessionHealth.Live,
            SteamSessionProvider.Classify(session, Now.AddHours(23).AddMinutes(30)));
    }

    [Fact]
    public void A_token_only_session_goes_straight_to_expired()
    {
        Assert.Equal(
            SteamSessionHealth.Expired,
            SteamSessionProvider.Classify(TokenOnly(), Now.AddHours(25)));
    }

    [Fact]
    public void A_renewable_session_still_passes_through_renewal_due()
    {
        // The distinction is a distinction, not a removal: the renewable kind
        // behaves exactly as it did.
        var renewable = SteamSessionFixtures.Session(Now);

        Assert.Equal(
            SteamSessionHealth.RenewalDue,
            SteamSessionProvider.Classify(renewable, Now.AddHours(23).AddMinutes(30)));

        Assert.Equal(
            SteamSessionHealth.RenewalDue,
            SteamSessionProvider.Classify(renewable, Now.AddHours(25)));
    }

    [Fact]
    public async Task A_token_only_session_round_trips_through_the_store()
    {
        var settings = new InMemorySettingsRepository();
        var store = new SettingsSteamSessionStore(settings, new SteamSessionFixtures.ReversibleProtector());

        var session = TokenOnly();
        await store.SaveAsync(session);

        var loaded = await store.LoadAsync();

        Assert.NotNull(loaded);
        Assert.Equal(session.AccessToken, loaded.AccessToken);
        Assert.Equal(session.SteamId, loaded.SteamId);
        Assert.Equal(session.ExpiresAt, loaded.ExpiresAt);

        // Null out, null back. Not an empty string that later reads as a
        // credential of length zero.
        Assert.Null(loaded.RefreshToken);
        Assert.False(loaded.HasRefreshToken);
    }

    [Fact]
    public async Task The_stored_shape_of_a_token_only_session_is_the_same_closed_list()
    {
        // §4.7 condition 2 again: the key set an audit reads must not depend on
        // which kind of session was stored, so refresh_token is still emitted —
        // as null — and no twelfth field appears in its place.
        var settings = new InMemorySettingsRepository();
        var protector = new SteamSessionFixtures.ReversibleProtector();
        var store = new SettingsSteamSessionStore(settings, protector);

        await store.SaveAsync(TokenOnly());

        var json = protector.Unprotect((await settings.GetAsync(SettingsSteamSessionStore.SessionSetting))!)!;

        using var document = System.Text.Json.JsonDocument.Parse(json);
        var fields = document.RootElement.EnumerateObject().Select(p => p.Name).ToArray();

        Assert.Equal(11, fields.Length);
        Assert.Contains("refresh_token", fields);
        Assert.Equal(
            System.Text.Json.JsonValueKind.Null,
            document.RootElement.GetProperty("refresh_token").ValueKind);

        foreach (var forbidden in new[] { "steamLoginSecure", "steamRefresh_steam", "sessionid", "<html", "<!DOCTYPE" })
        {
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task A_stored_session_whose_refresh_token_is_blank_reads_back_as_token_only()
    {
        // The shape an S2-era blob written by a build that always wrote a string
        // would have. Blank is absence, not a zero-length credential.
        var settings = new InMemorySettingsRepository();
        var protector = new SteamSessionFixtures.ReversibleProtector();

        var payload = $$"""
            {"access_token":"{{SteamSessionFixtures.AccessToken(Now.AddHours(24))}}",
             "expires_at":"{{Now.AddHours(24):O}}","audience":[],"issuer":"steam",
             "steamid64":"{{SteamSessionFixtures.Subject}}","refresh_token":"",
             "refresh_expires_at":null,"minted_at":"{{Now:O}}","last_renewed_at":null,
             "renewal_failures":0,"last_failure_kind":"None"}
            """;

        await settings.SetAsync(SettingsSteamSessionStore.SessionSetting, protector.Protect(payload)!);

        var loaded = await new SettingsSteamSessionStore(settings, protector).LoadAsync();

        Assert.NotNull(loaded);
        Assert.False(loaded.HasRefreshToken);
    }

    [Fact]
    public async Task The_credential_selector_sends_a_token_only_session()
    {
        // The point of relaxing the record: this session reaches the selector at
        // all. Under S2's rule it was never written and the user's completed
        // sign-in bought them nothing.
        using var host = new SteamWebTestHost(
            SteamWebTestHost.DefaultResponder(), apiKey: null, now: Now);

        await host.Resolve<ISteamSessionProvider>().SaveAsync(TokenOnly());

        var chosen = await host.Resolve<ISteamCredentialProvider>()
            .GetAsync(SteamCredentialPurpose.UserInitiated);

        Assert.NotNull(chosen);
        Assert.Equal(SteamCredentialKind.SessionToken, chosen.Kind);
    }
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
        Assert.DoesNotContain(session.RefreshToken!, log, StringComparison.Ordinal);
        Assert.DoesNotContain(SteamSessionFixtures.Subject, log, StringComparison.Ordinal);
    }
}
