using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Winnow.Api.Contracts.Protocol;

namespace Winnow.Backend;

public sealed class BackendOwnership : IDisposable
{
    private readonly FileStream _lock;
    private readonly Mutex _mutex;
    private readonly string _endpointPath;
    public string DataDirectory { get; }
    public string Token { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public BackendOwnership(string dataDirectory)
    {
        DataDirectory = Path.GetFullPath(dataDirectory);
        Directory.CreateDirectory(DataDirectory);
        var directory = Directory.CreateDirectory(Path.Combine(DataDirectory, "backend"));
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Backend discovery directory cannot be a symbolic link.");
        try { Protect(directory); }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException)
        { throw new IOException("Could not restrict backend discovery to the current user.", exception); }
        _endpointPath = Path.Combine(directory.FullName, "endpoint.json");
        var normalized = DataDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
        var name = "Local\\Winnow.Data." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        _mutex = new Mutex(false, name, out var created);
        if (!created)
        {
            _mutex.Dispose();
            throw new IOException("Another Winnow process already owns this data directory.");
        }
        try
        {
            _lock = new FileStream(Path.Combine(directory.FullName, "owner.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
        }
        catch { _mutex.Dispose(); throw; }
    }

    public void Publish(BackendDiscovery discovery)
    {
        var temporary = _endpointPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(discovery, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        File.Move(temporary, _endpointPath, true);
    }

    private static void Protect(DirectoryInfo directory)
    {
        if (OperatingSystem.IsWindows())
        {
            using var current = WindowsIdentity.GetCurrent();
            var identity = current.User ?? throw new IOException("Current user identity unavailable.");
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            // Being the owner grants WRITE_DAC, but not WRITE_OWNER. Avoid requesting
            // the latter on custom data folders that grant this user only Modify.
            var owner = directory.GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier));
            if (!identity.Equals(owner)) security.SetOwner(identity);
            security.AddAccessRule(new FileSystemAccessRule(identity, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            directory.SetAccessControl(security);
        }
        else
            File.SetUnixFileMode(directory.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    public void Dispose()
    {
        try { File.Delete(_endpointPath); }
        finally { _lock.Dispose(); _mutex.Dispose(); }
    }
}
