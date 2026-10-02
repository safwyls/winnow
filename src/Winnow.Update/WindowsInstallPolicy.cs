using Microsoft.Win32;
using System.Runtime.Versioning;

namespace Winnow.Update;

/// <summary>Identifies the original registered Inno installation and its safe restart arguments.</summary>
public static class WindowsInstallPolicy
{
    public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{A2A9E417-5D4B-4B85-8738-7D6E993E51CE}_is1";

    [SupportedOSPlatform("windows")]
    public static string? RegisteredDirectory(string? executable)
    {
        try
        {
            using var registry = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
            using var key = registry.OpenSubKey(UninstallKey);
            var directory = key?.GetValue("InstallLocation") as string;
            return MatchesInstallation(executable, directory) ? Path.GetFullPath(directory!) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    public static bool MatchesInstallation(string? executable, string? registeredDirectory)
    {
        if (string.IsNullOrWhiteSpace(executable) || string.IsNullOrWhiteSpace(registeredDirectory)) return false;
        try
        {
            return Path.IsPathFullyQualified(registeredDirectory) &&
                string.Equals(Path.GetFullPath(executable), Path.Combine(Path.GetFullPath(registeredDirectory), "Winnow.exe"), StringComparison.OrdinalIgnoreCase) &&
                File.Exists(Path.Combine(registeredDirectory, "unins000.exe"));
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or IOException) { return false; }
    }

    public static string[] RestartArguments(string dataDirectory, IEnumerable<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        var result = new List<string> { "--data-dir", Path.GetFullPath(dataDirectory) };
        if (arguments.Contains("--no-sync", StringComparer.Ordinal)) result.Add("--no-sync");
        return result.ToArray();
    }
}
