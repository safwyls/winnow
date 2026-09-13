using System.Runtime.InteropServices;
using System.Text.Json;

namespace Winnow.App.Services;

internal static class UpdateInstallation
{
    internal static bool IsManagedLinux(string directory) =>
        directory.StartsWith("/opt/", StringComparison.Ordinal) ||
        directory.StartsWith("/usr/", StringComparison.Ordinal) ||
        File.Exists(Path.Combine(directory, "package-managed"));

    internal static bool IsPortable(string directory, string? executable, string runtime, string? osRelease)
    {
        try
        {
            if (runtime is not ("win-x64" or "linux-x64")) return false;
            var name = runtime == "win-x64" ? "Winnow.exe" : "Winnow";
            var comparison = runtime == "win-x64" ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (executable is null || !string.Equals(Path.GetFullPath(executable), Path.Combine(Path.GetFullPath(directory), name), comparison)) return false;
            if (runtime == "linux-x64" && (IsManagedLinux(directory) || !IsSupportedUbuntu(osRelease))) return false;
            if (File.Exists(Path.Combine(directory, "unins000.exe"))) return false;
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "release-info.json")));
            return manifest.RootElement.GetProperty("runtime").GetString() == runtime &&
                ReleaseVersion.Parse(manifest.RootElement.GetProperty("version").GetString() ?? "") is { IsDevelopment: false } &&
                File.Exists(Path.Combine(directory, "update-helper", runtime == "win-x64" ? "Winnow.Update.Helper.exe" : "Winnow.Update.Helper"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        { return false; }
    }

    internal static bool IsSupportedUbuntu(string? osRelease)
    {
        var values = (osRelease ?? "").Split('\n').Select(line => line.Trim().Split('=', 2))
            .Where(parts => parts.Length == 2).ToArray();
        return values.Any(p => p[0] == "ID" && p[1].Trim('"') == "ubuntu") &&
            values.Any(p => p[0] == "VERSION_ID" && p[1].Trim('"') == "24.04");
    }

    internal static string Runtime => RuntimeInformation.ProcessArchitecture != Architecture.X64 ? "" :
        OperatingSystem.IsWindows() ? "win-x64" : OperatingSystem.IsLinux() ? "linux-x64" : "";

    internal static string? OsRelease
    {
        get
        {
            try { return OperatingSystem.IsLinux() ? File.ReadAllText("/etc/os-release") : null; }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }
    }
}
