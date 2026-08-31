using System.Text;
using System.Text.Json;
using Winnow.App.Services;
using Winnow.Auth.WebView;
using Winnow.Enrich.SteamWeb.Http;
using Xunit;

namespace Winnow.Tests.SteamWeb;

/// <summary>
/// THROWAWAY VERIFICATION SCAFFOLDING — TASK-56, spike items 1 and 5.
///
/// <para>The testable half of the sign-in probe. The probe's whole point is a
/// live session, and none of that is reachable here: a WebView2 control cannot
/// be created in a unit test and nobody can type a Steam Guard code into one.
/// What <em>is</em> reachable is the three pure parts the live run depends on,
/// and each of them fails silently if it is wrong. A claim read off the wrong
/// segment prints a plausible expiry for a token that expires elsewhere, a
/// redaction that misses puts a live credential on the user's terminal, and a
/// request URI with the wrong parameter name proves nothing about token auth
/// because it never used it.</para>
/// </summary>
public class SteamSignInProbeTests
{
    /// <summary>A SteamID64 in the individual-account range. Not a real account.</summary>
    private const string Subject = "76561198000000001";

    [Fact]
    public void A_token_payload_yields_its_expiry_subject_audience_and_issuer()
    {
        var expiry = DateTimeOffset.UtcNow.AddHours(23).ToUnixTimeSeconds();
        var token = Jwt($$"""
            {"iss":"steam","sub":"{{Subject}}","aud":["web"],"exp":{{expiry}}}
            """);

        var claims = SteamSignInProbeFacts.ReadClaims(token);

        Assert.True(claims.Readable);
        Assert.Equal(expiry, claims.ExpiresAt!.Value.ToUnixTimeSeconds());
        Assert.Equal(Subject, claims.Subject);
        Assert.Equal(new[] { "web" }, claims.Audiences);
        Assert.Equal("steam", claims.Issuer);
    }

    [Fact]
    public void A_single_string_audience_reads_as_one_entry()
    {
        // steam-session lists aud as an array per platform type, but the claim
        // is a string-or-array in every JWT ever written and a probe that
        // crashed on the scalar form would report "no aud claim" for the one
        // shape it was looking for.
        var claims = SteamSignInProbeFacts.ReadClaims(Jwt("""{"aud":"web"}"""));

        Assert.True(claims.Readable);
        Assert.Equal(new[] { "web" }, claims.Audiences);
    }

    [Fact]
    public void A_payload_needing_base64url_substitutions_and_padding_still_decodes()
    {
        // The two characters base64url swaps and the padding it drops. A
        // decoder that forgot either reads real Steam tokens roughly three
        // times in four, which is exactly often enough to look like it works.
        // The two runs below are chosen so that both substitutions are forced:
        // eight consecutive 0x3F bytes always produce a '/' sextet and eight
        // consecutive 0x3E bytes always produce a '+' one, whatever the
        // alignment.
        var token = Jwt($$"""{"sub":"{{Subject}}","note":"????????>>>>>>>>"}""");
        var payload = token.Split('.')[1];

        Assert.Contains('_', payload);
        Assert.Contains('-', payload);
        Assert.DoesNotContain('=', payload);
        Assert.Equal(Subject, SteamSignInProbeFacts.ReadClaims(token).Subject);
    }

    [Fact]
    public void A_nonsense_signature_is_not_a_reason_to_refuse_the_claims()
    {
        // Deliberate, and the reason the probe is diagnostics rather than auth:
        // it prints what the token says about itself and decides nothing.
        var token = Jwt($$"""{"sub":"{{Subject}}"}""", signature: "not-a-signature");

        Assert.Equal(Subject, SteamSignInProbeFacts.ReadClaims(token).Subject);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("notatoken")]
    [InlineData("one.two")]
    [InlineData("header.!!!not-base64!!!.signature")]
    public void An_unreadable_token_answers_empty_rather_than_throwing(string? token)
    {
        var claims = SteamSignInProbeFacts.ReadClaims(token);

        Assert.Null(claims.ExpiresAt);
        Assert.Null(claims.Subject);
        Assert.Empty(claims.Audiences);
    }

