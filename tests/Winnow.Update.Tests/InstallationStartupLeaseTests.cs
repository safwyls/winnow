using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Winnow.Backend;
using Winnow.Update.Helper;
using Xunit;

namespace Winnow.Update.Tests;

public sealed class LeasePermissionTheoryAttribute : TheoryAttribute
{
    public LeasePermissionTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
            Skip = "Permission proof covers Windows ACLs and Linux mode bits.";
        else if (OperatingSystem.IsLinux() && GetEffectiveUserId() == 0)
            Skip = "Run as a non-root user so Linux mode bits can deny creation.";
    }

    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEffectiveUserId();
}

public sealed class InstallationStartupLeaseTests
{
    [LeasePermissionTheory]
    [InlineData("", false)]
    [InlineData("backend", false)]
    [InlineData("resources/backend", false)]
    [InlineData("", true)]
    [InlineData("backend", true)]
    [InlineData("resources/backend", true)]
    public async Task ManagedFrontendAndBackendNeverCreateAnAdjacentLockInAnySupportedLayout(string layout, bool manifestMarker)
    {
        using var fixture = new Fixture();
        fixture.Managed(manifestMarker);
        var backend = fixture.Backend(layout);
        using var denied = fixture.DenySiblingWrites();
        fixture.AssertCreationDenied();
        using (var lease = PortableUpdateEngine.AcquireStartupLease(fixture.Install))
        {
            Assert.True(lease.IsManaged);
            Assert.False(lease.CanUpdate);
        }
        using (var lease = new BackendInstallationLease(backend))
            Assert.Equal(fixture.Install, lease.InstallationDirectory);
        var frame = await fixture.Frontend();
        Assert.False(frame.GetProperty("canUpdate").GetBoolean());
        Assert.Equal(JsonValueKind.Null, frame.GetProperty("recoveryStatus").ValueKind);
        fixture.AssertUntouched();
    }

    [LeasePermissionTheory]
    [InlineData("")]
    [InlineData("backend")]
    [InlineData("resources/backend")]
    public async Task ActualCreationDenialAllowsManualUpdateStartupWithoutAnExistingLockOrJournal(string layout)
    {
        using var fixture = new Fixture();
        var backend = fixture.Backend(layout);
        using var denied = fixture.DenySiblingWrites();
        fixture.AssertCreationDenied();
        using (var lease = PortableUpdateEngine.AcquireStartupLease(fixture.Install))
        {
            Assert.False(lease.IsManaged);
            Assert.False(lease.CanUpdate);
        }
        using (var lease = new BackendInstallationLease(backend))
            Assert.Equal(fixture.Install, lease.InstallationDirectory);
        var frame = await fixture.Frontend();
        Assert.False(frame.GetProperty("canUpdate").GetBoolean());
        Assert.Contains("manual update", frame.GetProperty("recoveryStatus").GetString());
        fixture.AssertUntouched();
    }

