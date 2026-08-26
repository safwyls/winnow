using Hoard.Ingest.Epic;

namespace Hoard.Tests;

/// <summary>
/// Materialises the flat sanitized Epic fixtures (tests/fixtures/epic/) into the
/// directory shape the launcher really uses, so a source test walks the same tree
/// layout as production: <c>Data\Manifests\*.item</c>,
/// <c>Data\Catalog\catcache.bin</c>, <c>Data\ThirPartyManagedApps\*.json</c>.
///
/// <para>The tree is built under a fresh temp directory each time and the caller
/// disposes it. Nothing is ever written back into <c>tests/fixtures</c>.</para>
/// </summary>
internal sealed class EpicFixtureTree : IDisposable
{
    private EpicFixtureTree(string root) => DataRoot = root;

    /// <summary>The launcher <c>Data</c> root to hand to <see cref="EpicLibrarySource.Scan(string?)"/>.</summary>
    internal string DataRoot { get; }

    /// <summary>Fez, installed and complete.</summary>
    internal const string FezManifest = "A47587CE819533CC1BDD688E306742B3.item";

    /// <summary>An installed DLC — <c>MainGame*</c> non-empty.</summary>
    internal const string DlcManifest = "B1000000000000000000000000000002.item";

    /// <summary>A part-downloaded install — <c>bIsIncompleteInstall</c> set.</summary>
    internal const string IncompleteManifest = "C2000000000000000000000000000003.item";

    /// <summary>Path of a fixture file as copied to the test output directory.</summary>
    internal static string PathOf(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "fixtures", "epic", fileName);

    /// <summary>
    /// Builds a launcher tree. Pass the manifest file names to include so a test
    /// can model "nothing installed" or "one incomplete install" precisely.
    /// </summary>
    /// <param name="manifests">Fixture <c>.item</c> file names to place under <c>Manifests\</c>.</param>
    /// <param name="includeCatalog">Whether to place <c>catcache.bin</c> under <c>Catalog\</c>.</param>
    /// <param name="includeThirdParty">Whether to place the <c>ThirPartyManagedApps</c> directory.</param>
    internal static EpicFixtureTree Create(
        IEnumerable<string>? manifests = null,
        bool includeCatalog = true,
        bool includeThirdParty = true)
    {
        var root = Path.Combine(Path.GetTempPath(), "hoard-epic-fixture-" + Guid.NewGuid().ToString("N"));
        var dataRoot = Path.Combine(root, "Data");

        var manifestsDirectory = Path.Combine(dataRoot, EpicPaths.ManifestsDirectoryName);
        Directory.CreateDirectory(manifestsDirectory);
        foreach (var manifest in manifests ?? [FezManifest])
        {
            File.Copy(PathOf(manifest), Path.Combine(manifestsDirectory, manifest));
        }

        // The launcher keeps an empty Pending\ sibling; reproduce it so the
        // reader is exercised against a directory that has one.
        Directory.CreateDirectory(Path.Combine(manifestsDirectory, "Pending"));

        if (includeCatalog)
        {
            var catalogDirectory = Path.Combine(dataRoot, EpicPaths.CatalogDirectoryName);
            Directory.CreateDirectory(catalogDirectory);
            File.Copy(
                PathOf(EpicPaths.CatalogCacheFileName),
                Path.Combine(catalogDirectory, EpicPaths.CatalogCacheFileName));
        }

        if (includeThirdParty)
        {
            var source = PathOf(EpicPaths.ThirdPartyManagedAppsDirectoryName);
            var destination = Path.Combine(dataRoot, EpicPaths.ThirdPartyManagedAppsDirectoryName);
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.EnumerateFiles(source))
            {
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            }
        }

        return new EpicFixtureTree(dataRoot);
    }

    public void Dispose()
    {
        var root = Path.GetDirectoryName(DataRoot);
        try
        {
            if (root is not null && Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory must never fail a test run.
        }
    }
}

/// <summary>
/// A third-party install probe with a scripted answer, so tests never touch the
/// machine's real registry (where Ubisoft Connect may or may not be installed).
/// </summary>
internal sealed class FakeEpicThirdPartyInstallProbe : IEpicThirdPartyInstallProbe
{
    private readonly EpicInstallState _answer;

    internal FakeEpicThirdPartyInstallProbe(EpicInstallState answer) => _answer = answer;

    /// <summary>Registry pointers this probe was asked about, in call order.</summary>
    internal List<(string Path, string Value)> Calls { get; } = [];

    public EpicInstallState Probe(string registryPath, string registryValueName)
    {
        Calls.Add((registryPath, registryValueName));
        return _answer;
    }
}
