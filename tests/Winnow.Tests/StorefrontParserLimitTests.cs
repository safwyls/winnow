using System.Text;
using Winnow.Core.Ingest;
using Winnow.Ingest.Epic;
using Winnow.Ingest.Gog;
using Winnow.Ingest.Steam;
using Xunit;

namespace Winnow.Tests;

public sealed class StorefrontParserLimitTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"winnow-parser-{Guid.NewGuid():N}");

    public StorefrontParserLimitTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void Vdf_rejects_oversize_fixture_and_accepts_exact_byte_boundary()
    {
        var path = SteamFixtures.PathOf("appmanifest-oversize.acf");
        var length = checked((int)new FileInfo(path).Length);
        Assert.NotNull(new AppManifestReader(limits: new() { MaxFileBytes = length }).Read(path));
        Assert.Null(new AppManifestReader(limits: new() { MaxFileBytes = length - 1 }).Read(path));
    }

    [Fact]
    public void Vdf_rejects_deep_fixture_before_recursive_deserialization()
    {
        var path = SteamFixtures.PathOf("appmanifest-deep.acf");
        Assert.Null(new AppManifestReader().Read(path));
        Assert.NotNull(new AppManifestReader(limits: new() { MaxDepth = 80 }).Read(path));
        Assert.Empty(new LocalConfigReader().Read(path));
        Assert.Empty(new LibraryFoldersReader().Read(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Depth_guard_ignores_comments_quoted_braces_and_escaped_quotes(bool utf16)
    {
        var text = File.ReadAllText(SteamFixtures.PathOf("appmanifest_1244090.acf"));
        var braces = new string('{', 100);
        text = text.Replace("\"AppState\"", $"// {braces}\n\"AppState\"");
        text = text.Insert(text.LastIndexOf('}'), $"\"extra\" \"escaped \\\" {braces} \\\\\"\n");
        var path = Path.Combine(_root, "quoted.acf");
        File.WriteAllText(path, text, utf16 ? Encoding.Unicode : Encoding.UTF8);
        Assert.NotNull(new AppManifestReader().Read(path));
    }

    [Theory]
    [InlineData("#include")]
    [InlineData("#base")]
    public void Vdf_cannot_expand_external_files(string directive)
    {
        var path = Path.Combine(_root, "include.acf");
        var external = Path.Combine(_root, "external.acf");
        File.Copy(SteamFixtures.PathOf("appmanifest_1244090.acf"), external);
        File.WriteAllText(path, $"{directive} \"{external.Replace("\\", "\\\\")}\"\n\"AppState\" {{ \"appid\" \"1\" }}");
        Assert.Null(new AppManifestReader().Read(path));
    }

    [Theory]
    [InlineData("epic", "A47587CE819533CC1BDD688E306742B3.item")]
    [InlineData("gog", "goggame-1971477531.info")]
    [InlineData("thirdparty", "ThirPartyManagedApps/ecebf45065bc4993abfe0e84c40ff18e_6dc445f656de4e029834b2d32b6a2f77_Jasper.json")]
    public void Json_readers_enforce_bytes_and_depth_on_captured_fixtures(string kind, string file)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", kind == "gog" ? "gog" : "epic", file);
        var length = checked((int)new FileInfo(path).Length);
        Assert.NotNull(Read(kind, path, new() { MaxFileBytes = length }));
        Assert.Null(Read(kind, path, new() { MaxFileBytes = length - 1 }));
        var text = File.ReadAllText(path);
        var nested = string.Concat(Enumerable.Repeat("{\"nested\":", 70)) + "0" + new string('}', 70);
        text = text.Insert(text.LastIndexOf('}'), $",\"depth_test\":{nested}");
        var deep = Path.Combine(_root, "deep.json");
        File.WriteAllText(deep, text);
        Assert.Null(Read(kind, deep, new()));
        Assert.NotNull(Read(kind, deep, new() { MaxDepth = 80 }));
    }

    [Fact]
    public void Base64_catalog_checks_encoded_size_and_decoded_depth()
    {
        var path = EpicFixtureTree.PathOf("catcache.bin");
        var length = checked((int)new FileInfo(path).Length);
        Assert.NotEmpty(new EpicCatalogReader(limits: new() { MaxFileBytes = length }).Read(path));
        Assert.Empty(new EpicCatalogReader(limits: new() { MaxFileBytes = length - 1 }).Read(path));
        Assert.Empty(new EpicCatalogReader(limits: new() { MaxDepth = 1 }).Read(path));
    }

    [Fact]
    public void Fingerprint_and_galaxy_config_apply_limits()
    {
        var manifests = Path.Combine(_root, "Manifests");
        Directory.CreateDirectory(manifests);
        File.Copy(EpicFixtureTree.PathOf(EpicFixtureTree.FezManifest), Path.Combine(manifests, "game.item"));
        Assert.NotNull(new EpicManifestStateReader(_root).ReadFingerprint());
        Assert.Null(new EpicManifestStateReader(_root, new() { MaxFileBytes = 1 }).ReadFingerprint());
        var path = Path.Combine(_root, "config.json");
        File.WriteAllText(path, "{\"storagePath\":\"storage\",\"extra\":{\"nested\":1}}");
        Assert.Equal("storage", GogPaths.ReadStoragePath(path));
        Assert.Null(GogPaths.ReadStoragePath(path, new() { MaxFileBytes = 1 }));
        Assert.Null(GogPaths.ReadStoragePath(path, new() { MaxDepth = 1 }));
    }

    private static object? Read(string kind, string path, StorefrontParserLimits limits) => kind switch
    {
        "epic" => new EpicManifestReader(limits: limits).Read(path),
        "gog" => new GogGameInfoReader(limits: limits).Read(path),
        _ => new EpicThirdPartyAppReader(limits: limits).Read(path),
    };
}