    [LeasePermissionTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManagedOrReadOnlyStartupNeverBypassesAnyExistingJournal(bool managed)
    {
        using var fixture = new Fixture();
        if (managed) fixture.Managed(manifestMarker: false);
        Directory.CreateDirectory(fixture.Workspace);
        var backend = fixture.Backend("backend");
        using var denied = fixture.DenySiblingWrites();
        fixture.AssertCreationDenied();
        foreach (var phase in Enum.GetValues<UpdatePhase>())
        {
            fixture.Journal(phase);
            var before = File.ReadAllBytes(fixture.JournalPath);
            Assert.ThrowsAny<Exception>(() => new BackendInstallationLease(backend));
            using var output = new StringWriter();
            await Assert.ThrowsAnyAsync<Exception>(() => fixture.Frontend(output));
            Assert.Empty(output.ToString());
            Assert.Equal(before, File.ReadAllBytes(fixture.JournalPath));
            Assert.False(File.Exists(fixture.LockPath));
            Assert.Empty(Directory.GetFileSystemEntries(fixture.Data));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ARealExclusiveReplacementLockNeverBecomesReadOnlyFallback(bool managed, bool journal)
    {
        using var fixture = new Fixture();
        if (managed) fixture.Managed(manifestMarker: false);
        if (journal) fixture.Journal(UpdatePhase.Installed);
        var before = journal ? File.ReadAllBytes(fixture.JournalPath) : null;
        using var locked = fixture.Exclusive();
        Assert.Throws<IOException>(() => PortableUpdateEngine.AcquireStartupLease(fixture.Install));
        Assert.Throws<IOException>(() => new BackendInstallationLease(fixture.Backend("resources/backend")));
        using var output = new StringWriter();
        await Assert.ThrowsAsync<IOException>(() => fixture.Frontend(output));
        Assert.Empty(output.ToString());
        if (journal) Assert.Equal(before, File.ReadAllBytes(fixture.JournalPath));
        Assert.Empty(Directory.GetFileSystemEntries(fixture.Data));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingReadableLocksRemainHeldEvenForManagedInstallations(bool managed)
    {
        using var fixture = new Fixture();
        if (managed) fixture.Managed(manifestMarker: false);
        using (fixture.Exclusive()) { }
        using (var frontend = PortableUpdateEngine.AcquireStartupLease(fixture.Install))
        {
            Assert.Equal(!managed, frontend.CanUpdate);
            using (var backend = new BackendInstallationLease(fixture.Backend("backend")))
                Assert.Throws<IOException>(() => fixture.Exclusive());
            Assert.Throws<IOException>(() => fixture.Exclusive());
        }
        using var released = fixture.Exclusive();
    }

    [Fact]
    public async Task AnInvalidLockEntryIsNotReadOnlyMedia()
    {
        using var fixture = new Fixture();
        Directory.CreateDirectory(fixture.LockPath);
        Assert.ThrowsAny<Exception>(() => new BackendInstallationLease(fixture.Backend("backend")));
        using var output = new StringWriter();
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Frontend(output));
        Assert.Empty(output.ToString());
        Assert.Empty(Directory.GetFileSystemEntries(fixture.Data));
    }

    private sealed class Fixture : IDisposable
    {
        private string Root { get; } = Path.Combine(Path.GetTempPath(), "winnow-startup-lease-" + Guid.NewGuid().ToString("N"));
        internal string Install => Path.Combine(Root, "installed");
        internal string Data => Path.Combine(Root, "data");
        internal string JournalPath => PortableUpdateEngine.GetJournalPath(Install);
        internal string Workspace => Path.GetDirectoryName(JournalPath)!;
        internal string LockPath => Workspace + ".lock";
        internal Fixture()
        {
            Directory.CreateDirectory(Install);
            Directory.CreateDirectory(Data);
            File.WriteAllText(Path.Combine(Install, "release-info.json"), "{}");
        }
        internal string Backend(string layout)
        {
            var path = Path.Combine(Install, layout);
            Directory.CreateDirectory(path);
            return path;
        }
        internal void Managed(bool manifestMarker)
        {
            if (manifestMarker) File.WriteAllText(Path.Combine(Install, "release-info.json"), "{\"package-managed\":true}");
            else File.WriteAllText(Path.Combine(Install, "package-managed"), "deb");
        }
        internal void Journal(UpdatePhase phase) => File.WriteAllText(JournalPathWithDirectory(), JsonSerializer.Serialize(new UpdateJournal
        {
            InstallationDirectory = Install, DataDirectory = Data, ExecutableName = "Winnow",
            Runtime = "linux-x64", Version = "2.0.0", Phase = phase,
        }));
        private string JournalPathWithDirectory() { Directory.CreateDirectory(Workspace); return JournalPath; }
        internal FileStream Exclusive() => new(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        internal async Task<JsonElement> Frontend(StringWriter? suppliedOutput = null)
        {
            using var parent = Process.GetCurrentProcess();
            using var input = new StringReader("");
            using var output = suppliedOutput ?? new StringWriter();
            await FrontendInstallationHost.RunAsync(Install, Data, parent, input, output);
            using var document = JsonDocument.Parse(output.ToString());
            return document.RootElement.Clone();
        }
        internal IDisposable DenySiblingWrites()
        {
            if (OperatingSystem.IsWindows()) return new WindowsWriteDenial(Root);
            if (OperatingSystem.IsLinux()) return new LinuxWriteDenial(Root);
            throw new PlatformNotSupportedException();
        }
        internal void AssertCreationDenied() => Assert.Throws<UnauthorizedAccessException>(() => File.WriteAllText(Path.Combine(Root, "permission-probe"), "probe"));
        internal void AssertUntouched()
        {
            Assert.False(File.Exists(LockPath));
            Assert.False(Directory.Exists(Workspace));
            Assert.Empty(Directory.GetFileSystemEntries(Data));
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    [SupportedOSPlatform("windows")]
    private sealed class WindowsWriteDenial : IDisposable
    {
        private readonly DirectoryInfo _directory;
        private readonly DirectorySecurity _original;
        internal WindowsWriteDenial(string path)
        {
            _directory = new DirectoryInfo(path);
            _original = _directory.GetAccessControl(AccessControlSections.Access);
            var changed = _directory.GetAccessControl(AccessControlSections.Access);
            using var identity = WindowsIdentity.GetCurrent();
            changed.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.CreateFiles | FileSystemRights.CreateDirectories,
                InheritanceFlags.None, PropagationFlags.None, AccessControlType.Deny));
            _directory.SetAccessControl(changed);
        }
        public void Dispose() => _directory.SetAccessControl(_original);
    }

    [SupportedOSPlatform("linux")]
    private sealed class LinuxWriteDenial : IDisposable
    {
        private readonly string _path;
        private readonly UnixFileMode _mode;
        internal LinuxWriteDenial(string path)
        {
            _path = path;
            _mode = File.GetUnixFileMode(path);
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.GroupRead |
                UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        public void Dispose() => File.SetUnixFileMode(_path, _mode);
    }
}
