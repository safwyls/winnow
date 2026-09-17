using System.Text;
using Winnow.Plugin.Xbox;
using Xunit;

namespace Winnow.Plugin.Xbox.Tests;

public sealed class XboxLocalWindowsLibraryTests : IDisposable
{
    private const string Family = "Microsoft.MicrosoftSolitaireCollection_8wekyb3d8bbwe";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "winnow-xbox-test-" + Guid.NewGuid().ToString("N"));

    public XboxLocalWindowsLibraryTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public async Task SuccessfulSnapshotImportsGameWithoutChangingPackageFiles()
    {
        CreatePackage();
        var before = Directory.GetFiles(_root).ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes);
        var scan = await Library([new(Family, _root, true)]).ScanAsync();
        Assert.NotNull(scan);
        Assert.True(scan.IsComplete);
        Assert.Single(scan.Games);
        foreach (var file in Directory.GetFiles(_root)) Assert.Equal(before[Path.GetFileName(file)], File.ReadAllBytes(file));
    }

    [Fact]
    public async Task MissingOrUnreadablePackagePreventsAbsenceAuthorityButPreservesPositiveFacts()
    {
        CreatePackage();
        var scan = await Library([new(Family, _root, true), new("Other_123456789abcd", Path.Combine(_root, "missing"), true)]).ScanAsync();
        Assert.NotNull(scan);
        Assert.False(scan.IsComplete);
        Assert.Single(scan.Games);
    }

    [Fact]
    public async Task UnhealthyOrDuplicateRegistrationDoesNotEstablishAbsence()
    {
        CreatePackage();
        var scan = await Library([new(Family, _root, true), new(Family, _root, true), new("Other_123456789abcd", _root, false)]).ScanAsync();
        Assert.NotNull(scan);
        Assert.False(scan.IsComplete);
        Assert.Single(scan.Games);
    }

    [Fact]
    public async Task EmptyInventoryIsAuthoritativeWhileUnavailableInventoryIsNot()
    {
        var empty = await Library([]).ScanAsync();
        Assert.NotNull(empty);
        Assert.True(empty.IsComplete);
        Assert.Empty(empty.Games);
        Assert.Null(await Library(null).ScanAsync());
    }

    [Fact]
    public async Task ChangingRegistrationDuringScanWithholdsAbsenceAuthority()
    {
        CreatePackage();
        var calls = 0;
        var library = new WindowsXboxLocalLibrary(_ => Task.FromResult<IReadOnlyList<XboxLocalRegistration>?>(
            ++calls == 1 ? [new(Family, _root, true)] : []));
        var scan = await library.ScanAsync();
        Assert.NotNull(scan);
        Assert.False(scan.IsComplete);
        Assert.Single(scan.Games);
    }

    [Fact]
    public async Task MalformedOptionalGameEvidencePreventsFalseUninstall()
    {
        CreatePackage();
        File.WriteAllText(Path.Combine(_root, "xboxservices.config"), "{");
        var scan = await Library([new(Family, _root, true)]).ScanAsync();
        Assert.NotNull(scan);
        Assert.False(scan.IsComplete);
        Assert.Empty(scan.Games);
    }

    [Fact]
    public async Task UnclassifiedPackagesRemainAvailableForExactHistoryJoins()
    {
        File.WriteAllBytes(Path.Combine(_root, "AppxManifest.xml"), XboxLocalPackageParserTests.Fixture("solitaire-appx.xml"));
        var scan = await Library([new(Family, _root, true)]).ScanAsync();
        Assert.NotNull(scan);
        Assert.True(scan.IsComplete);
        Assert.Empty(scan.Games);
        Assert.Single(scan.Packages);
    }

    [Fact]
    public async Task ScanResolvesCopiedManifestReferencesUsingRegisteredPackageIdentity()
    {
        CreatePackage();
        var manifest = File.ReadAllText(Path.Combine(_root, "AppxManifest.xml"))
            .Replace("Solitaire &amp; Casual Games", "ms-resource:GameTitle", StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(_root, "AppxManifest.xml"), manifest);
        const string fullName = "Microsoft.MicrosoftSolitaireCollection_4.26.7290.0_x64__8wekyb3d8bbwe";
        var library = new WindowsXboxLocalLibrary(_ => Task.FromResult<IReadOnlyList<XboxLocalRegistration>?>(
            [new(Family, _root, true) { PackageFullName = fullName }]), resolveResource: (package, family, resource) =>
            {
                Assert.Equal(fullName, package);
                Assert.Equal(Family, family);
                return resource == "ms-resource:GameTitle" ? "Localized Solitaire" : null;
            });
        var scan = await library.ScanAsync();
        Assert.NotNull(scan);
        var game = Assert.Single(scan.Games);
        Assert.Equal("Localized Solitaire", game.Title);
        Assert.False(game.IsTitleProvisional);
        Assert.Equal(manifest, File.ReadAllText(Path.Combine(_root, "AppxManifest.xml")));
    }

    [Fact]
    public async Task LaunchRequiresFreshMatchingRegistrationAndRejectsRemovedGame()
    {
        CreatePackage();
        string? activated = null;
        var library = Library([new(Family, _root, true)], aumid => { activated = aumid; return true; });
        Assert.True(await library.LaunchAsync(Family, "App"));
        Assert.Equal(Family + "!App", activated);
        activated = null;
        Assert.False(await library.LaunchAsync(Family, "Other"));
        Assert.False(await library.LaunchAsync(Family, "App;calc"));
        File.Delete(Path.Combine(_root, "AppxManifest.xml"));
        Assert.False(await library.LaunchAsync(Family, "App"));
        Assert.Null(activated);
    }

    [Fact]
    public async Task CallerCancellationStopsBeforeDiscoveryOrActivation()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var library = new WindowsXboxLocalLibrary(_ => throw new InvalidOperationException("Must not enumerate"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => library.ScanAsync(cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => library.LaunchAsync(Family, "App", cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => library.OpenStoreAsync("9NBLGGH4R315", cancelled.Token));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[{\"PackageFamilyName\":\"x\"}]")]
    [InlineData("[{\"PackageFamilyName\":\"x\",\"InstallLocation\":\"x\",\"Healthy\":\"true\"}]")]
    public void MalformedInventoryCannotBecomeEmptySuccess(string json)
        => Assert.Throws<FormatException>(() => WindowsXboxLocalLibrary.ParseInventory(json));

    [Fact]
    public void InventoryDistinguishesHealthFromPresence()
    {
        var registrations = WindowsXboxLocalLibrary.ParseInventory("""
            [{"PackageFamilyName":"Example_123456789abcd","InstallLocation":"C:\\XboxGames\\Example","Healthy":false}]
            """);
        Assert.False(Assert.Single(registrations).Healthy);
    }

    private WindowsXboxLocalLibrary Library(IReadOnlyList<XboxLocalRegistration>? registrations, Func<string, bool>? activate = null)
        => new(_ => Task.FromResult(registrations), activate: activate);

    private void CreatePackage()
    {
        File.WriteAllBytes(Path.Combine(_root, "AppxManifest.xml"), XboxLocalPackageParserTests.Fixture("solitaire-appx.xml"));
        File.WriteAllBytes(Path.Combine(_root, "xboxservices.config"), XboxLocalPackageParserTests.Fixture("solitaire-xboxservices.json"));
    }
}
