using System.Reflection;

namespace Winnow.App.Services;

/// <summary>Build identity embedded in Winnow itself, including in portable installations.</summary>
public sealed record ApplicationBuildInfo(string Version, string Commit)
{
    public static ApplicationBuildInfo Current { get; } = FromInformationalVersion(
        typeof(ApplicationBuildInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    internal static ApplicationBuildInfo FromInformationalVersion(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
            return new("Unknown", "Unavailable");

        var parts = informationalVersion.Split('+', 2);
        return new(parts[0], parts.Length == 2 && parts[1].Length > 0 ? parts[1] : "Unavailable");
    }
}
