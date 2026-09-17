using System.Globalization;
using System.Text;
using System.Xml;
using Winnow.Plugin.Xbox;
using Xunit;

namespace Winnow.Plugin.Xbox.Tests;

public sealed class XboxLocalPackageParserTests
{
    private const string SolitaireFamily = "Microsoft.MicrosoftSolitaireCollection_8wekyb3d8bbwe";
    private const string SampleFamily = "41336MicrosoftATG.Achievements2017Redux_123456789abcd";
    private static string Root => Path.Combine(Path.GetTempPath(), "xbox-parser-fixture");

    [Fact]
    public void CapturedSolitairePackageReadsGameAndDecimalTitleIdentity()
    {
        var parsed = XboxLocalPackageParser.Parse(SolitaireFamily, Root, Fixture("solitaire-appx.xml"),
            xboxServicesConfig: Fixture("solitaire-xboxservices.json"));

        Assert.NotNull(parsed);
        Assert.True(parsed.IsGame);
        Assert.Equal("85494077", parsed.Game.TitleId);
        Assert.Equal("Solitaire & Casual Games", parsed.Game.Title);
        Assert.False(parsed.Game.IsTitleProvisional);
        Assert.Equal(SolitaireFamily + "!App", parsed.Game.AppUserModelId);
        Assert.Equal(Path.Combine(Root, "Solitaire.exe"), parsed.Game.ExecutablePath);
        Assert.Null(parsed.Game.StoreId);
    }

    [Fact]
    public void MicrosoftGdkSampleUsesHexadecimalTitleIdWithoutConfusingStoreId()
    {
        var parsed = XboxLocalPackageParser.Parse(SampleFamily, Root, SampleManifest(), Fixture("achievements-microsoftgame.config"));
        Assert.NotNull(parsed);
        Assert.True(parsed.IsGame);
        Assert.Equal(uint.Parse("64353034", NumberStyles.HexNumber, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture), parsed.Game.TitleId);
        Assert.Equal(SampleFamily + "!Game", parsed.Game.AppUserModelId);
        Assert.Null(parsed.Game.StoreId);
    }

    [Fact]
    public void OrdinaryAppManifestAloneDoesNotAssertGameClassification()
    {
        var parsed = XboxLocalPackageParser.Parse(SolitaireFamily, Root, Fixture("solitaire-appx.xml"));
        Assert.NotNull(parsed);
        Assert.False(parsed.IsGame);
        Assert.Null(parsed.Game.TitleId);
    }

    [Fact]
    public void LocalizedApplicationDisplayNameTakesPriorityOverPackageLiteral()
    {
        var manifest = Encoding.UTF8.GetString(SampleManifest()).Replace(" /></Applications>",
            "><VisualElements DisplayName=\"ms-resource:GameTitle\" /></Application></Applications>", StringComparison.Ordinal);
        var parsed = XboxLocalPackageParser.Parse(SampleFamily, Root, Encoding.UTF8.GetBytes(manifest),
            resolveResource: resource => resource == "ms-resource:GameTitle" ? "Localized Game Title" : null);
        Assert.NotNull(parsed);
        Assert.Equal("Localized Game Title", parsed.Game.Title);
        Assert.False(parsed.Game.IsTitleProvisional);
    }

    [Fact]
    public void MissingResourcesMarkIdentityFallbackProvisional()
    {
        var manifest = Encoding.UTF8.GetString(SampleManifest()).Replace("ATG Achievements Sample", "ms-resource:GameTitle", StringComparison.Ordinal);
        var parsed = XboxLocalPackageParser.Parse(SampleFamily, Root, Encoding.UTF8.GetBytes(manifest), resolveResource: _ => null);
        Assert.NotNull(parsed);
        Assert.Equal("41336MicrosoftATG.Achievements2017Redux", parsed.Game.Title);
        Assert.True(parsed.Game.IsTitleProvisional);
    }

