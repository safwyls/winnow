using Winnow.Enrich.SteamWeb;
using Winnow.Enrich.SteamWeb.Credentials;
using Winnow.Enrich.SteamWeb.Http;
using Xunit;

namespace Winnow.Tests.SteamWeb;

/// <summary>
/// <see cref="SteamCredential"/> and <see cref="SteamCredential.AppendTo"/>.
/// <c>AppendTo</c> is the only place a credential is allowed to enter a URI,
/// so everything that used to be true of the hand-concatenated <c>key=</c> has
/// to be true of it, for both credential kinds: one parameter, escaped, never
/// both names at once.
/// </summary>
public class SteamCredentialTests
{
    [Fact]
    public void An_api_key_travels_as_key()
    {
        var credential = SteamCredential.TryCreateApiKey("ABCDEF", "settings");

        Assert.NotNull(credential);
        Assert.Equal(SteamCredentialKind.ApiKey, credential.Kind);
        Assert.Equal("key", credential.ParameterName);
        Assert.Null(credential.ExpiresAt);
        Assert.Null(credential.SteamId);
    }

    [Fact]
    public void A_session_token_travels_as_access_token()
    {
        var account = SteamId.FromAccountId(12345);
        var expiry = new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero);
        var credential = SteamCredential.TryCreateSessionToken("eyJ.body.sig", "webview", expiry, account);

