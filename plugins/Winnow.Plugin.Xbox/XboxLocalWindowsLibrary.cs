using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Winnow.Plugin.Xbox;

public sealed record XboxLocalRegistration(string PackageFamilyName, string InstallLocation, bool Healthy)
{
    public string? PackageFullName { get; init; }
}

/// <summary>Windows package registration is the authority; no launcher databases or registry internals are modified.</summary>
public sealed class WindowsXboxLocalLibrary : IXboxLocalLibrary
{
    private const int MaximumInventoryCharacters = 8 * 1024 * 1024;
    private readonly Func<CancellationToken, Task<IReadOnlyList<XboxLocalRegistration>?>> _inventory;
    private readonly Func<string, string> _canonicalize;
    private readonly Func<string, bool> _activate;
    private readonly Func<string, string, string, string?> _resolveResource;

    public WindowsXboxLocalLibrary()
        : this(ReadWindowsInventoryAsync, CanonicalizeWindowsPath, ActivateWindowsApplication, XboxLocalDisplayNameResolver.Resolve) { }

    public WindowsXboxLocalLibrary(Func<CancellationToken, Task<IReadOnlyList<XboxLocalRegistration>?>> inventory,
        Func<string, string>? canonicalize = null, Func<string, bool>? activate = null,
        Func<string, string, string, string?>? resolveResource = null)
    {
        _inventory = inventory;
        _canonicalize = canonicalize ?? Path.GetFullPath;
        _activate = activate ?? (_ => false);
        _resolveResource = resolveResource ?? ((_, _, _) => null);
    }