    [Fact]
    public void UnresolvedVisualResourceKeepsAvailableLiteralPackageTitle()
    {
        var parsed = XboxLocalPackageParser.Parse(SolitaireFamily, Root, Fixture("solitaire-appx.xml"), resolveResource: _ => null);
        Assert.NotNull(parsed);
        Assert.Equal("Solitaire & Casual Games", parsed.Game.Title);
        Assert.False(parsed.Game.IsTitleProvisional);
    }

    [Fact]
    public void GdkExecutableWithDefaultIdUsesExactManifestExecutableToFindRegisteredId()
    {
        var config = Encoding.UTF8.GetString(Fixture("achievements-microsoftgame.config")).Replace("Id=\"Game\"", "", StringComparison.Ordinal);
        var parsed = XboxLocalPackageParser.Parse(SampleFamily, Root, SampleManifest(), Encoding.UTF8.GetBytes(config));
        Assert.NotNull(parsed);
        Assert.Equal(SampleFamily + "!Game", parsed.Game.AppUserModelId);
    }

    [Fact]
    public void KnownGdkGameWithUnresolvedApplicationStaysInstalledWithoutGuessedActivation()
    {
        var manifest = Encoding.UTF8.GetString(SampleManifest()).Replace("Id=\"Game\"", "Id=\"Alternate\"", StringComparison.Ordinal);
        var parsed = XboxLocalPackageParser.Parse(SampleFamily, Root, Encoding.UTF8.GetBytes(manifest), Fixture("achievements-microsoftgame.config"));
        Assert.NotNull(parsed);
        Assert.True(parsed.IsGame);
        Assert.Null(parsed.Game.AppUserModelId);
    }

    [Theory]
    [InlineData("../Outside.exe")]
    [InlineData("..\\Outside.exe")]
    [InlineData("C:\\Outside.exe")]
    [InlineData("\\\\server\\share\\Outside.exe")]
    [InlineData("/Outside.exe")]
    [InlineData("inside.exe:payload")]
    [InlineData("bin/../../Outside.exe")]
    [InlineData("Game.exe|Other.exe")]
    public void RejectsUnsafeExecutablePaths(string executable)
    {
        Assert.Throws<FormatException>(() => XboxLocalPackageParser.Parse(SampleFamily, Root, SampleManifest(executable)));
    }

    [Fact]
    public void RejectsExternalEntityAndDeepXml()
    {
        var entity = "<!DOCTYPE Package [<!ENTITY local SYSTEM 'file:///unread'>]><Package>&local;</Package>";
        Assert.Throws<XmlException>(() => XboxLocalPackageParser.Parse(SampleFamily, Root, Encoding.UTF8.GetBytes(entity)));
        var deep = "<Package>" + string.Concat(Enumerable.Repeat("<Nested>", 70))
            + string.Concat(Enumerable.Repeat("</Nested>", 70)) + "</Package>";
        Assert.Throws<FormatException>(() => XboxLocalPackageParser.Parse(SampleFamily, Root, Encoding.UTF8.GetBytes(deep)));
        Assert.Throws<FormatException>(() => XboxLocalPackageParser.Parse(SampleFamily, Root, new byte[XboxLocalPackageParser.MaximumFileBytes + 1]));
    }

    [Fact]
    public void RejectsDuplicateAndConflictingIdentityFields()
    {
        var config = Encoding.UTF8.GetString(Fixture("achievements-microsoftgame.config"));
        Assert.Throws<FormatException>(() => XboxLocalPackageParser.Parse(SampleFamily, Root, SampleManifest(),
            Encoding.UTF8.GetBytes(config.Replace("</TitleId>", "</TitleId><TitleId>12345678</TitleId>", StringComparison.Ordinal))));
        Assert.Throws<FormatException>(() => XboxLocalPackageParser.Parse(SampleFamily, Root, SampleManifest(),
            Fixture("achievements-microsoftgame.config"), Fixture("solitaire-xboxservices.json")));
    }