        Assert.NotNull(credential);
        Assert.Equal(SteamCredentialKind.SessionToken, credential.Kind);
        Assert.Equal("access_token", credential.ParameterName);
        Assert.Equal(expiry, credential.ExpiresAt);
        Assert.Equal(account, credential.SteamId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_values_count_as_unset_for_both_kinds(string? value)
    {
        Assert.Null(SteamCredential.TryCreateApiKey(value, "settings"));
        Assert.Null(SteamCredential.TryCreateSessionToken(value, "webview", null, null));
    }

    [Fact]
    public void Surrounding_whitespace_is_trimmed_off_both_kinds()
    {
        Assert.Equal("ABCDEF", SteamCredential.TryCreateApiKey("  ABCDEF  ", "settings")!.Value);
        Assert.Equal(
            "eyJ.body.sig",
            SteamCredential.TryCreateSessionToken(" eyJ.body.sig ", "webview", null, null)!.Value);
    }

    [Fact]
    public void AppendTo_adds_the_key_parameter_once_to_a_uri_that_already_has_a_query()
    {
        var credential = SteamCredential.TryCreateApiKey("ABCDEF", "settings")!;

        var uri = credential.AppendTo("IPlayerService/GetOwnedGames/v1/?steamid=1&format=json");

        Assert.Equal("IPlayerService/GetOwnedGames/v1/?steamid=1&format=json&key=ABCDEF", uri);
        Assert.Equal(1, CountOf(uri, "key="));
    }

    [Fact]
    public void AppendTo_adds_the_access_token_parameter_once()
    {
        var credential = SteamCredential.TryCreateSessionToken("TOKEN", "webview", null, null)!;

        var uri = credential.AppendTo("IPlayerService/ClientGetLastPlayedTimes/v1/?format=json");

        Assert.Equal(
            "IPlayerService/ClientGetLastPlayedTimes/v1/?format=json&access_token=TOKEN", uri);
        Assert.Equal(1, CountOf(uri, "access_token="));

        // The token goes in access_token and nowhere else: a request must never
        // carry both names for the same secret.
        Assert.DoesNotContain("&key=", uri, StringComparison.Ordinal);
    }

    [Fact]
    public void AppendTo_opens_the_query_when_the_uri_has_none()
    {
        var credential = SteamCredential.TryCreateApiKey("ABCDEF", "settings")!;

        Assert.Equal(
            "IPlayerService/GetOwnedGames/v1/?key=ABCDEF",
            credential.AppendTo("IPlayerService/GetOwnedGames/v1/"));
    }

    /// <summary>
    /// A JWT carries <c>.</c>, <c>-</c>, <c>_</c> and other encodings carry
    /// <c>+</c> and <c>/</c>; an API key is hex today but nothing guarantees
    /// that forever. An unescaped value could smuggle a second parameter into
    /// the query. Escaping is not optional.
    /// </summary>
    [Theory]
    [InlineData(SteamCredentialKind.ApiKey, "key")]
    [InlineData(SteamCredentialKind.SessionToken, "access_token")]
    public void AppendTo_escapes_the_value(SteamCredentialKind kind, string parameter)
    {
        const string awkward = "a+b/c=d&e f";
        var credential = kind is SteamCredentialKind.ApiKey
            ? SteamCredential.TryCreateApiKey(awkward, "settings")!
            : SteamCredential.TryCreateSessionToken(awkward, "webview", null, null)!;

        var uri = credential.AppendTo("Some/Endpoint/v1/?format=json");

        Assert.Equal(
            "Some/Endpoint/v1/?format=json&" + parameter + "=" + Uri.EscapeDataString(awkward), uri);

        // The raw value would have smuggled a second parameter into the query.
        Assert.DoesNotContain("&e", uri, StringComparison.Ordinal);
        Assert.Equal(awkward, Uri.UnescapeDataString(uri[(uri.LastIndexOf('=') + 1)..]));
    }

    [Fact]
    public void AppendTo_round_trips_through_the_uri_parser_for_both_kinds()
    {
        foreach (var credential in new[]
        {
            SteamCredential.TryCreateApiKey("ABCDEF0123456789", "settings")!,
            SteamCredential.TryCreateSessionToken("eyJ0eXAi.eyJzdWIi.c2ln-_", "webview", null, null)!,
        })
        {
            var uri = new Uri(
                new Uri("https://api.steampowered.com/"),
                credential.AppendTo("IPlayerService/GetOwnedGames/v1/?steamid=1&format=json"));

            var sent = new RecordedSteamWebRequest(HttpMethod.Get, uri, "Winnow");

            Assert.Equal(credential.Value, sent.Parameter(credential.ParameterName));
            Assert.Equal("1", sent.Parameter("steamid"));
        }
    }

    [Fact]
    public void ToString_redacts_the_value_for_both_kinds()
    {
        foreach (var credential in new[]
        {
            SteamCredential.TryCreateApiKey("SUPERSECRETKEYVALUE", "settings")!,
            SteamCredential.TryCreateSessionToken("SUPERSECRETKEYVALUE", "webview", null, null)!,
        })
        {
            // The compiler-generated record ToString would print Value. This is
            // the guard against the first person who interpolates one into a log
            // line, and it is the same guard SteamApiKey has carried.
            var rendered = $"{credential}";

            Assert.DoesNotContain("SUPERSECRETKEYVALUE", rendered, StringComparison.Ordinal);
            Assert.Contains("redacted", rendered, StringComparison.Ordinal);
            Assert.Contains(credential.Provenance, rendered, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// <see cref="SteamWebRedaction"/> is an allowlist, so <c>access_token</c>
    /// is redacted by construction rather than because someone remembered to add
    /// it to a denylist. This pins that for the parameter name the new credential
    /// kind introduces, and asserts that neither credential parameter name may
    /// ever be added to the allowlist.
    /// </summary>
    [Fact]
    public void The_redactor_redacts_the_session_token_parameter_by_construction()
    {
        var credential = SteamCredential.TryCreateSessionToken("SUPERSECRETTOKEN", "webview", null, null)!;

        var described = SteamWebRedaction.Describe(new Uri(
            new Uri("https://api.steampowered.com/"),
            credential.AppendTo("IPlayerService/GetOwnedGames/v1/?steamid=1&format=json")));

        Assert.DoesNotContain("SUPERSECRETTOKEN", described, StringComparison.Ordinal);
        Assert.Contains(
            "access_token=" + SteamWebRedaction.Placeholder, described, StringComparison.Ordinal);

        // Neither parameter name a credential can travel under is on the
        // allowlist, and neither may ever be added to it.
        Assert.DoesNotContain(
            SteamCredential.SessionTokenParameter, SteamWebRedaction.SafeParameters);
        Assert.DoesNotContain(SteamCredential.ApiKeyParameter, SteamWebRedaction.SafeParameters);

        // The account and the format still survive, so the line stays useful.
        Assert.Contains("steamid=1", described, StringComparison.Ordinal);
    }

    [Fact]
    public void FromApiKey_carries_the_source_across_as_provenance()
    {
        var lifted = SteamCredential.FromApiKey(SteamApiKey.TryCreate("ABCDEF", "settings"));

        Assert.NotNull(lifted);
        Assert.Equal(SteamCredentialKind.ApiKey, lifted.Kind);
        Assert.Equal("ABCDEF", lifted.Value);
        Assert.Equal("settings", lifted.Provenance);

        Assert.Null(SteamCredential.FromApiKey(null));
    }

    private static int CountOf(string haystack, string needle)
    {
        var count = 0;
        for (var at = haystack.IndexOf(needle, StringComparison.Ordinal);
             at >= 0;
             at = haystack.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
