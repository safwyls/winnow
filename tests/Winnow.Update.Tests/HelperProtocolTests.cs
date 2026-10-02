using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Winnow.Update.Helper;
using Xunit;

namespace Winnow.Update.Tests;

public sealed class HelperProtocolTests
{
    [Fact]
    public async Task ActualFrontendHoldsReplacementLeaseUntilInputClosesWithoutOpeningTheDatabase()
    {
        using var fixture = new Fixture();
        using var child = fixture.StartFrontend();
        var frame = await Frame(child);
        Assert.Equal("leased", frame.GetProperty("kind").GetString());
        Assert.True(frame.GetProperty("canUpdate").GetBoolean());
        Assert.Throws<IOException>(() => fixture.ExclusiveLease());
        Assert.Empty(Directory.GetFiles(fixture.Data));
        child.StandardInput.Close();
        await Exit(child, 0);
        using var released = fixture.ExclusiveLease();
        Assert.Empty(Directory.GetFiles(fixture.Data));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InstalledJournalIsValidatedBeforePublicationAndOnlyExplicitReadyCommitsIt(bool ready)
    {
        using var fixture = new Fixture();
        fixture.Journal(UpdatePhase.Installed);
        using var child = fixture.StartFrontend();
        Assert.Equal("leased", (await Frame(child)).GetProperty("kind").GetString());
        var validated = PortableUpdateEngine.ReadJournal(fixture.JournalPath);
        Assert.Equal(UpdatePhase.MigrationStarted, validated.Phase);
        Assert.True(validated.MigrationMayHaveStarted);
        Assert.Empty(Directory.GetFiles(fixture.Data));
        if (ready)
        {
            await child.StandardInput.WriteLineAsync("{\"kind\":\"ready\"}");
            await child.StandardInput.FlushAsync();
            Assert.Equal("ready", (await Frame(child)).GetProperty("kind").GetString());
        }
        child.StandardInput.Close();
        await Exit(child, 0);
        Assert.Equal(ready ? UpdatePhase.Ready : UpdatePhase.MigrationStarted,
            PortableUpdateEngine.ReadJournal(fixture.JournalPath).Phase);
    }

    [Theory]
    [InlineData(UpdatePhase.BackupComplete)]
    [InlineData(UpdatePhase.Replacing)]
    [InlineData(UpdatePhase.MigrationStarted)]
    [InlineData(UpdatePhase.RecoveryRequired)]
    [InlineData(UpdatePhase.Restoring)]
    public async Task UnsafeJournalCannotPublishAFrontendLeaseOrTouchItsSelectedLibrary(UpdatePhase phase)
    {
        using var fixture = new Fixture();
        fixture.Journal(phase);
        var before = File.ReadAllBytes(fixture.JournalPath);
        using var child = fixture.StartFrontend();
        await Exit(child, 1);
        Assert.Empty(await child.StandardOutput.ReadToEndAsync());
        Assert.Contains("recovery", await child.StandardError.ReadToEndAsync());
        Assert.Equal(before, File.ReadAllBytes(fixture.JournalPath));
        Assert.Empty(Directory.GetFiles(fixture.Data));
        using var released = fixture.ExclusiveLease();
    }

    [Fact]
    public async Task InstalledJournalRejectsADifferentSelectedLibraryBeforeMigration()
    {
        using var fixture = new Fixture();
        fixture.Journal(UpdatePhase.Installed, data: Path.Combine(fixture.Root, "another-library"));
        var before = File.ReadAllBytes(fixture.JournalPath);
        using var child = fixture.StartFrontend();
        await Exit(child, 1);
        Assert.Empty(await child.StandardOutput.ReadToEndAsync());
        Assert.Equal(before, File.ReadAllBytes(fixture.JournalPath));
        Assert.Empty(Directory.GetFiles(fixture.Data));
    }

    [Theory]
    [InlineData("frontend")]
    [InlineData("handoff")]
    [InlineData("installed-handoff")]
    public async Task ParentBoundCommandsRejectAnUnrelatedPidBeforeAnyFilesystemWork(string command)
    {
        using var fixture = new Fixture();
        using var child = fixture.Start(command, "--pid", "0");
        await Exit(child, 1);
        Assert.Contains("parent", await child.StandardError.ReadToEndAsync());
        Assert.False(Directory.Exists(fixture.Workspace));
        Assert.False(File.Exists(fixture.LockPath));
        Assert.Empty(Directory.GetFiles(fixture.Data));
    }

    [Fact]
    public async Task InstalledHandoffRefusesAnUnregisteredOrSpoofedExecutableWithoutWritingOrLaunching()
    {
        using var fixture = new Fixture();
        using var child = fixture.Start("installed-handoff", "--pid", Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
            "--installer", Path.Combine(fixture.Root, "never-read.exe"), "--sha256", new string('a', 64),
            "--installation", fixture.Install, "--executable", Path.Combine(fixture.Install, fixture.ExecutableName),
            "--data-dir", fixture.Data);
        await Exit(child, 1);
        Assert.Contains(OperatingSystem.IsWindows() ? "registered Winnow installation" : "require Windows", await child.StandardError.ReadToEndAsync());
        Assert.Empty(Directory.GetFileSystemEntries(fixture.Data));
        Assert.False(Directory.Exists(fixture.Workspace));
    }

    [Theory]
    [InlineData("{\"kind\":\"ready\",\"extra\":true}\n")]
    [InlineData("{\"kind\":\"other\"}\n")]
    [InlineData("{\"kind\":\"ready\"}")]
    [InlineData("oversized")]
    public async Task InvalidOrUnboundedFramesFailWithoutMarkingReadyAndReleaseTheLease(string frame)
    {
        using var fixture = new Fixture();
        fixture.Journal(UpdatePhase.Installed);
        using var child = fixture.StartFrontend();
        await Frame(child);
        await child.StandardInput.WriteAsync(frame == "oversized" ? new string('x', 1025) : frame);
        child.StandardInput.Close();
        await Exit(child, 1);
        Assert.Equal(UpdatePhase.MigrationStarted, PortableUpdateEngine.ReadJournal(fixture.JournalPath).Phase);
        using var released = fixture.ExclusiveLease();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnUnavailableLeaseAllowsReadOnlyStartupOnlyWithoutAnyPendingJournal(bool journal)
    {
        using var fixture = new Fixture();
        if (journal) fixture.Journal(UpdatePhase.Installed);
        using var locked = fixture.ExclusiveLease();
        using var child = fixture.StartFrontend();
        if (journal)
        {
            await Exit(child, 1);
            Assert.Empty(await child.StandardOutput.ReadToEndAsync());
            Assert.Equal(UpdatePhase.Installed, PortableUpdateEngine.ReadJournal(fixture.JournalPath).Phase);
        }
        else
        {
            var frame = await Frame(child);
            Assert.False(frame.GetProperty("canUpdate").GetBoolean());
            Assert.Contains("manual update", frame.GetProperty("recoveryStatus").GetString());
            child.StandardInput.Close();
            await Exit(child, 0);
        }
        Assert.Empty(Directory.GetFiles(fixture.Data));
    }

    [Fact]
    public async Task RestoredJournalPublishesRecoveryStatusWithoutRepeatingItsFailureText()
    {
        using var fixture = new Fixture();
        fixture.Journal(UpdatePhase.Restored, failure: "Untrusted diagnostic contents");
        using var child = fixture.StartFrontend();
        var status = (await Frame(child)).GetProperty("recoveryStatus").GetString();
        Assert.Contains("restored", status);
        Assert.DoesNotContain("Untrusted", status);
        child.StandardInput.Close();
        await Exit(child, 0);
    }

    [Fact]
    public async Task StagePrepareAndDiscardKeepExactTransactionAndSafeRestartFlags()
    {
        using var fixture = new Fixture();
        fixture.Archive();
        var transaction = Guid.NewGuid().ToString("N");
        var (code, output, _) = await Command("stage", "--archive", fixture.ArchivePath, "--sha256", fixture.Digest,
            "--version", "2.0.0", "--runtime", fixture.Runtime, "--installation", fixture.Install,
            "--data-dir", fixture.Data, "--executable", fixture.ExecutableName, "--transaction", transaction,
            "--no-sync", "--seed-sample", "--epic-login");
        Assert.Equal(0, code);
        Assert.Equal(fixture.JournalPath, output.Trim());
        var journal = PortableUpdateEngine.ReadJournal(fixture.JournalPath);
        Assert.Equal(transaction, journal.TransactionId);
        Assert.Equal(new[] { "--no-sync" }, journal.RestartArguments);
        Assert.Equal(fixture.Data, journal.DataDirectory);
        var helperDirectory = Path.Combine(fixture.Install, "update-helper");
        Directory.CreateDirectory(helperDirectory);
        var helperName = OperatingSystem.IsWindows() ? "Winnow.Update.Helper.exe" : "Winnow.Update.Helper";
        File.WriteAllText(Path.Combine(helperDirectory, helperName), "helper bytes");
        Assert.Equal(1, (await Command("prepare", "--journal", fixture.JournalPath, "--transaction", Guid.NewGuid().ToString("N"))).Code);
        var prepared = await Command("prepare", "--journal", fixture.JournalPath, "--transaction", transaction);
        Assert.Equal(0, prepared.Code);
        Assert.Equal("helper bytes", File.ReadAllText(prepared.Output.Trim()));
        Assert.Equal(1, (await Command("discard", "--journal", fixture.JournalPath, "--transaction", Guid.NewGuid().ToString("N"))).Code);
        Assert.True(File.Exists(fixture.JournalPath));
        Assert.Equal(0, (await Command("discard", "--journal", fixture.JournalPath, "--transaction", transaction)).Code);
        Assert.False(Directory.Exists(fixture.Workspace));
        Assert.Equal("old", File.ReadAllText(Path.Combine(fixture.Install, fixture.ExecutableName)));
        Assert.Empty(Directory.GetFiles(fixture.Data));
    }

    [Fact]
    public void ReplacementWorkersUseSeparateStreamsFromTheFrontendProtocol()
    {
        var start = HandoffProcess.StartInfo("worker");
        Assert.True(start.RedirectStandardInput);
        Assert.True(start.RedirectStandardOutput);
        Assert.True(start.RedirectStandardError);
        Assert.False(start.UseShellExecute);
        Assert.True(start.CreateNoWindow);
    }

    [Fact]
    public async Task ParentDeathCancelsAnIdleProtocolReadAndReleasesItsLease()
    {
        using var parentFixture = new Fixture();
        using var parent = parentFixture.StartFrontend();
        await Frame(parent);
        using var fixture = new Fixture();
        using var input = new PendingReader();
        using var output = new PublishedWriter();
        var lifetime = FrontendInstallationHost.RunAsync(fixture.Install, fixture.Data, parent, input, output);
        await output.Published.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Throws<IOException>(() => fixture.ExclusiveLease());
        parent.StandardInput.Close();
        await Exit(parent, 0);
        await lifetime.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(input.Cancelled);
        using var released = fixture.ExclusiveLease();
    }

    [Fact]
    public async Task RealCopiedHelperAcknowledgesHandoffAndReleasesCommandPipesWhileItsParentLives()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture();
        fixture.Archive();
        var transaction = Guid.NewGuid().ToString("N");
        PortableUpdateEngine.Stage(fixture.ArchivePath, fixture.Digest, "2.0.0", fixture.Runtime,
            fixture.Install, fixture.Data, fixture.ExecutableName, ["--no-sync"], transactionId: transaction);
        var helperRoot = Path.Combine(fixture.Install, "update-helper");
        Directory.CreateDirectory(helperRoot);
        var built = Path.GetDirectoryName(typeof(UpdateHelperCommands).Assembly.Location)!;
        foreach (var file in Directory.GetFiles(built))
            File.Copy(file, Path.Combine(helperRoot, Path.GetFileName(file)));
        var helper = Path.Combine(helperRoot, "Winnow.Update.Helper.exe");
        Assert.True(File.Exists(helper));
        var options = Path.Combine(fixture.Root, "options.json");
        File.WriteAllText(options, JsonSerializer.Serialize(new { helper, journal = fixture.JournalPath, transaction }));
        var script = Path.Combine(fixture.Root, "parent.ps1");
        File.WriteAllText(script, """
            param([string]$Options)
            $data = Get-Content -LiteralPath $Options -Raw | ConvertFrom-Json
            & $data.helper handoff --journal $data.journal --transaction $data.transaction --pid $PID
            [Console]::Out.WriteLine("handoff:$LASTEXITCODE")
            [Console]::Out.Flush()
            [void][Console]::ReadLine()
            """);
        var start = HandoffProcess.StartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            @"WindowsPowerShell\v1.0\powershell.exe"));
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-Options", options })
            start.ArgumentList.Add(argument);
        using var parent = Process.Start(start)!;
        try
        {
            Assert.Equal("handoff:0", await parent.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(25)));
            Assert.False(parent.HasExited);
            Assert.Equal(transaction, File.ReadAllText(Path.Combine(fixture.Workspace, "helper-ready")));
            Assert.Equal(transaction, File.ReadAllText(Path.Combine(fixture.Workspace, "proceed")));
            Assert.Equal(UpdatePhase.Staged, PortableUpdateEngine.ReadJournal(fixture.JournalPath).Phase);
            Assert.Equal("old", File.ReadAllText(Path.Combine(fixture.Install, fixture.ExecutableName)));
            Assert.Empty(Directory.GetFiles(fixture.Data));
        }
        finally
        {
            // Stop only this disposable fixture's waiting worker before letting its parent exit.
            // The test verifies handoff without replacing or starting an application.
            var copied = Path.Combine(fixture.Workspace, "helper", "Winnow.Update.Helper.exe");
            foreach (var worker in Process.GetProcessesByName("Winnow.Update.Helper"))
            {
                using (worker)
                {
                    if (!string.Equals(worker.MainModule?.FileName, copied, StringComparison.OrdinalIgnoreCase)) continue;
                    worker.Kill();
                    await worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }
            }
            parent.StandardInput.Close();
            if (!parent.HasExited)
            {
                try { await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (TimeoutException) { parent.Kill(entireProcessTree: true); await parent.WaitForExitAsync(); }
            }
        }
    }

    private sealed class PendingReader : TextReader
    {
        internal bool Cancelled { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); return 0; }
            catch (OperationCanceledException) { Cancelled = true; throw; }
        }
    }

    private sealed class PublishedWriter : StringWriter
    {
        internal TaskCompletionSource Published { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override Task FlushAsync() { Published.TrySetResult(); return Task.CompletedTask; }
    }

    private static async Task<(int Code, string Output, string Error)> Command(params string[] arguments)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        var code = await UpdateHelperCommands.RunAsync(arguments, TextReader.Null, output, error);
        return (code, output.ToString(), error.ToString());
    }

    private static async Task<JsonElement> Frame(Process child)
    {
        var line = await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
        if (line is null) Assert.Fail(await child.StandardError.ReadToEndAsync());
        return JsonDocument.Parse(line!).RootElement.Clone();
    }

    private static async Task Exit(Process child, int expected)
    {
        try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
        catch { if (!child.HasExited) child.Kill(entireProcessTree: true); throw; }
        Assert.Equal(expected, child.ExitCode);
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "winnow-update-helper-" + Guid.NewGuid().ToString("N"));
        internal string Install => Path.Combine(Root, "portable");
        internal string Data => Path.Combine(Root, "selected library");
        internal string JournalPath => PortableUpdateEngine.GetJournalPath(Install);
        internal string Workspace => Path.GetDirectoryName(JournalPath)!;
        internal string LockPath => Workspace + ".lock";
        internal string Runtime => OperatingSystem.IsWindows() ? "win-x64" : "linux-x64";
        internal string ExecutableName => OperatingSystem.IsWindows() ? "Winnow.exe" : "Winnow";
        internal string ArchivePath => Path.Combine(Root, "release.zip");
        internal string Digest => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(ArchivePath)));
        private readonly List<Process> _children = [];
        internal Fixture()
        {
            Directory.CreateDirectory(Install);
            Directory.CreateDirectory(Data);
            File.WriteAllText(Path.Combine(Install, ExecutableName), "old");
            File.WriteAllText(Path.Combine(Install, "release-info.json"), JsonSerializer.Serialize(new { version = "1.0.0", runtime = Runtime }));
        }
        internal void Journal(UpdatePhase phase, string? data = null, string? failure = null)
        {
            Directory.CreateDirectory(Workspace);
            File.WriteAllText(JournalPath, JsonSerializer.Serialize(new UpdateJournal
            {
                InstallationDirectory = Install, DataDirectory = data ?? Data, ExecutableName = ExecutableName,
                Runtime = Runtime, Version = "2.0.0", Phase = phase, Failure = failure,
            }));
        }
        internal void Archive()
        {
            using var file = File.Create(ArchivePath);
            using var zip = new ZipArchive(file, ZipArchiveMode.Create);
            void Add(string name, string value) { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write(value); }
            Add("release-info.json", JsonSerializer.Serialize(new { version = "2.0.0", runtime = Runtime }));
            Add(ExecutableName, "new");
        }
        internal FileStream ExclusiveLease() => new(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        internal Process StartFrontend() => Start("frontend", "--installation", Install, "--data-dir", Data,
            "--pid", Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        internal Process Start(params string[] arguments)
        {
            var directory = Path.GetDirectoryName(typeof(HelperProtocolTests).Assembly.Location)!;
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (var value in new[] { "exec", "--runtimeconfig", Path.Combine(directory, "Winnow.Update.Tests.runtimeconfig.json"),
                "--depsfile", Path.Combine(directory, "Winnow.Update.Tests.deps.json"), typeof(UpdateHelperCommands).Assembly.Location }.Concat(arguments))
                start.ArgumentList.Add(value);
            var child = Process.Start(start)!;
            _children.Add(child);
            return child;
        }
        public void Dispose()
        {
            foreach (var child in _children)
            {
                try { if (!child.HasExited) { child.Kill(entireProcessTree: true); child.WaitForExit(5000); } }
                catch (InvalidOperationException) { }
                child.Dispose();
            }
            Directory.Delete(Root, recursive: true);
        }
    }
}
