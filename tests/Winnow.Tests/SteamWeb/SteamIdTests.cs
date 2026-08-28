using Winnow.Enrich.SteamWeb;
using Xunit;

namespace Winnow.Tests.SteamWeb;

/// <summary>
/// The steam3 ↔ SteamID64 conversion, which is the whole reason this module
/// needs no <c>ResolveVanityURL</c> call to work out who the local user is.
/// </summary>
public class SteamIdTests
{
    [Fact]
    public void The_fixture_pairing_round_trips()
    {
        // tests/fixtures/steam/README.md documents this exact pair as the
        // sanitized identity of one account: steam3id 12345678 alongside
        // LastOwner 76561197972611406. If the constant were wrong these two
        // would not line up.
        var id = SteamId.FromAccountId(12345678);

        Assert.NotNull(id);
        Assert.Equal(76561197972611406UL, id.Value.Value);
        Assert.Equal("12345678", id.Value.AccountRef);
    }

    [Fact]
    public void The_base_is_the_documented_constant()
        => Assert.Equal(0x0110000100000000UL, SteamId.SteamId64Base);

    [Theory]
    [InlineData("12345678", 76561197972611406UL)]
    [InlineData("76561197972611406", 76561197972611406UL)]
    [InlineData("  12345678  ", 76561197972611406UL)]
    public void Either_form_parses_to_the_same_account(string text, ulong expected)
    {
        Assert.True(SteamId.TryParse(text, out var id));
        Assert.Equal(expected, id.Value);
    }

    [Fact]
    public void A_steamid64_survives_the_round_trip_back_to_a_steam3_folder_name()
    {
        Assert.True(SteamId.TryParse("76561197972611406", out var id));

        // AccountRef is what gets written to CandidateOwnership.AccountRef, so
        // a Web API candidate attributes to the same account as its local twin.
        Assert.Equal("12345678", id.AccountRef);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-number")]
    [InlineData("-1")]
    [InlineData("0")]                     // Steam's anonymous account: no library.
    [InlineData("76561197960265728")]     // the base itself is account id 0.
    [InlineData("99999999999999999999")]  // beyond ulong.
    public void Nonsense_is_rejected_rather_than_producing_a_wrong_account(string? text)
        => Assert.False(SteamId.TryParse(text, out _));

    [Fact]
    public void An_id_above_the_individual_range_is_rejected()
    {
        // Group, clan and gameserver ids live above the individual range. Adding
        // the base to one, or passing one through, would query a real but wrong
        // account rather than fail.
        Assert.Null(SteamId.FromSteamId64(SteamId.MaxIndividualSteamId64 + 1));
        Assert.NotNull(SteamId.FromSteamId64(SteamId.MaxIndividualSteamId64));
    }

    [Fact]
    public void Account_id_zero_is_rejected()
        => Assert.Null(SteamId.FromAccountId(0));
}
