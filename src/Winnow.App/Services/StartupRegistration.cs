using Microsoft.Win32;
using System.Runtime.Versioning;

namespace Winnow.App.Services;

/// <summary>The operating-system seam behind SETTINGS › APPLICATION › START WITH WINDOWS.</summary>
public interface IStartupRegistration
{
    bool IsSupported { get; }

    bool IsEnabled();

    void SetEnabled(bool enabled);
}

/// <summary>
/// Registers the installed executable for the current Windows user. The Run
/// key needs no elevation and keeps Winnow's no-account, local-only boundary.
/// </summary>
public sealed class WindowsStartupRegistration : IStartupRegistration
{
    internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ValueName = "Winnow";

    private readonly string? _executablePath;

    public WindowsStartupRegistration()
        : this(Environment.ProcessPath)
    {
    }

    internal WindowsStartupRegistration(string? executablePath)
    {
        _executablePath = executablePath;
    }

    public bool IsSupported => OperatingSystem.IsWindows()
        && !string.IsNullOrWhiteSpace(_executablePath);

    internal string? Command => IsSupported
        ? $"\"{_executablePath}\" --background"
        : null;

    public bool IsEnabled()
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(_executablePath))
        {
            return false;
        }

        return IsEnabledOnWindows();
    }

    [SupportedOSPlatform("windows")]
    private bool IsEnabledOnWindows()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return string.Equals(
            key?.GetValue(ValueName) as string,
            Command,
            StringComparison.OrdinalIgnoreCase);
    }

    public void SetEnabled(bool enabled)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(_executablePath))
        {
            throw new PlatformNotSupportedException("Start with Windows is only available on Windows.");
        }

        SetEnabledOnWindows(enabled);
    }

    [SupportedOSPlatform("windows")]
    private void SetEnabledOnWindows(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("The Windows startup settings could not be opened.");

        if (enabled)
        {
            key.SetValue(ValueName, Command!, RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
