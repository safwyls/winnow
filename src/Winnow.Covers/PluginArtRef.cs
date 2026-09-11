using System.Security.Cryptography;
using System.Text;

namespace Winnow.Covers;

/// <summary>Content-addressed plugin artwork keys keep remote URLs out of cache filenames.</summary>
public static class PluginArtRef
{
    public const string SourcePrefix = "plugin:";
    private const string KeyPrefix = "plugin-";
    private const string UriPrefix = "winnow://plugin-art/";

    public static bool IsPluginId(string? id) => id is { Length: > 0 and <= 64 }
        && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.') && id == id.ToLowerInvariant();

    public static CoverKey? Key(string pluginId, string? url)
        => IsPluginId(pluginId) && Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo) && uri.Fragment.Length == 0 && uri.IsDefaultPort
            ? new(KeyPrefix + pluginId, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri)))) : null;

    public static string? PluginId(CoverKey key) => key.Provider.StartsWith(KeyPrefix, StringComparison.Ordinal)
        && IsPluginId(key.Provider[KeyPrefix.Length..]) && key.Id.Length == 64 && key.Id.All(char.IsAsciiHexDigit)
            ? key.Provider[KeyPrefix.Length..] : null;

    public static string Reference(CoverKey key) => UriPrefix + PluginId(key) + "/" + key.Id;

    public static CoverKey? Parse(string? reference)
    {
        if (reference?.StartsWith(UriPrefix, StringComparison.Ordinal) != true) return null;
        var parts = reference[UriPrefix.Length..].Split('/');
        if (parts.Length != 2) return null;
        var key = new CoverKey(KeyPrefix + parts[0], parts[1]);
        return PluginId(key) is null ? null : key;
    }
}
