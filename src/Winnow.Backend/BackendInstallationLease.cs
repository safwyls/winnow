using Winnow.Update;

namespace Winnow.Backend;

internal sealed class BackendInstallationLease : IDisposable
{
    private readonly IDisposable? _lease;
    public string InstallationDirectory { get; } = FindInstallationDirectory(AppContext.BaseDirectory);

    public BackendInstallationLease()
    {
        if (File.Exists(Path.Combine(InstallationDirectory, "release-info.json")))
            _lease = PortableUpdateEngine.AcquireApplicationLease(InstallationDirectory);
    }

    internal static string FindInstallationDirectory(string executableDirectory)
    {
        var directory = Path.GetFullPath(executableDirectory);
        var parent = Directory.GetParent(Path.TrimEndingDirectorySeparator(directory))?.FullName;
        return parent is not null && File.Exists(Path.Combine(parent, "release-info.json")) ? parent : directory;
    }

    public void Dispose() => _lease?.Dispose();
}
