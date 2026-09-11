using Winnow.PluginSdk;
using Winnow.Plugins;
using Xunit;

namespace Winnow.Plugins.Tests;

public sealed class PluginManifestTests
{
    private static PluginManifest Valid => new()
    {
        Id = "example.art", Name = "Example artwork", Version = "1.2.0", EntryAssembly = "Example.dll", EntryType = "Example.Art",
        Capabilities = [PluginCapabilities.Artwork], Network = new() { AllowedHosts = ["api.example.com"] },
    };

    [Theory]
    [InlineData("../outside.dll")]
    [InlineData("nested/plugin.dll")]
    [InlineData("C:\\plugin.dll")]
    [InlineData("..\\outside.dll")]
    public void Entry_assembly_must_be_a_package_local_filename(string assembly) =>
        Assert.Throws<InvalidDataException>(() => PluginManifestReader.Validate(Valid with { EntryAssembly = assembly }));

    [Theory]
    [InlineData("../other")]
    [InlineData("UPPER")]
    [InlineData("a/b")]
    [InlineData("")]
    public void Id_cannot_escape_scoped_storage(string id) =>
        Assert.Throws<InvalidDataException>(() => PluginManifestReader.Validate(Valid with { Id = id }));

    [Theory]
    [InlineData("*.example.com")]
    [InlineData("https://api.example.com")]
    [InlineData("api.example.com:443")]
    [InlineData("127.0.0.1")]
    [InlineData("api.example.com.")]
    public void Host_entries_are_explicit_dns_names(string host) =>
        Assert.Throws<InvalidDataException>(() => PluginManifestReader.Validate(Valid with
        { Network = new() { AllowedHosts = [host] } }));

    [Fact]
    public void Compatibility_capability_settings_and_network_limits_are_checked_before_loading()
    {
        PluginManifestReader.Validate(Valid);
        Assert.Throws<InvalidDataException>(() => PluginManifestReader.Validate(Valid with { ApiVersion = 2 }));
        Assert.Throws<InvalidDataException>(() => PluginManifestReader.Validate(Valid with { Capabilities = ["custom-ui"] }));
        Assert.Throws<InvalidDataException>(() => PluginManifestReader.Validate(Valid with { Capabilities = ["artwork", "artwork"] }));
        Assert.Throws<InvalidDataException>(() => PluginManifestReader.Validate(Valid with
        { Settings = [new() { Key = "key", Label = "API key" }, new() { Key = "key", Label = "Duplicate" }] }));
        Assert.Throws<InvalidDataException>(() => PluginManifestReader.Validate(Valid with
        { Settings = [new() { Key = "key", Label = "API key", SetupUrl = "file:///private" }] }));
        Assert.Throws<InvalidDataException>(() => PluginManifestReader.Validate(Valid with
        { Network = new() { RequestsPerSecond = double.PositiveInfinity } }));
        Assert.Throws<InvalidDataException>(() => PluginManifestReader.Validate(Valid with
        { Network = new() { MaxResponseBytes = int.MaxValue } }));
        Assert.Throws<InvalidDataException>(() => PluginManifestReader.Validate(Valid with
        { Network = new() { MaxRetries = 3 } }));
    }

    [Fact]
    public async Task Oversized_manifest_is_rejected_before_deserialization()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, new string(' ', PluginManifestReader.MaximumManifestBytes + 1));
            await Assert.ThrowsAsync<InvalidDataException>(() => PluginManifestReader.ReadAsync(path));
        }
        finally { File.Delete(path); }
    }
}