    [Theory]
    [InlineData("FFFFFFFF")]
    [InlineData("00000000")]
    [InlineData("6435303")]
    [InlineData("not-a-title")]
    public void RejectsSentinelOrMalformedGdkTitleId(string value)
    {
        var config = Encoding.UTF8.GetString(Fixture("achievements-microsoftgame.config")).Replace("64353034", value, StringComparison.Ordinal);
        Assert.Throws<FormatException>(() => XboxLocalPackageParser.Parse(SampleFamily, Root, SampleManifest(), Encoding.UTF8.GetBytes(config)));
    }

    [Fact]
    public void DlcAndDeveloperOnlyConfigurationsAreNotPlayableGames()
    {
        var config = Encoding.UTF8.GetString(Fixture("achievements-microsoftgame.config"));
        var dlc = config.Replace("</Game>", "<TargetDeviceFamilyForDLC>PC</TargetDeviceFamilyForDLC></Game>", StringComparison.Ordinal);
        Assert.Null(XboxLocalPackageParser.Parse(SampleFamily, Root, SampleManifest(), Encoding.UTF8.GetBytes(dlc)));
        var devOnly = config.Replace("Id=\"Game\"", "Id=\"Game\" IsDevOnly=\"true\"", StringComparison.Ordinal);
        Assert.Null(XboxLocalPackageParser.Parse(SampleFamily, Root, SampleManifest(), Encoding.UTF8.GetBytes(devOnly)));
    }

    [Fact]
    public void MultipleVisibleApplicationsHaveNoGuessedLaunchTarget()
    {
        var manifest = Encoding.UTF8.GetString(SampleManifest()).Replace("</Applications>",
            "<Application Id=\"Other\" Executable=\"Other.exe\" /></Applications>", StringComparison.Ordinal);
        var parsed = XboxLocalPackageParser.Parse(SampleFamily, Root, Encoding.UTF8.GetBytes(manifest));
        Assert.NotNull(parsed);
        Assert.Null(parsed.Game.AppUserModelId);
        Assert.Null(parsed.Game.ExecutablePath);
    }

    [Fact]
    public void HiddenEntryDoesNotCompeteWithGameLaunchTarget()
    {
        var manifest = Encoding.UTF8.GetString(SampleManifest()).Replace("</Applications>",
            "<Application Id=\"Helper\" Executable=\"Helper.exe\"><VisualElements AppListEntry=\"none\" /></Application></Applications>", StringComparison.Ordinal);
        var parsed = XboxLocalPackageParser.Parse(SampleFamily, Root, Encoding.UTF8.GetBytes(manifest));
        Assert.NotNull(parsed);
        Assert.Equal(SampleFamily + "!Game", parsed.Game.AppUserModelId);
    }

    [Theory]
    [InlineData("A_123456789abcd", "Game", true)]
    [InlineData("A_123456789abcd", "Game.Main", true)]
    [InlineData("A_123456789abcd", "Game;calc", false)]
    [InlineData("A_123456789abcd", "Game..Main", false)]
    [InlineData("A_123456789abcd!Other", "Game", false)]
    [InlineData("../A_123456789abcd", "Game", false)]
    public void ActivationIdentityGrammarRejectsCommandText(string family, string app, bool valid)
        => Assert.Equal(valid, XboxLocalPackageParser.IsPackageFamilyName(family) && XboxLocalPackageParser.IsApplicationId(app));

    internal static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "xbox-local", name));

    private static byte[] SampleManifest(string executable = "Achievements.exe") => Encoding.UTF8.GetBytes($"""
        <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
          <Identity Name="41336MicrosoftATG.Achievements2017Redux" />
          <Properties><DisplayName>ATG Achievements Sample</DisplayName></Properties>
          <Applications><Application Id="Game" Executable="{System.Security.SecurityElement.Escape(executable)}" /></Applications>
        </Package>
        """);
}
