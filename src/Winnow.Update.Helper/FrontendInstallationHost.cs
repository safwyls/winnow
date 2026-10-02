using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Winnow.Update.Helper;

/// <summary>Owns portable frontend replacement exclusion until its actual parent or input closes.</summary>
internal static class FrontendInstallationHost
{
    internal static Process Parent(int expectedId)
    {
        var actual = OperatingSystem.IsWindows() ? Winnow.Activation.WindowsParentProcess.CurrentParentId() : GetParentPid();
        if (expectedId <= 0 || expectedId != actual)
            throw new ArgumentException("The update helper parent does not match its launching process.");
        var process = Process.GetProcessById(actual);
        try
        {
            _ = process.SafeHandle;
            if (process.HasExited) throw new IOException("The update helper parent has exited.");
            return process;
        }
        catch { process.Dispose(); throw; }
    }

    [DllImport("libc", EntryPoint = "getppid")]
    private static extern int GetParentPid();

    internal static async Task RunAsync(string installation, string dataDirectory, Process parent, TextReader input, TextWriter output)
    {
        installation = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installation));
        dataDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDirectory));
        var journal = PortableUpdateEngine.GetJournalPath(installation);
        InstallationStartupLease? lease = null;
        var pendingStartup = false;
        string? recoveryStatus = null;
        try
        {
            lease = PortableUpdateEngine.AcquireStartupLease(installation);
            if (!lease.CanUpdate && !lease.IsManaged)
                recoveryStatus = "This read-only portable copy requires a manual update from the release page.";
            // Never turn an interrupted upgrade into a read-only launch: the backend may migrate data.
            if (File.Exists(journal))
            {
                var state = PortableUpdateEngine.ReadJournal(journal);
                if (state.Phase == UpdatePhase.Installed)
                {
                    PortableUpdateEngine.ValidateStartup(journal, installation, dataDirectory);
                    pendingStartup = true;
                }
                else if (state.Phase is not (UpdatePhase.Staged or UpdatePhase.Ready or UpdatePhase.Restored))
                    throw new IOException("An interrupted portable update requires recovery before Winnow can start: " + journal);
                if (state.Phase == UpdatePhase.Restored)
                    recoveryStatus = "The previous installation and selected library were restored after an interrupted update.";
                else if (!string.IsNullOrWhiteSpace(state.Failure))
                    recoveryStatus = "A previous update failed. Recovery details: " + journal;
            }
            await WriteAsync(output, new { kind = "leased", canUpdate = lease.CanUpdate, recoveryStatus });
            using var lifetime = new CancellationTokenSource();
            var parentEnded = parent.WaitForExitAsync(lifetime.Token);
            try
            {
                while (true)
                {
                    var read = ReadFrameAsync(input, lifetime.Token);
                    if (await Task.WhenAny(parentEnded, read) == parentEnded)
                    {
                        await lifetime.CancelAsync();
                        try { await read; } catch (OperationCanceledException) { }
                        break;
                    }
                    var frame = await read;
                    if (frame is null) break;
                    using var document = JsonDocument.Parse(frame);
                    var value = document.RootElement;
                    if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() != 1 ||
                        !value.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String || kind.GetString() != "ready")
                        throw new IOException("Invalid portable frontend readiness command.");
                    if (pendingStartup)
                    {
                        PortableUpdateEngine.MarkReady(journal);
                        pendingStartup = false;
                    }
                    await WriteAsync(output, new { kind = "ready" });
                }
            }
            finally
            {
                await lifetime.CancelAsync();
                try { await parentEnded; } catch (OperationCanceledException) { }
            }
        }
        finally { lease?.Dispose(); }
    }

    private static async Task<string?> ReadFrameAsync(TextReader input, CancellationToken cancellationToken)
    {
        var frame = new char[1024];
        var length = 0;
        while (length < frame.Length)
        {
            var read = await input.ReadAsync(frame.AsMemory(length, 1), cancellationToken);
            if (read == 0) return length == 0 ? null : throw new IOException("Incomplete portable frontend command.");
            if (frame[length] == '\n') return new string(frame, 0, length).TrimEnd('\r');
            length++;
        }
        throw new IOException("Portable frontend command exceeds the protocol limit.");
    }

    private static async Task WriteAsync<T>(TextWriter output, T value)
    {
        await output.WriteLineAsync(JsonSerializer.Serialize(value));
        await output.FlushAsync();
    }
}
