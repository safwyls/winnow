using Winnow.Update;

namespace Winnow.Backend;

internal sealed class BackendInstallationLease : IDisposable
{
    private readonly IDisposable? _lease;
    public string InstallationDirectory { get; }

    public BackendInstallationLease() : this(AppContext.BaseDirectory) { }

    internal BackendInstallationLease(string executableDirectory)
    {
        InstallationDirectory = FindInstallationDirectory(executableDirectory);
        if (File.Exists(Path.Combine(InstallationDirectory, "release-info.json")))
            _lease = PortableUpdateEngine.AcquireStartupLease(InstallationDirectory);
    }

    internal static string FindInstallationDirectory(string executableDirectory)
    {
        var directory = Path.GetFullPath(executableDirectory);
        if (File.Exists(Path.Combine(directory, "release-info.json"))) return directory;
        // Only the two shipped backend layouts may borrow an enclosing installation lease.
        var current = new DirectoryInfo(directory);
        if (current.Name.Equals("backend", StringComparison.OrdinalIgnoreCase) && current.Parent is { } parent)
        {
            if (File.Exists(Path.Combine(parent.FullName, "release-info.json"))) return parent.FullName;
            if (parent.Name.Equals("resources", StringComparison.OrdinalIgnoreCase) && parent.Parent is { } root &&
                File.Exists(Path.Combine(root.FullName, "release-info.json"))) return root.FullName;
        }
        return directory;
    }

    public void Dispose() => _lease?.Dispose();
}
