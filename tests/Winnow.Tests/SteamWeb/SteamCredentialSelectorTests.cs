using Winnow.Enrich.SteamWeb;
using Winnow.Enrich.SteamWeb.Credentials;
using Xunit;

namespace Winnow.Tests.SteamWeb;

/// <summary>
/// The selector matrix for <see cref="SteamCredentialSelector"/>. Pins the
/// binding decision recorded on TASK-55 (decision note 2, 2026-08-30): the key
/// drives unattended schedulers because it does not expire; a user-initiated
/// call prefers the session because it identifies the account and needs no key
/// registration. An expired session falls through, and null is a normal outcome.
/// </summary>
public class SteamCredentialSelectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    private static SteamCredential Key
        => SteamCredential.TryCreateApiKey("the-one-key", "settings")!;

    private static SteamCredential UsableSession
        => SteamCredential.TryCreateSessionToken(
            "the-one-token", "webview", Now.AddHours(12), SteamId.FromAccountId(12345))!;

    private static SteamCredential ExpiredSession
        => SteamCredential.TryCreateSessionToken(
            "the-stale-token", "webview", Now.AddHours(-1), SteamId.FromAccountId(12345))!;

    [Theory]
    [InlineData(SteamCredentialPurpose.Unattended)]
    [InlineData(SteamCredentialPurpose.UserInitiated)]
    public void Neither_credential_returns_null(SteamCredentialPurpose purpose)
        => Assert.Null(SteamCredentialSelector.Choose(purpose, apiKey: null, session: null, Now));

    [Theory]
    [InlineData(SteamCredentialPurpose.Unattended)]
    [InlineData(SteamCredentialPurpose.UserInitiated)]
    public void Key_only_is_chosen_for_either_purpose(SteamCredentialPurpose purpose)
    {
        var chosen = SteamCredentialSelector.Choose(purpose, Key, session: null, Now);

        Assert.NotNull(chosen);
        Assert.Equal(SteamCredentialKind.ApiKey, chosen.Kind);
    }

    [Theory]
    [InlineData(SteamCredentialPurpose.Unattended)]
    [InlineData(SteamCredentialPurpose.UserInitiated)]
    public void Usable_session_only_is_chosen_for_either_purpose(SteamCredentialPurpose purpose)
    {
        var chosen = SteamCredentialSelector.Choose(purpose, apiKey: null, UsableSession, Now);

        Assert.NotNull(chosen);
        Assert.Equal(SteamCredentialKind.SessionToken, chosen.Kind);
    }

    [Fact]
    public void Unattended_prefers_the_key_when_both_exist()
    {
        var chosen = SteamCredentialSelector.Choose(
            SteamCredentialPurpose.Unattended, Key, UsableSession, Now);

        Assert.NotNull(chosen);
        Assert.Equal(SteamCredentialKind.ApiKey, chosen.Kind);
    }

    [Fact]
    public void User_initiated_prefers_the_session_when_both_exist()
    {
        var chosen = SteamCredentialSelector.Choose(
            SteamCredentialPurpose.UserInitiated, Key, UsableSession, Now);

        Assert.NotNull(chosen);
        Assert.Equal(SteamCredentialKind.SessionToken, chosen.Kind);
    }

    [Theory]
    [InlineData(SteamCredentialPurpose.Unattended)]
    [InlineData(SteamCredentialPurpose.UserInitiated)]
    public void An_expired_session_is_chosen_for_neither_purpose(SteamCredentialPurpose purpose)
        => Assert.Null(SteamCredentialSelector.Choose(purpose, apiKey: null, ExpiredSession, Now));

    [Theory]
    [InlineData(SteamCredentialPurpose.Unattended)]
    [InlineData(SteamCredentialPurpose.UserInitiated)]
    public void An_expired_session_falls_through_to_the_key(SteamCredentialPurpose purpose)
    {
        var chosen = SteamCredentialSelector.Choose(purpose, Key, ExpiredSession, Now);

        Assert.NotNull(chosen);
        Assert.Equal(SteamCredentialKind.ApiKey, chosen.Kind);
    }

    /// <summary>
    /// A token that outlives <c>now</c> by less than the skew is treated as
    /// expired, because it would very likely die between the moment it is
    /// chosen and the moment the request reaches Valve.
    /// </summary>
    [Fact]
    public void A_session_inside_the_skew_window_counts_as_expired()
    {
        var expiringNow = SteamCredential.TryCreateSessionToken(
            "about-to-die", "webview", Now.AddSeconds(30), steamId: null)!;

        Assert.Null(SteamCredentialSelector.Choose(
            SteamCredentialPurpose.UserInitiated, apiKey: null, expiringNow, Now));

        // With no skew allowance it is still alive, which pins that the rejection
        // above is the skew and not an off-by-one on the comparison itself.
        Assert.NotNull(SteamCredentialSelector.Choose(
            SteamCredentialPurpose.UserInitiated, apiKey: null, expiringNow, Now, TimeSpan.Zero));
    }

    /// <summary>An API key has no expiry, so it is usable at any instant.</summary>
    [Fact]
    public void An_api_key_never_expires()
    {
        Assert.Null(Key.ExpiresAt);
        Assert.True(Key.IsUsableAt(DateTimeOffset.MaxValue, TimeSpan.Zero));
    }
}
