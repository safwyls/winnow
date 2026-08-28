using Winnow.Core.Auth;
using Xunit;

namespace Winnow.Tests.EpicWeb;

/// <summary>
/// The one part of the embedded sign-in that CAN be settled exhaustively without
/// a real browser: telling "nobody is signed in" apart from "the capture broke".
///
/// <para>This distinction is not an abstraction. The first build of the embedded
/// flow started on Epic's <c>id/api/redirect</c> endpoint, which answers a
/// cookie-less browser with every code field null. That is the endpoint working
/// correctly and saying "there is no session here" — but it was read as "no code
/// captured", so every first-time user got a message about a broken capture for
/// a sign-in that had simply never happened, and the real cause (no login form
/// was ever rendered) was invisible.</para>
/// </summary>
public class AuthCodeBodyTests
{
    /// <summary>Epic's two code fields, in the order the module looks for them.</summary>
    private static readonly AuthJsonCodeField[] EpicFields =
    [
        new("authorizationCode", AuthCodeKind.AuthorizationCode),
        new("exchangeCode", AuthCodeKind.ExchangeCode),
    ];

    [Fact]
    public void The_verbatim_no_session_body_is_no_session_not_no_code()
    {
        // Byte-for-byte what a real run got back. The whole redesign turns on
        // this reading.
        var reading = AuthCodeBody.Read(EpicFixturesWeb.RedirectNoSession(), EpicFields);

        Assert.Equal(AuthCodeBodyOutcome.NoSession, reading.Outcome);
        Assert.Null(reading.Code);

        // And emphatically not the generic miss.
        Assert.NotEqual(AuthCodeBodyOutcome.NotACodeBody, reading.Outcome);
    }

    [Fact]
    public void A_populated_body_yields_the_code_and_its_grant()
    {
        var reading = AuthCodeBody.Read(EpicFixturesWeb.RedirectWithCode(), EpicFields);

        Assert.Equal(AuthCodeBodyOutcome.CodeFound, reading.Outcome);
        Assert.Equal(AuthCodeKind.AuthorizationCode, reading.Kind);
        Assert.Equal("0123456789abcdef0123456789abcdef", reading.Code);
    }

    [Fact]
    public void An_exchange_code_is_found_and_carries_the_exchange_grant()
    {
        // The bridge is not the only way an exchange code can arrive: the same
        // body can carry one, and it must not be spent on the wrong grant.
        var reading = AuthCodeBody.Read(
            """{"authorizationCode":null,"exchangeCode":"EXCHANGE-VALUE","sid":null}""", EpicFields);

        Assert.Equal(AuthCodeBodyOutcome.CodeFound, reading.Outcome);
        Assert.Equal(AuthCodeKind.ExchangeCode, reading.Kind);
        Assert.Equal("EXCHANGE-VALUE", reading.Code);
    }

    [Fact]
    public void Field_order_is_the_callers_priority_order()
    {
        var reading = AuthCodeBody.Read(
            """{"authorizationCode":"AUTH","exchangeCode":"EXCHANGE"}""", EpicFields);

        Assert.Equal(AuthCodeKind.AuthorizationCode, reading.Kind);
        Assert.Equal("AUTH", reading.Code);
    }

    [Theory]
    // Not JSON at all — the login page itself, or any HTML the flow passes through.
    [InlineData("<!doctype html><html><body>Sign in</body></html>")]
    // JSON, but not this provider's code body.
    [InlineData("""{"errorCode":"errors.com.epicgames.accountportal.validation.unknown"}""")]
    // A JSON array, and a bare JSON value.
    [InlineData("""[{"authorizationCode":"NOPE"}]""")]
    [InlineData("\"authorizationCode\"")]
    // Nothing.
    [InlineData("")]
    [InlineData(null)]
    public void Anything_that_is_not_the_providers_code_body_is_classified_as_such(string? body)
    {
        var reading = AuthCodeBody.Read(body, EpicFields);

        Assert.Equal(AuthCodeBodyOutcome.NotACodeBody, reading.Outcome);
        Assert.Null(reading.Code);
    }

    [Fact]
    public void A_present_but_blank_field_is_still_no_session()
    {
        // Empty string rather than null. Same meaning, and treating it as a code
        // would send whitespace to the token endpoint.
        var reading = AuthCodeBody.Read("""{"authorizationCode":"","exchangeCode":"   "}""", EpicFields);

        Assert.Equal(AuthCodeBodyOutcome.NoSession, reading.Outcome);
    }

    [Fact]
    public void A_wrongly_typed_field_is_still_recognised_as_the_code_body()
    {
        // Presence is what identifies the body, not type. A provider that starts
        // sending false or 0 here has changed its shape, but it is still
        // answering about a session — and the honest reading is "no session",
        // not "this is some other page".
        var reading = AuthCodeBody.Read("""{"authorizationCode":false,"exchangeCode":0}""", EpicFields);

        Assert.Equal(AuthCodeBodyOutcome.NoSession, reading.Outcome);
    }

    [Fact]
    public void The_reading_never_prints_the_code_it_found()
    {
        var reading = AuthCodeBody.Read("""{"authorizationCode":"SECRET-CODE-VALUE"}""", EpicFields);

        Assert.DoesNotContain("SECRET-CODE-VALUE", reading.ToString(), StringComparison.Ordinal);
        Assert.Contains("redacted", reading.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void With_no_fields_to_look_for_nothing_is_a_code_body()
    {
        // A prompt given no fields must not start guessing at what a code looks
        // like from a JSON body's shape.
        var reading = AuthCodeBody.Read(EpicFixturesWeb.RedirectWithCode(), []);

        Assert.Equal(AuthCodeBodyOutcome.NotACodeBody, reading.Outcome);
    }
}
