using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Winnow.Update.Helper;

internal static class InstalledUpdateHandoff
{
    internal static async Task PrepareAsync(string installer, string sha256, string installation, string executable,
        string dataDirectory, Process parent, string[] arguments)
    {
        if (!OperatingSystem.IsWindows()) throw new IOException("Registered installer updates require Windows.");
        var registered = WindowsInstallPolicy.RegisteredDirectory(executable);
        if (registered is null || !string.Equals(Path.TrimEndingDirectorySeparator(registered), Path.TrimEndingDirectorySeparator(Path.GetFullPath(installation)), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(parent.MainModule?.FileName, Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase))
            throw new IOException("This running copy is not the registered Winnow installation.");
        if (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit)) throw new IOException("The update has no valid SHA-256 digest.");
        await using (var payload = File.OpenRead(installer))
            if (!Convert.ToHexString(await SHA256.HashDataAsync(payload)).Equals(sha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("The update checksum does not match. Download it again.");

        var root = Path.Combine(Path.GetFullPath(dataDirectory), "updates", "handoff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var script = Path.Combine(root, "Install-Update.ps1");
        await using (var source = typeof(InstalledUpdateHandoff).Assembly.GetManifestResourceStream("Winnow.UpdateHelper.ps1")
            ?? throw new IOException("The registered update helper is missing."))
        await using (var destination = File.Create(script)) await source.CopyToAsync(destination);
        var manifest = Path.Combine(root, "handoff.json");
        await File.WriteAllTextAsync(manifest, JsonSerializer.Serialize(new
        {
            ProcessId = parent.Id,
            ProcessStartTicks = parent.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
            Executable = Path.GetFullPath(executable), InstallDirectory = registered,
            Installer = Path.GetFullPath(installer), Sha256 = sha256,
            Arguments = WindowsInstallPolicy.RestartArguments(dataDirectory, arguments), WaitSeconds = 120,
        }));
        try
        {
            var start = HandoffProcess.StartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"));
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-ManifestPath", manifest })
                start.ArgumentList.Add(argument);
            using var child = Process.Start(start) ?? throw new IOException("The registered update helper could not start.");
            HandoffProcess.Drain(child);
            var timer = Stopwatch.StartNew();
            while (!File.Exists(Path.Combine(root, "ready")))
            {
                if (parent.HasExited || child.HasExited || timer.Elapsed >= TimeSpan.FromSeconds(20))
                    throw new IOException("The installer handoff failed. Recovery details: " + root);
                await Task.Delay(50);
            }
            await File.WriteAllTextAsync(Path.Combine(root, "proceed"), "ready");
        }
        catch
        {
            await File.WriteAllTextAsync(Path.Combine(root, "cancel"), "cancelled");
            throw;
        }
    }
}
