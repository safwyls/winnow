using System.Text;
using System.Text.Json;
using Winnow.Enrich.SteamWeb;
using Winnow.Enrich.SteamWeb.Credentials;
using Xunit;

namespace Winnow.Tests.SteamWeb;

/// <summary>
/// Builds the tokens these tests need. Nothing here is signed, because nothing
/// in the product validates a signature: Steam decides whether a token is good
/// and does so on every request.
/// </summary>
internal static class SteamSessionFixtures
{
    /// <summary>A SteamID64 in the individual-account range. Not a real account.</summary>
    public const string Subject = "76561198000000001";

    /// <summary>Base64url with the padding dropped, which is what a JWT segment is.</summary>
    public static string Segment(string json)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>A three-segment token whose payload is the given JSON. The header and signature are filler.</summary>
    public static string Jwt(string payloadJson)
        => Segment("""{"typ":"JWT","alg":"EdDSA"}""") + "." + Segment(payloadJson) + ".SIGNATURE-NOT-CHECKED";

    /// <summary>An access token in the shape Valve mints: array audience, SteamID64 subject.</summary>
    public static string AccessToken(DateTimeOffset expiresAt, string subject = Subject)
        => Jwt($$"""
            {"iss":"steam","sub":"{{subject}}","aud":["web:store"],"exp":{{expiresAt.ToUnixTimeSeconds()}}}
            """);

    /// <summary>A refresh token in the cookie's <c>steamid64||jwt</c> shape.</summary>
    public static string RefreshToken(DateTimeOffset expiresAt, string subject = Subject)
        => subject + "||" + Jwt($$"""
            {"iss":"steam","sub":"{{subject}}","aud":["web:auth"],"exp":{{expiresAt.ToUnixTimeSeconds()}}}
            """);

    /// <summary>A session minted at <paramref name="now"/> with the two lifetimes the spike measured.</summary>
    public static SteamSession Session(
        DateTimeOffset now, TimeSpan? accessLife = null, TimeSpan? refreshLife = null)
        => SteamSession.TryCreate(
            AccessToken(now + (accessLife ?? TimeSpan.FromHours(24))),
            refreshLife is null
                ? RefreshToken(now + TimeSpan.FromDays(207))
                : RefreshToken(now + refreshLife.Value),
            now)!;

    /// <summary>
    /// A reversible stand-in for DPAPI, so the storage tests assert the
    /// <i>shape</i> of protection (that nothing readable is written and that it
    /// round-trips) without depending on a real Windows user profile.
    ///
    /// <para>Base64 is not encryption and is not pretending to be. The test that
    /// matters for real encryption is the DPAPI round-trip; this one exists so
    /// the store's own logic is testable anywhere.</para>
    /// </summary>
    public sealed class ReversibleProtector : ISteamSecretProtector
    {
        public bool FailToUnprotect { get; init; }

        public bool IsAvailable => true;

        public string Name => "test:reversible";

        public string? Protect(string plaintext)
            => Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext));

        public string? Unprotect(string protectedBase64)
        {
            if (FailToUnprotect)
            {
                return null;
            }

            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(protectedBase64));
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }
}

