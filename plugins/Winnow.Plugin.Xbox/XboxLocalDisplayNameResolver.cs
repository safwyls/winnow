using System.Runtime.InteropServices;
using System.Text;

namespace Winnow.Plugin.Xbox;

/// <summary>Asks Windows for a registered package's localized text; it never loads a caller-specified DLL or PRI path.</summary>
public static class XboxLocalDisplayNameResolver
{
    public static string? Resolve(string packageFullName, string familyName, string resource)
    {
        if (!OperatingSystem.IsWindows()) return null;
        foreach (var indirectString in BuildIndirectStrings(packageFullName, familyName, resource))
        {
            var buffer = new StringBuilder(512);
            if (SHLoadIndirectString(indirectString, buffer, (uint)buffer.Capacity, IntPtr.Zero) != 0) continue;
            var result = buffer.ToString().Trim();
            if (result is { Length: > 0 and <= 256 } && !result.Any(char.IsControl)
                && !result.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)
                && !result.StartsWith('@')) return result;
        }
        return null;
    }

    public static IReadOnlyList<string> BuildIndirectStrings(string packageFullName, string familyName, string resource)
    {
        if (!XboxLocalPackageParser.IsPackageFamilyName(familyName)
            || packageFullName is not { Length: > 0 and <= 512 }
            || !packageFullName.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_')
            || resource is not { Length: > 12 and <= 1024 }
            || !resource.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)
            || resource.Any(c => char.IsControl(c) || c is '{' or '}' or '?' or '#' or '\\' or '@')) return [];

        var separator = familyName.LastIndexOf('_');
        var name = familyName[..separator];
        var publisher = familyName[separator..];
        if (!packageFullName.StartsWith(name + "_", StringComparison.OrdinalIgnoreCase)
            || !packageFullName.EndsWith(publisher, StringComparison.OrdinalIgnoreCase)) return [];

        var suffix = resource[12..];
        string[] references;
        if (suffix.StartsWith("//", StringComparison.Ordinal) && !suffix.StartsWith("///", StringComparison.Ordinal))
        {
            var slash = suffix.IndexOf('/', 2);
            if (slash < 0 || !string.Equals(suffix[2..slash], name, StringComparison.OrdinalIgnoreCase)) return [];
            references = ["ms-resource:" + suffix];
        }
        else if (suffix.StartsWith('/'))
        {
            references = ["ms-resource://" + name + "/" + suffix.TrimStart('/')];
        }
        else
        {
            // Manifest shorthand normally uses Resources.resw; Windows also supports an
            // unqualified lookup across namespaces for older package resource maps.
            references = ["ms-resource://" + name + "/resources/" + suffix, "ms-resource://" + name + "/" + suffix];
        }
        if (references.Any(reference => reference.Split('/').Any(part => part is "." or ".."))) return [];
        return references.Select(reference => "@{" + packageFullName + "?" + reference + "}").ToArray();
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int SHLoadIndirectString(string source, StringBuilder output, uint capacity, IntPtr reserved);
}
