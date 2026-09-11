using System.IO.Compression;
using System.Text.Json;
using Winnow.PluginSdk;
using Xunit;

namespace Winnow.Plugins.Tests;

public sealed class PluginArchiveInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "winnow-archive-tests-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _issues = [];
    private static readonly PluginManifest Manifest = new()
    {
        Id = "archive-fixture", Name = "Archive fixture", Version = "1.0.0", EntryAssembly = "fixture.dll",
        EntryType = "Fixture.Plugin", Capabilities = [PluginCapabilities.Artwork]
    };

    public PluginArchiveInstallerTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("")]
    [InlineData("package/")]
    public async Task Installs_root_or_single_folder_layout_and_retains_original_zip(string prefix)
    {
        var zip = Package(prefix);
        var original = await File.ReadAllBytesAsync(zip);

        var installed = await Install(zip);

        Assert.Equal(Path.Combine(_root, Manifest.Id), installed);
        Assert.Equal("dll", await File.ReadAllTextAsync(Path.Combine(installed!, "fixture.dll")));
        Assert.Equal("asset", await File.ReadAllTextAsync(Path.Combine(installed!, "assets", "nested.txt")));
        Assert.Empty(_issues);
        Assert.False(File.Exists(zip));
        Assert.Equal(original, await File.ReadAllBytesAsync(Assert.Single(Directory.GetFiles(Path.Combine(_root, ".archives")))));
        Assert.Empty(Directory.GetDirectories(_root, ".unpack-*"));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("/outside.txt")]
    [InlineData("C:/outside.txt")]
    [InlineData("assets\\outside.txt")]
    [InlineData("assets/../../outside.txt")]
    [InlineData("assets//outside.txt")]
    [InlineData("assets/./outside.txt")]
    [InlineData("assets/CON.txt")]
    [InlineData("assets/LPT1")]
    [InlineData("assets/COM¹.txt")]
    [InlineData("assets/file.")]
    [InlineData("assets/file ")]
    [InlineData("assets/file:stream")]
    [InlineData("assets/fi?le")]
    public async Task Rejects_unsafe_portable_paths(string name)
    {
        var zip = Package();
        Add(zip, name, "unsafe");
        await AssertRejected(zip);
    }

    [Theory]
    [InlineData("fixture.dll")]
    [InlineData("FIXTURE.dll")]
    [InlineData("ASSETS/other.txt")]
    [InlineData("assets")]
    [InlineData("fixture.dll/child")]
    public async Task Rejects_duplicate_case_colliding_and_file_directory_conflicts(string name)
    {
        var zip = Package();
        Add(zip, name, "conflict");
        await AssertRejected(zip);
    }

    [Theory]
    [InlineData(0xA0000000)]
    [InlineData(0x00000400)]
    [InlineData(0x10000000)]
    public async Task Rejects_links_and_special_entries(uint attributes)
    {
        var zip = Package();
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update))
            archive.CreateEntry("link").ExternalAttributes = unchecked((int)attributes);
        await AssertRejected(zip);
    }

    [Fact]
    public async Task Rejects_multiple_packages_and_files_outside_wrapper()
    {
        var zip = Package("first/");
        Add(zip, "second/plugin.json", JsonSerializer.Serialize(Manifest));
        await AssertRejected(zip);
        File.Delete(zip);
        zip = Package("first/");
        Add(zip, "readme.txt", "outside");
        await AssertRejected(zip);
    }

    [Fact]
    public async Task Rejects_deeper_wrapping()
    {
        await AssertRejected(Package("first/second/"));
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("incompatible")]
    [InlineData("missing-dll")]
    [InlineData("missing-manifest")]
    [InlineData("reserved-id")]
    public async Task Rejects_invalid_packages(string kind)
    {
        var zip = Package();
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update))
        {
            if (kind == "missing-dll") archive.GetEntry("fixture.dll")!.Delete();
            else
            {
                archive.GetEntry("plugin.json")!.Delete();
                if (kind != "missing-manifest")
                {
                    using var writer = new StreamWriter(archive.CreateEntry("plugin.json").Open());
                    writer.Write(kind == "malformed" ? "{" : JsonSerializer.Serialize(kind == "incompatible"
                        ? Manifest with { ApiVersion = 99 } : Manifest with { Id = "con" }));
                }
            }
        }
        await AssertRejected(zip);
    }

    [Fact]
    public async Task Refuses_existing_plugin_id_even_when_directory_has_another_name()
    {
        var zip = Package();
        Assert.Null(await Install(zip, new HashSet<string> { Manifest.Id.ToUpperInvariant() }));
        Assert.Single(_issues);
        Assert.True(File.Exists(zip));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Refuses_existing_destination_file_or_directory(bool directory)
    {
        var existing = Path.Combine(_root, Manifest.Id.ToUpperInvariant());
        if (directory) Directory.CreateDirectory(existing);
        else await File.WriteAllTextAsync(existing, "keep");
        var zip = Package();
        Assert.Null(await Install(zip));
        Assert.True(Path.Exists(existing));
        if (!directory) Assert.Equal("keep", await File.ReadAllTextAsync(existing));
        Assert.Single(_issues);
        Assert.True(File.Exists(zip));
    }

    [Fact]
    public async Task Archive_retention_failure_still_returns_installed_directory()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, ".archives"), "blocking file");
        var zip = Package();
        var result = await Install(zip);
        Assert.NotNull(result);
        Assert.True(File.Exists(Path.Combine(result, "fixture.dll")));
        Assert.True(File.Exists(zip));
        Assert.Contains("was installed", Assert.Single(_issues));
    }

    [Fact]
    public async Task Rejects_invalid_zip_without_modifying_it()
    {
        var zip = Path.Combine(_root, "broken.zip");
        await File.WriteAllTextAsync(zip, "not a zip");
        await AssertRejected(zip);
        Assert.Equal("not a zip", await File.ReadAllTextAsync(zip));
    }

    [Fact]
    public async Task External_exception_details_are_not_exposed_in_issues()
    {
        var missing = Path.Combine(_root, "private-account-token.zip");
        Assert.Null(await Install(missing));
        var issue = Assert.Single(_issues);
        Assert.DoesNotContain("private-account-token", issue);
        Assert.DoesNotContain(_root, issue);
        Assert.Contains("valid, compatible plugin package", issue);
    }

    [Fact]
    public async Task Cancellation_propagates_and_preserves_zip()
    {
        var zip = Package();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PluginArchiveInstaller.TryInstallAsync(
            zip, _root, new HashSet<string>(), (_, message) => _issues.Add(message), cancellation.Token));
        Assert.True(File.Exists(zip));
        Assert.Empty(_issues);
        Assert.Empty(Directory.GetDirectories(_root));
    }

    [Fact]
    public async Task Cancellation_during_extraction_removes_staging_and_preserves_zip()
    {
        var zip = Package();
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update))
        {
            using var content = archive.CreateEntry("large-asset.bin").Open();
            content.Write(new byte[16 * 1024 * 1024]);
        }
        using var cancellation = new CancellationTokenSource();
        var install = PluginArchiveInstaller.TryInstallAsync(zip, _root, new HashSet<string>(),
            (_, message) => _issues.Add(message), cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => install);
        Assert.True(File.Exists(zip));
        Assert.Empty(_issues);
        Assert.Empty(Directory.GetDirectories(_root));
    }

    [Fact]
    public async Task Rejects_excessive_entry_count()
    {
        var zip = Package();
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update))
            for (var i = 0; i < PluginArchiveInstaller.MaximumEntries; i++) archive.CreateEntry("empty-" + i);
        await AssertRejected(zip);
    }

    [Theory]
    [InlineData(30000)]
    [InlineData(32)]
    public async Task Rejects_deep_paths_before_building_directory_prefixes(int depth)
    {
        var zip = Package();
        Add(zip, string.Concat(Enumerable.Repeat("a/", depth)) + "payload", "");
        await AssertRejected(zip);
        Assert.Contains("32 components", Assert.Single(_issues));
    }

    [Fact]
    public async Task Rejects_long_paths_even_with_shallow_depth()
    {
        var zip = Package();
        Add(zip, string.Join('/', Enumerable.Repeat(new string('a', 200), 6)), "");
        await AssertRejected(zip);
        Assert.Contains("1024 characters", Assert.Single(_issues));
    }

    [Fact]
    public async Task Rejects_excessive_compressed_file_size_before_opening_zip()
    {
        var zip = Path.Combine(_root, "oversized.zip");
        using (var stream = File.Create(zip)) stream.SetLength(PluginArchiveInstaller.MaximumArchiveBytes + 1);
        await AssertRejected(zip);
        Assert.Contains("256 MiB", Assert.Single(_issues));
    }

    [Fact]
    public async Task Rejects_excessive_declared_uncompressed_size_before_extracting()
    {
        var zip = Package();
        var bytes = await File.ReadAllBytesAsync(zip);
        var central = FindCentralDirectory(bytes);
        BitConverter.GetBytes((uint)(PluginArchiveInstaller.MaximumUncompressedBytes + 1)).CopyTo(bytes, central + 24);
        await File.WriteAllBytesAsync(zip, bytes);
        await AssertRejected(zip);
        Assert.Contains("unpacked size", Assert.Single(_issues));
    }

    private static int FindCentralDirectory(byte[] bytes)
    {
        for (var i = 0; i <= bytes.Length - 4; i++)
            if (BitConverter.ToUInt32(bytes, i) == 0x02014b50) return i;
        throw new InvalidOperationException("Missing ZIP central directory.");
    }

    private string Package(string prefix = "")
    {
        var zip = Path.Combine(_root, "package.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            if (prefix.Length > 0) archive.CreateEntry(prefix);
        Add(zip, prefix + "plugin.json", JsonSerializer.Serialize(Manifest));
        Add(zip, prefix + "fixture.dll", "dll");
        Add(zip, prefix + "assets/nested.txt", "asset");
        return zip;
    }

    private static void Add(string zip, string path, string content)
    {
        using var archive = ZipFile.Open(zip, ZipArchiveMode.Update);
        using var writer = new StreamWriter(archive.CreateEntry(path).Open());
        writer.Write(content);
    }

    private Task<string?> Install(string zip, IReadOnlySet<string>? ids = null) =>
        PluginArchiveInstaller.TryInstallAsync(zip, _root, ids ?? new HashSet<string>(), (_, message) => _issues.Add(message));

    private async Task AssertRejected(string zip)
    {
        Assert.Null(await Install(zip));
        Assert.NotEmpty(_issues);
        Assert.True(File.Exists(zip));
        Assert.False(Directory.Exists(Path.Combine(_root, Manifest.Id)));
        Assert.Empty(Directory.GetDirectories(_root, ".unpack-*"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