/// <summary>
/// What reaches disk, and what must never.
///
/// <para>These are section 4.7's second amendment made mechanical. Condition 2
/// permits exactly two secrets at rest and forbids a cookie jar, a
/// <c>steamLoginSecure</c>, a <c>sessionid</c>, a browser profile and any page
/// content; the shape assertion below is what turns adding one into a test
/// failure rather than a review question. Condition 2 also says a host that
/// cannot encrypt refuses to store, which is the refusal test.</para>
/// </summary>
public sealed class SteamSessionStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The closed list. Eleven fields, and the test fails if the serializer
    /// emits a twelfth, which is exactly what a "just stash the cookie too"
    /// change would do.
    /// </summary>
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

    [Fact]
    public async Task A_session_round_trips_through_the_store_and_the_protector()
    {
        var settings = new InMemorySettingsRepository();
        var protector = new SteamSessionFixtures.ReversibleProtector();
        var store = new SettingsSteamSessionStore(settings, protector);

        Assert.True(store.CanPersist);

        var session = SteamSessionFixtures.Session(Now);
        await store.SaveAsync(session);

        var loaded = await store.LoadAsync();

        Assert.NotNull(loaded);
        Assert.Equal(session.AccessToken, loaded.AccessToken);
        Assert.Equal(session.RefreshToken, loaded.RefreshToken);
        Assert.Equal(session.SteamId, loaded.SteamId);
        Assert.Equal(session.ExpiresAt, loaded.ExpiresAt);
        Assert.Equal(session.RefreshExpiresAt, loaded.RefreshExpiresAt);
        Assert.Equal(session.MintedAt, loaded.MintedAt);
        Assert.Equal(session.Issuer, loaded.Issuer);
        Assert.Equal(session.Audience, loaded.Audience);
        Assert.Equal(0, loaded.RenewalFailures);
        Assert.Null(loaded.LastRenewedAt);
        Assert.Equal(SteamSessionRenewalFailure.None, loaded.LastFailureKind);
    }

    [Fact]
    public async Task Neither_token_is_readable_in_the_settings_table()
    {
        var settings = new InMemorySettingsRepository();
        var store = new SettingsSteamSessionStore(settings, new SteamSessionFixtures.ReversibleProtector());

        var session = SteamSessionFixtures.Session(Now);
        await store.SaveAsync(session);

        var stored = await settings.GetAsync(SettingsSteamSessionStore.SessionSetting);

        Assert.NotNull(stored);
        Assert.DoesNotContain(session.AccessToken, stored, StringComparison.Ordinal);
        Assert.DoesNotContain(session.RefreshToken!, stored, StringComparison.Ordinal);

        // The account id is not a credential, but it identifies a real person.
        Assert.DoesNotContain(SteamSessionFixtures.Subject, stored, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_stored_shape_holds_the_two_permitted_secrets_and_nothing_else()
    {
        // Condition 2 of the second §4.7 amendment, as an assertion rather than
        // as a paragraph. The stored blob is decrypted here on purpose: the
        // question is not whether it is encrypted (the test above asks that) but
        // what is inside it once it is not.
        var settings = new InMemorySettingsRepository();
        var protector = new SteamSessionFixtures.ReversibleProtector();
        var store = new SettingsSteamSessionStore(settings, protector);

        await store.SaveAsync(SteamSessionFixtures.Session(Now));

        var json = protector.Unprotect((await settings.GetAsync(SettingsSteamSessionStore.SessionSetting))!)!;

        using var document = JsonDocument.Parse(json);
        var fields = document.RootElement.EnumerateObject().Select(p => p.Name).ToArray();

        Assert.Equal(PermittedFields.Order(StringComparer.Ordinal), fields.Order(StringComparer.Ordinal));

        // Named individually as well, because the set comparison above would
        // still pass if someone renamed a field to smuggle one of these in
        // under a permitted-looking name.
        foreach (var name in fields)
        {
            foreach (var forbidden in new[]
                     {
                         "cookie", "session_id", "sessionid", "login", "password",
                         "profile", "html", "page", "document", "key",
                     })
            {
                Assert.DoesNotContain(forbidden, name, StringComparison.OrdinalIgnoreCase);
            }
        }

        // And over the whole document for the specific things §4.7 names. These
        // are long and distinctive enough not to collide with base64 by chance,
        // which is why the check above is on names rather than on the blob.
        foreach (var forbidden in new[] { "steamLoginSecure", "steamRefresh_steam", "sessionid", "<html", "<!DOCTYPE" })
        {
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task A_garbled_blob_yields_no_session_rather_than_an_exception()
    {
        // Three ways the same thing goes wrong, all of which mean "sign in
        // again" and none of which mean "crash": a value the protector cannot
        // decrypt, a value that decrypts to something that is not JSON, and a
        // value that is JSON but not a session.
        var undecryptable = new InMemorySettingsRepository();
        await undecryptable.SetAsync(SettingsSteamSessionStore.SessionSetting, "not-decryptable-by-anyone");
        Assert.Null(await new SettingsSteamSessionStore(
            undecryptable, new SteamSessionFixtures.ReversibleProtector { FailToUnprotect = true }).LoadAsync());

        var protector = new SteamSessionFixtures.ReversibleProtector();

        var notJson = new InMemorySettingsRepository();
        await notJson.SetAsync(SettingsSteamSessionStore.SessionSetting, protector.Protect("}{ not json at all")!);
        Assert.Null(await new SettingsSteamSessionStore(notJson, protector).LoadAsync());

        // Valid JSON, missing the refresh token and the account. Half a session
        // would only move the failure to the first request.
        var halfBuilt = new InMemorySettingsRepository();
        await halfBuilt.SetAsync(
            SettingsSteamSessionStore.SessionSetting,
            protector.Protect("""{"access_token":"a","refresh_token":"","steamid64":""}""")!);
        Assert.Null(await new SettingsSteamSessionStore(halfBuilt, protector).LoadAsync());
    }

    [Fact]
    public async Task A_session_that_cannot_be_encrypted_is_not_written_at_all()
    {
        // The rule with no exceptions: there is no plaintext fallback. The
        // failure mode of one is silent and permanent; the failure mode of
        // refusing is a sign-in the user repeats after a restart.
        var settings = new InMemorySettingsRepository();
        var store = new SettingsSteamSessionStore(settings, new UnavailableSteamSecretProtector());

        Assert.False(store.CanPersist);

        await store.SaveAsync(SteamSessionFixtures.Session(Now));

        Assert.Null(await settings.GetAsync(SettingsSteamSessionStore.SessionSetting));
        Assert.Null(await store.LoadAsync());
    }

    [Fact]
    public async Task A_host_with_no_settings_table_reports_that_it_cannot_persist()
    {
        var store = new SettingsSteamSessionStore(null, new SteamSessionFixtures.ReversibleProtector());

        Assert.False(store.CanPersist);

        await store.SaveAsync(SteamSessionFixtures.Session(Now));

        Assert.Null(await store.LoadAsync());
    }

    [Fact]
    public async Task Clearing_writes_empty_and_reads_back_as_absent()
    {
        // ISettingsRepository has no remove, so sign-out writes empty. The test
        // asserts both halves: that the row really is empty rather than deleted,
        // and that empty reads back as no session rather than as a parse error.
        var settings = new InMemorySettingsRepository();
        var store = new SettingsSteamSessionStore(settings, new SteamSessionFixtures.ReversibleProtector());

        await store.SaveAsync(SteamSessionFixtures.Session(Now));
        Assert.NotNull(await store.LoadAsync());

        await store.ClearAsync();

        Assert.Equal(string.Empty, await settings.GetAsync(SettingsSteamSessionStore.SessionSetting));
        Assert.Null(await store.LoadAsync());
    }

    [Fact]
    public async Task The_in_memory_store_keeps_a_session_for_the_run_and_admits_it_is_not_persisted()
    {
        var store = new InMemorySteamSessionStore();

        Assert.False(store.CanPersist);

        var session = SteamSessionFixtures.Session(Now);
        await store.SaveAsync(session);

        Assert.Same(session, await store.LoadAsync());

        await store.ClearAsync();
        Assert.Null(await store.LoadAsync());
    }

    [Fact]
    public void A_session_never_prints_its_tokens()
    {
        // The compiler-generated record ToString would print both tokens the
        // first time anyone interpolated a session into a log line.
        var session = SteamSessionFixtures.Session(Now);

        var text = session.ToString();

        Assert.DoesNotContain(session.AccessToken, text, StringComparison.Ordinal);
        Assert.DoesNotContain(session.RefreshToken!, text, StringComparison.Ordinal);
        Assert.Contains("redacted", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Dpapi_round_trips_a_session_blob_on_windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var protector = new DpapiSteamSecretProtector();
        var secret = SteamSessionFixtures.RefreshToken(Now.AddDays(207));

        var cipher = protector.Protect(secret);

        Assert.NotNull(cipher);
        Assert.DoesNotContain(secret, cipher, StringComparison.Ordinal);
        Assert.Equal(secret, protector.Unprotect(cipher));

        // Garbage in, null out, never an exception.
        Assert.Null(protector.Unprotect("not base64 at all !!"));
        Assert.Null(protector.Unprotect(Convert.ToBase64String([1, 2, 3, 4])));
        Assert.Null(protector.Unprotect(string.Empty));
    }

    [Fact]
    public void The_epic_protector_cannot_read_a_steam_blob()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // The entropy is distinct on purpose. Two credentials granting access to
        // two different accounts should not be interchangeable ciphertexts.
        var steam = new DpapiSteamSecretProtector();
        var epic = new Winnow.Ingest.Epic.Web.Auth.DpapiEpicSecretProtector();

        var cipher = steam.Protect("a-steam-refresh-token")!;

        Assert.Null(epic.Unprotect(cipher));
        Assert.Equal("a-steam-refresh-token", steam.Unprotect(cipher));
    }

    [Fact]
    public void The_windows_protector_is_selected_only_on_windows()
    {
        using var host = new SteamWebTestHost(SteamWebTestHost.DefaultResponder());

        var protector = host.Resolve<ISteamSecretProtector>();

        if (OperatingSystem.IsWindows())
        {
            Assert.IsType<DpapiSteamSecretProtector>(protector);
            Assert.True(protector.IsAvailable);
            Assert.Equal("dpapi:CurrentUser", protector.Name);
        }
        else
        {
            // Not a plaintext store. A refusal.
            Assert.IsType<UnavailableSteamSecretProtector>(protector);
            Assert.False(protector.IsAvailable);
        }
    }
}