    [Fact]
    public void A_payload_that_is_valid_base64_but_not_an_object_is_not_readable()
    {
        var token = Jwt("[1,2,3]");

        Assert.False(SteamSignInProbeFacts.ReadClaims(token).Readable);
    }

    [Fact]
    public void Redaction_removes_a_whole_jwt_and_every_segment_of_it()
    {
        var token = Jwt($$"""{"sub":"{{Subject}}","exp":1799999999}""");
        var scrubbed = SteamSignInProbeFacts.Redact(
            "Steam refused the request for " + token + " at IPlayerService.");

        Assert.DoesNotContain(token, scrubbed, StringComparison.Ordinal);
        foreach (var segment in token.Split('.'))
        {
            Assert.DoesNotContain(segment, scrubbed, StringComparison.Ordinal);
        }

        Assert.Contains(SteamSignInProbeFacts.Placeholder, scrubbed, StringComparison.Ordinal);
    }

    [Fact]
    public void Redaction_removes_a_named_credential_parameter_value()
    {
        var scrubbed = SteamSignInProbeFacts.Redact(
            "GET /IPlayerService/GetOwnedGames/v1/?steamid=76561198000000001&access_token=abc123def456 failed");

        Assert.DoesNotContain("abc123def456", scrubbed, StringComparison.Ordinal);
        Assert.Contains("access_token=" + SteamSignInProbeFacts.Placeholder, scrubbed, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("steamLoginSecure=76561198000000001%7C%7Cabcdefabcdefabcdef")]
    [InlineData("steamRefresh_steam: eyJhbGciOiJFZERTQSJ9.payloadpayload.sigsigsig")]
    [InlineData("key=0123456789ABCDEF0123456789ABCDEF")]
    [InlineData("sessionid=deadbeefdeadbeefdeadbeef")]
    public void Redaction_removes_every_credential_shape_the_probe_can_meet(string text)
    {
        var scrubbed = SteamSignInProbeFacts.Redact(text);

        Assert.Contains(SteamSignInProbeFacts.Placeholder, scrubbed, StringComparison.Ordinal);
        Assert.DoesNotContain("abcdefabcdefabcdef", scrubbed, StringComparison.Ordinal);
        Assert.DoesNotContain("payloadpayload", scrubbed, StringComparison.Ordinal);
        Assert.DoesNotContain("0123456789ABCDEF0123456789ABCDEF", scrubbed, StringComparison.Ordinal);
        Assert.DoesNotContain("deadbeefdeadbeefdeadbeef", scrubbed, StringComparison.Ordinal);
    }

    [Fact]
    public void Redaction_leaves_the_facts_the_report_exists_to_print()
    {
        // A redactor that ate the endpoint names, the statuses and the counts
        // would be safe and useless.
        const string Line =
            "ISaleFeatureService/GetUserYearInReview/v1/ returned 401; x-eresult 15; 2025: 96 games, 412 points";

        Assert.Equal(Line, SteamSignInProbeFacts.Redact(Line));
    }

    [Fact]
    public void Redaction_of_nothing_is_the_empty_string()
    {
        Assert.Equal(string.Empty, SteamSignInProbeFacts.Redact(null));
        Assert.Equal(string.Empty, SteamSignInProbeFacts.Redact(string.Empty));
    }

    [Fact]
    public void The_last_played_request_carries_the_token_and_no_steamid()
    {
        // Verified live under key auth on 2026-08-28: this endpoint takes no
        // steamid, because the credential identifies the account. A token
        // carries the same identity in its sub claim, so sending one would be
        // asking a different question than the one the probe is for.
        var uri = SteamSignInProbeFacts.LastPlayedTimesUri("tok-en");

        Assert.Equal("/IPlayerService/ClientGetLastPlayedTimes/v1/", uri.AbsolutePath);
        Assert.Contains("access_token=tok-en", uri.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("steamid=", uri.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("&key=", uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public void The_owned_games_request_carries_the_token_and_the_three_mandated_flags()
    {
        var uri = SteamSignInProbeFacts.OwnedGamesUri("tok-en", 76561198000000001UL);

        Assert.Equal("/IPlayerService/GetOwnedGames/v1/", uri.AbsolutePath);
        Assert.Contains("steamid=76561198000000001", uri.Query, StringComparison.Ordinal);
        Assert.Contains("include_appinfo=1", uri.Query, StringComparison.Ordinal);
        Assert.Contains("include_played_free_games=1", uri.Query, StringComparison.Ordinal);
        Assert.Contains("skip_unvetted_apps=false", uri.Query, StringComparison.Ordinal);
        Assert.Contains("access_token=tok-en", uri.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("&key=", uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public void The_year_in_review_request_carries_the_token_the_account_and_the_year()
    {
        var uri = SteamSignInProbeFacts.YearInReviewUri("tok-en", 76561198000000001UL, 2025);

        Assert.Equal("/ISaleFeatureService/GetUserYearInReview/v1/", uri.AbsolutePath);
        Assert.Contains("steamid=76561198000000001", uri.Query, StringComparison.Ordinal);
        Assert.Contains("year=2025", uri.Query, StringComparison.Ordinal);
        Assert.Contains("access_token=tok-en", uri.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("&key=", uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_request_goes_to_the_web_api_host_over_https()
    {
        foreach (var uri in Requests())
        {
            Assert.Equal("https", uri.Scheme);
            Assert.Equal("api.steampowered.com", uri.Host);
        }
    }

    [Fact]
    public void A_token_with_url_significant_characters_is_escaped_into_the_query()
    {
        // A real Steam JWT is base64url and needs no escaping, which is exactly
        // why an unescaped concatenation would pass every live run and only
        // break the day Valve changes the encoding.
        var uri = SteamSignInProbeFacts.LastPlayedTimesUri("a+b/c=d&e");
        var sent = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Single(static pair => pair.StartsWith("access_token=", StringComparison.Ordinal));

        Assert.DoesNotContain("a+b/c=d&e", uri.Query, StringComparison.Ordinal);
        Assert.Equal("a+b/c=d&e", Uri.UnescapeDataString(sent["access_token=".Length..]));
    }

    [Fact]
    public void An_absent_token_is_refused_rather_than_sent_as_an_empty_parameter()
    {
        Assert.Throws<ArgumentException>(() => SteamSignInProbeFacts.LastPlayedTimesUri("  "));
    }

    [Fact]
    public void The_shipped_redactor_hides_the_token_from_every_request_uri()
    {
        // The probe logs requests through SteamWebRedaction rather than a
        // second redactor of its own. That type works from an ALLOWLIST:
        // access_token is hidden because it is not on the list, not because
        // anybody remembered to add it. This is the test that notices if the
        // allowlist ever grows to include it.
        foreach (var uri in Requests())
        {
            var described = SteamWebRedaction.Describe(uri);

            Assert.DoesNotContain("tok-en", described, StringComparison.Ordinal);
            Assert.Contains(
                "access_token=" + SteamWebRedaction.Placeholder, described, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_mint_script_reads_the_two_routes_the_spike_documented()
    {
        // Playnite's route and Valve's own auth_refresh.js global. If a later
        // edit drops either, the probe answers "no token" on a page that was
        // carrying one, and the finding recorded against spike item 1 is wrong
        // rather than missing.
        Assert.Contains("application_config", SteamSignInProbeScripts.Mint, StringComparison.Ordinal);
        Assert.Contains("data-store_user_config", SteamSignInProbeScripts.Mint, StringComparison.Ordinal);
        Assert.Contains("webapi_token", SteamSignInProbeScripts.Mint, StringComparison.Ordinal);
        Assert.Contains("g_wapit", SteamSignInProbeScripts.Mint, StringComparison.Ordinal);
        Assert.Contains("data-userinfo", SteamSignInProbeScripts.Mint, StringComparison.Ordinal);
    }

    [Fact]
    public void The_probe_starts_and_mints_on_the_store_origin_only()
    {
        Assert.Equal("store.steampowered.com", SteamSignInProbeSession.LoginPage.Host);
        Assert.Equal("https", SteamSignInProbeSession.LoginPage.Scheme);

        Assert.NotEmpty(SteamSignInProbeSession.MintPages);
        foreach (var page in SteamSignInProbeSession.MintPages)
        {
            Assert.Equal("store.steampowered.com", page.Host);
            Assert.Equal("https", page.Scheme);
        }
    }

    [Fact]
    public void The_store_home_page_is_in_mint_scope()
    {
        // THE REGRESSION. The first run of this probe stalled for its whole
        // ten-minute timeout because the scope predicate was copied verbatim
        // from the harvest session, whose IsSignInJourney counts an empty path
        // as part of signing in. Steam lands the user on the store ROOT after
        // Steam Guard, so the one page the probe was ever shown was the one page
        // it refused to read.
        Assert.True(SteamSignInProbeSession.IsMintScope(new Uri("https://store.steampowered.com/")));
        Assert.True(SteamSignInProbeSession.IsMintScope(new Uri("https://store.steampowered.com")));
        Assert.True(SteamSignInProbeSession.IsMintScope(
            new Uri("https://store.steampowered.com/?snr=1_4_4__global-header")));
    }

    [Fact]
    public void Every_mint_page_is_in_mint_scope()
    {
        // A candidate the walk steers to but the scope then refuses to read
        // would burn a settle period and report "no token on this page" about a
        // page nobody ever looked at.
        foreach (var page in SteamSignInProbeSession.MintPages)
        {
            Assert.True(SteamSignInProbeSession.IsMintScope(page), page.ToString());
        }
    }

    [Theory]
    [InlineData("https://store.steampowered.com/login/")]
    [InlineData("https://store.steampowered.com/login/?redir=account")]
    [InlineData("https://store.steampowered.com/join/")]
    [InlineData("https://store.steampowered.com/twofactor/manage")]
    [InlineData("https://store.steampowered.com/password/reset")]
    [InlineData("https://store.steampowered.com/mobilelogin")]
    [InlineData("https://store.steampowered.com/account/security")]
    public void A_page_the_user_types_credentials_into_is_never_in_mint_scope(string url)
    {
        Assert.False(SteamSignInProbeSession.IsMintScope(new Uri(url)));
    }

    [Theory]
    [InlineData("https://login.steampowered.com/jwt/ajaxrefresh")]
    [InlineData("https://steamcommunity.com/my/edit/info")]
    [InlineData("https://help.steampowered.com/en/")]
    [InlineData("https://store.steampowered.evil.com/")]
    [InlineData("http://store.steampowered.com/")]
    public void Nothing_off_the_store_origin_is_in_mint_scope(string url)
    {
        // Navigable is not readable. The community route would mint a token too
        // (spike section 1, route 2) and is deliberately still out of scope: the
        // probe reads one origin.
        Assert.False(SteamSignInProbeSession.IsMintScope(new Uri(url)));
    }

    [Fact]
    public void Nothing_the_probe_reads_lies_outside_what_the_shipped_policy_already_trusts()
    {
        // The invariant that keeps the probe's own scope honest: it is a strict
        // subset of the account-page policy's trusted origin, so the probe can
        // never read a document that flow would not already have been allowed
        // to navigate to.
        var policy = Winnow.Core.Auth.SteamAccountPagePolicy.For(0, 0);

        foreach (var page in SteamSignInProbeSession.MintPages.Append(
            new Uri("https://store.steampowered.com/")))
        {
            Assert.True(SteamSignInProbeSession.IsMintScope(page));
            Assert.True(policy.IsTrustedOrigin(page));
        }
    }

    [Fact]
    public void The_shipped_harvest_gate_is_left_exactly_as_the_account_page_flow_needs_it()
    {
        // The fix gave the probe its own predicate rather than widening this
        // one. If a later edit relaxes AllowsHarvest to admit the store root,
        // the account-page session gains the right to run a script in a document
        // the user never agreed to hand over, and this is the test that says so.
        var policy = Winnow.Core.Auth.SteamAccountPagePolicy.For(0, 0);

        Assert.False(policy.AllowsHarvest(new Uri("https://store.steampowered.com/")));
        Assert.False(policy.AllowsHarvest(new Uri("https://store.steampowered.com/explore/")));
        Assert.True(policy.AllowsHarvest(new Uri("https://store.steampowered.com/account/licenses/")));
        Assert.True(policy.AllowsHarvest(new Uri("https://store.steampowered.com/account/history/")));
    }

    [Fact]
    public void The_mint_scope_and_the_harvest_gate_deliberately_disagree_about_the_store_root()
    {
        // The two predicates answer different questions and this pins that they
        // are allowed to differ, so nobody "fixes" the divergence by collapsing
        // them back into one.
        var root = new Uri("https://store.steampowered.com/");

        Assert.True(SteamSignInProbeSession.IsMintScope(root));
        Assert.False(Winnow.Core.Auth.SteamAccountPagePolicy.For(0, 0).AllowsHarvest(root));
    }

    [Fact]
    public void A_null_address_is_not_in_mint_scope()
    {
        Assert.False(SteamSignInProbeSession.IsMintScope(null));
    }

    private static IEnumerable<Uri> Requests() =>
    [
        SteamSignInProbeFacts.LastPlayedTimesUri("tok-en"),
        SteamSignInProbeFacts.OwnedGamesUri("tok-en", 76561198000000001UL),
        SteamSignInProbeFacts.YearInReviewUri("tok-en", 76561198000000001UL, 2025),
    ];

    /// <summary>Builds a JWT-shaped string around one payload. Nothing signs it; nothing checks.</summary>
    private static string Jwt(string payload, string signature = "c2lnbmF0dXJl")
        => Segment("""{"typ":"JWT","alg":"EdDSA"}""") + "." + Segment(payload) + "." + signature;

    private static string Segment(string json)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}

/// <summary>
/// The probe's report file.
///
/// <para>Winnow is a <c>WinExe</c>, so the process has no console unless one is
/// attached by hand and every <c>Console.WriteLine</c> in it may go nowhere —
/// code review finding F41, and the reason the second live run of this probe
/// printed nothing at all. The file is therefore not a convenience, it is the
/// channel the findings actually travel on, and these are the tests that say
/// so.</para>
/// </summary>
public class SteamProbeLogTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "winnow-probe-log-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A temp directory that outlives one test run is not a test failure.
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void The_report_lands_at_a_deterministic_path()
    {
        // Deterministic because somebody has to be told where to look, in a
        // sentence written before the run rather than after it.
        using var log = new SteamProbeLog(_root);

        Assert.Equal(
            Path.Combine(_root, SteamSignInProbeConsole.ReportFileName), log.Path);
    }

    [Fact]
    public void Every_line_is_on_disk_before_the_log_is_closed()
    {
        // The guarantee that matters when a run is killed halfway: a report
        // flushed only at the end is a report the user never sees, which is
        // exactly how the first two runs ended.
        using var log = new SteamProbeLog(_root);

        log.Line("SIGN-IN");
        log.Line("  outcome           : {0}", "TokenMinted");

        using var reader = new FileStream(
            log.Path!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var text = new StreamReader(reader);

        var written = text.ReadToEnd();

        Assert.Contains("SIGN-IN", written, StringComparison.Ordinal);
        Assert.Contains("outcome           : TokenMinted", written, StringComparison.Ordinal);
    }

    [Fact]
    public void The_directory_is_created_when_it_does_not_exist()
    {
        var nested = Path.Combine(_root, "a", "b");

        using var log = new SteamProbeLog(nested);

        Assert.NotNull(log.Path);
        Assert.True(File.Exists(log.Path));
    }

    [Fact]
    public void A_report_file_that_cannot_be_opened_is_survivable()
    {
        // The probe must still run and still print. Losing the file is bad;
        // throwing out of a diagnostic because it could not open its own log
        // would be worse.
        Directory.CreateDirectory(_root);
        var blocker = Path.Combine(_root, "blocked");
        File.WriteAllText(blocker, "not a directory");

        using var log = new SteamProbeLog(blocker);

        Assert.Null(log.Path);
        Assert.NotEqual("no reason recorded", log.Failure);

        log.Line("this must not throw");
        log.Line("nor {0} this", "must");
    }

    [Fact]
    public void Disposing_twice_is_harmless()
    {
        // Dispose runs from the using in Run AND from the hard-exit thread, and
        // those can race on the way out.
        var log = new SteamProbeLog(_root);
        log.Line("something");

        log.Dispose();
        log.Dispose();
    }

    [Fact]
    public void Composite_formatting_is_invariant()
    {
        using var log = new SteamProbeLog(_root);

        log.Line("{0} of 3 endpoints returned populated data under token auth.", 3);
        log.Dispose();

        Assert.Contains(
            "3 of 3 endpoints", File.ReadAllText(log.Path!), StringComparison.Ordinal);
    }
}
