using System.Text.Json;
using System.Text.RegularExpressions;
using Winnow.PluginSdk;

namespace Winnow.Plugins;

public static partial class PluginManifestReader
{
    public const int MaximumManifestBytes = 64 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task<PluginManifest> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var input = File.OpenRead(path);
        if (input.Length > MaximumManifestBytes) throw new InvalidDataException("The plugin manifest is too large.");
        var manifest = await JsonSerializer.DeserializeAsync<PluginManifest>(input, JsonOptions, cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidDataException("The plugin manifest is empty.");
        Validate(manifest);
        return manifest;
    }

    public static void Validate(PluginManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        Require(IsValidId(manifest.Id), "The plugin ID must contain lowercase letters, numbers, dots or hyphens.");
        Require(!string.IsNullOrWhiteSpace(manifest.Name) && manifest.Name.Length <= 100, "The plugin name is invalid.");
        Require(System.Version.TryParse(manifest.Version, out var version) && version.Major >= 0, "The plugin version is invalid.");
        Require(manifest.ApiVersion == PluginApi.Version, "This plugin requires a different Winnow plugin API version.");
        Require(!string.IsNullOrWhiteSpace(manifest.EntryAssembly) && manifest.EntryAssembly.Length <= 180
            && manifest.EntryAssembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            && manifest.EntryAssembly.IndexOfAny(['/', '\\', ':']) < 0
            && Path.GetFileName(manifest.EntryAssembly) == manifest.EntryAssembly,
            "The plugin entry assembly must be a DLL in its package directory.");
        Require(!string.IsNullOrWhiteSpace(manifest.EntryType) && manifest.EntryType.Length <= 256, "The plugin entry type is invalid.");
        Require(manifest.Description is null || manifest.Description.Length <= 2000, "The plugin description is too long.");
        Require(IsOptionalHttpsUrl(manifest.Website), "The plugin website must use HTTPS.");
        Require(manifest.Capabilities is { Count: > 0 and <= 4 }
            && manifest.Capabilities.All(x => x is PluginCapabilities.Library or PluginCapabilities.Metadata
                or PluginCapabilities.Artwork or PluginCapabilities.Recommendations)
            && manifest.Capabilities.Distinct(StringComparer.Ordinal).Count() == manifest.Capabilities.Count,
            "The plugin capabilities are invalid.");
        Require(manifest.Settings is { Count: <= 32 }, "The plugin has too many settings.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var setting in manifest.Settings)
        {
            Require(setting is not null && IsValidId(setting.Key) && keys.Add(setting.Key), "Plugin setting keys must be unique and valid.");
            Require(!string.IsNullOrWhiteSpace(setting.Label) && setting.Label.Length <= 120, "The plugin setting label is invalid.");
            Require(setting.Help is null || setting.Help.Length <= 1000, "The plugin setting help is too long.");
            Require(IsOptionalHttpsUrl(setting.SetupUrl), "The plugin setup URL must use HTTPS.");
        }
        ValidateNetwork(manifest.Network);
    }

    public static void ValidateNetwork(PluginNetworkOptions network)
    {
        Require(network is not null, "The plugin network configuration is missing.");
        Require(network.AllowedHosts is { Count: <= 32 } && network.AllowedHosts.All(IsValidHost), "The allowed hosts are invalid.");
        Require(double.IsFinite(network.RequestsPerSecond) && network.RequestsPerSecond is >= 0.05 and <= 4,
            "The request budget must be between 0.05 and 4 requests per second.");
        Require(network.MaxRetries is >= 0 and <= 2, "The retry budget must be between zero and two.");
        Require(network.MaxResponseBytes is > 0 and <= 32 * 1024 * 1024, "The response size limit is invalid.");
        Require(network.TimeoutSeconds is >= 1 and <= 120, "The request timeout is invalid.");
    }

    public static bool IsValidId(string? value) => value is { Length: > 0 and <= 64 } && Identifier().IsMatch(value);
    public static bool IsValidHost(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 253
        && Uri.CheckHostName(value) == UriHostNameType.Dns && !value.EndsWith('.')
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-');

    private static bool IsOptionalHttpsUrl(string? value) => value is null || (value.Length <= 2048
        && Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        && uri.UserInfo.Length == 0);

    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9.-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex Identifier();
}