    public async Task<XboxLocalScan?> ScanAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var registrations = await _inventory(cancellationToken).ConfigureAwait(false);
        if (registrations is null) return null;
        var packages = new List<XboxLocalGame>();
        var games = new List<XboxLocalGame>();
        var complete = true;
        var scratch = Path.Combine(Path.GetTempPath(), "Winnow", "xbox-scan-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(scratch);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var registration in registrations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!registration.Healthy || !seen.Add(registration.PackageFamilyName))
                {
                    complete = false;
                    continue;
                }
                try
                {
                    if (!XboxLocalPackageParser.IsPackageFamilyName(registration.PackageFamilyName)
                        || !Path.IsPathFullyQualified(registration.InstallLocation))
                        throw new FormatException("Invalid Windows package registration.");
                    var location = _canonicalize(registration.InstallLocation);
                    var manifest = await CopySnapshotAsync(location, "AppxManifest.xml", scratch, false, cancellationToken).ConfigureAwait(false);
                    var config = await CopySnapshotAsync(location, "MicrosoftGame.config", scratch, true, cancellationToken).ConfigureAwait(false);
                    var services = await CopySnapshotAsync(location, "xboxservices.config", scratch, true, cancellationToken).ConfigureAwait(false);
                    var parsed = XboxLocalPackageParser.Parse(registration.PackageFamilyName, location, manifest!, config, services,
                        resource => registration.PackageFullName is { } fullName
                            ? _resolveResource(fullName, registration.PackageFamilyName, resource) : null);
                    if (parsed is not null)
                    {
                        packages.Add(parsed.Game);
                        if (parsed.IsGame) games.Add(parsed.Game);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException
                    or System.Xml.XmlException or JsonException or System.ComponentModel.Win32Exception or ArgumentException)
                {
                    complete = false;
                }
            }
        }
        finally
        {
            // Only files created in this scan's private directory are removed.
            try { Directory.Delete(scratch, true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        // Registration can change while manifests are copied. A second inventory withholds
        // absence authority during an install, uninstall or update observed across this scan.
        if (complete)
        {
            var after = await _inventory(cancellationToken).ConfigureAwait(false);
            complete = after is not null && registrations.Count == after.Count
                && registrations.OrderBy(x => x.PackageFamilyName, StringComparer.OrdinalIgnoreCase)
                    .SequenceEqual(after.OrderBy(x => x.PackageFamilyName, StringComparer.OrdinalIgnoreCase));
        }
        return new XboxLocalScan(games, complete) { Packages = packages };
    }

    public async Task<bool> LaunchAsync(string packageFamilyName, string applicationId, CancellationToken cancellationToken = default)
    {
        if (!XboxLocalPackageParser.IsPackageFamilyName(packageFamilyName) || !XboxLocalPackageParser.IsApplicationId(applicationId))
            return false;
        var scan = await ScanAsync(cancellationToken).ConfigureAwait(false);
        var target = packageFamilyName + "!" + applicationId;
        // Re-read registration before handing off: a stale or caller-supplied AUMID cannot name a different app.
        if (scan?.Packages.Count(p => string.Equals(p.AppUserModelId, target, StringComparison.OrdinalIgnoreCase)) != 1)
            return false;
        cancellationToken.ThrowIfCancellationRequested();
        return _activate(target);
    }

    public Task<bool> OpenStoreAsync(string productId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows() || !XboxLocalPackageParser.IsStoreId(productId)) return Task.FromResult(false);
        try
        {
            using var process = Process.Start(new ProcessStartInfo("ms-windows-store://pdp/?ProductId=" + productId)
            {
                UseShellExecute = true
            });
            return Task.FromResult(true);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return Task.FromResult(false);
        }
    }

    public static IReadOnlyList<XboxLocalRegistration> ParseInventory(string json)
    {
        if (json.Length > MaximumInventoryCharacters) throw new FormatException("Package inventory is too large.");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new FormatException("Package inventory is not an array.");
        var result = new List<XboxLocalRegistration>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (result.Count >= 10000 || item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("PackageFamilyName", out var family) || family.ValueKind != JsonValueKind.String
                || !item.TryGetProperty("InstallLocation", out var path) || path.ValueKind != JsonValueKind.String
                || !item.TryGetProperty("Healthy", out var healthy) || healthy.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new FormatException("Malformed package inventory.");
            result.Add(new XboxLocalRegistration(family.GetString()!, path.GetString()!, healthy.GetBoolean())
            {
                PackageFullName = item.TryGetProperty("PackageFullName", out var fullName) && fullName.ValueKind == JsonValueKind.String
                    ? fullName.GetString() : null
            });
        }
        return result;
    }

    private static async Task<IReadOnlyList<XboxLocalRegistration>?> ReadWindowsInventoryAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return null;
        // Fixed program text: no title, path, account or setting is interpolated into PowerShell.
        const string script = """
            $ErrorActionPreference = 'Stop'
            [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
            $rows = @(Get-AppxPackage -PackageTypeFilter Main | Where-Object { -not $_.IsFramework -and -not $_.IsResourcePackage -and $_.SignatureKind -eq 'Store' } | ForEach-Object {
                $location = ''; $healthy = $false
                try { $location = $_.InstallLocation; $healthy = [string]$_.Status -eq 'Ok' } catch { }
                [pscustomobject]@{ PackageFamilyName = $_.PackageFamilyName; PackageFullName = $_.PackageFullName; InstallLocation = [string]$location; Healthy = [bool]$healthy }
            })
            ConvertTo-Json -InputObject $rows -Compress
            """;
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32", "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) })
            start.ArgumentList.Add(argument);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(30));
        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start()) return null;
            var outputTask = ReadBoundedAsync(process.StandardOutput, budget.Token);
            var errorTask = ReadBoundedAsync(process.StandardError, budget.Token);
            await Task.WhenAll(outputTask, errorTask, process.WaitForExitAsync(budget.Token)).ConfigureAwait(false);
            return process.ExitCode == 0 ? ParseInventory(await outputTask.ConfigureAwait(false)) : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or FormatException or JsonException) { return null; }
        finally
        {
            try { if (process.Id > 0 && !process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var result = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (result.Length + count > MaximumInventoryCharacters) throw new FormatException("Package inventory is too large.");
            result.Append(buffer, 0, count);
        }
        return result.ToString();
    }

    private static async Task<byte[]?> CopySnapshotAsync(string directory, string fileName, string scratch, bool optional,
        CancellationToken cancellationToken)
    {
        var source = Path.Combine(directory, fileName);
        var copy = Path.Combine(scratch, Guid.NewGuid().ToString("N"));
        try
        {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 8192, true))
            await using (var output = new FileStream(copy, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true))
            {
                var buffer = new byte[8192];
                var total = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    total += count;
                    if (total > XboxLocalPackageParser.MaximumFileBytes) throw new FormatException("Package document is too large.");
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                }
            }
            return await File.ReadAllBytesAsync(copy, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException) when (optional) { return null; }
    }

    private static string CanonicalizeWindowsPath(string path)
    {
        if (!OperatingSystem.IsWindows()) return Path.GetFullPath(path);
        using var handle = CreateFile(path, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var buffer = new StringBuilder(32768);
        var length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
        if (length == 0 || length >= buffer.Capacity) throw new IOException("Cannot resolve package installation path.");
        var final = buffer.ToString();
        return final.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) ? @"\\" + final[8..]
            : final.StartsWith(@"\\?\", StringComparison.Ordinal) ? final[4..] : final;
    }

    private static bool ActivateWindowsApplication(string aumid)
    {
        if (!OperatingSystem.IsWindows()) return false;
        object? manager = null;
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C"), true)!;
            manager = Activator.CreateInstance(type);
            return ((IApplicationActivationManager)manager!).ActivateApplication(aumid, null, 2, out _) >= 0;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or System.ComponentModel.Win32Exception) { return false; }
        finally { if (manager is not null) Marshal.FinalReleaseComObject(manager); }
    }

    [ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string? arguments, uint options, out uint processId);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint capacity, uint flags);
}
