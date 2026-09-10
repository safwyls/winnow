using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;

namespace Winnow.App.Services;

public interface IUpdateInstaller
{
    bool IsSupported { get; }
    Task PrepareAsync(string installerPath, string sha256, CancellationToken ct = default);
}

/// <summary>Hands an authenticated installer to a separate process before normal shutdown.</summary>
public sealed class WindowsUpdateInstaller : IUpdateInstaller
{
    public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{A2A9E417-5D4B-4B85-8738-7D6E993E51CE}_is1";
    public bool IsSupported => OperatingSystem.IsWindows() && GetInstallDirectory() is not null;

    [SupportedOSPlatform("windows")]
    private static string? GetInstallDirectory()
    {
        try
        {
            using var registry = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
            using var key = registry.OpenSubKey(UninstallKey);
            var directory = key?.GetValue("InstallLocation") as string;
            return MatchesInstallation(Environment.ProcessPath, directory) ? Path.GetFullPath(directory!) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
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
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException) { return false; }
    }

    public static string[] RestartArguments(string dataDirectory, IEnumerable<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        // Seeding and sign-in commands must never be repeated by an upgrade.
        var result = new List<string> { "--data-dir", Path.GetFullPath(dataDirectory) };
        if (arguments.Contains("--no-sync", StringComparer.Ordinal)) result.Add("--no-sync");
        return result.ToArray();
    }

    public async Task PrepareAsync(string installerPath, string sha256, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows() || GetInstallDirectory() is not { } installDirectory)
            throw new InvalidOperationException("This copy needs a manual update from GitHub Releases.");
        if (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("The update has no valid SHA-256 digest.");
        await using (var payload = File.OpenRead(installerPath))
        {
            if (!string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(payload, ct).ConfigureAwait(false)), sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The update checksum does not match. Download it again.");
        }

        var directory = Path.Combine(Program.DataLocation.Root, "updates", "handoff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var script = Path.Combine(directory, "Install-Update.ps1");
        await using (var source = typeof(WindowsUpdateInstaller).Assembly.GetManifestResourceStream("Winnow.UpdateHelper.ps1")
            ?? throw new InvalidOperationException("The update helper is missing. Install this release manually."))
        await using (var destination = File.Create(script)) await source.CopyToAsync(destination, ct).ConfigureAwait(false);
        using var current = Process.GetCurrentProcess();
        var manifest = Path.Combine(directory, "handoff.json");
        await File.WriteAllTextAsync(manifest, JsonSerializer.Serialize(new
        {
            ProcessId = current.Id,
            ProcessStartTicks = current.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Executable = Environment.ProcessPath,
            InstallDirectory = installDirectory,
            Installer = Path.GetFullPath(installerPath),
            Sha256 = sha256,
            Arguments = RestartArguments(Program.DataLocation.Root, Environment.GetCommandLineArgs()),
            WaitSeconds = 120
        }), ct).ConfigureAwait(false);
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-ManifestPath", manifest })
            start.ArgumentList.Add(argument);
        using var helper = Process.Start(start) ?? throw new IOException("The update helper could not start.");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            while (!File.Exists(Path.Combine(directory, "ready")))
            {
                if (helper.HasExited) throw new IOException($"The update helper failed. See {directory} for recovery details.");
                await Task.Delay(100, timeout.Token).ConfigureAwait(false);
            }
            ct.ThrowIfCancellationRequested();
            await File.WriteAllTextAsync(Path.Combine(directory, "proceed"), "ready", ct).ConfigureAwait(false);
        }
        catch
        {
            // A helper without permission cannot install, even if Winnow closes later.
            File.WriteAllText(Path.Combine(directory, "cancel"), "cancelled");
            throw;
        }
    }
}
