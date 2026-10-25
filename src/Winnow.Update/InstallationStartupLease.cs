namespace Winnow.Update;

/// <summary>
/// Holds replacement exclusion when available. Managed installations and read-only copies
/// without an existing lock or recovery journal can run, but cannot replace themselves.
/// </summary>
public sealed class InstallationStartupLease : IDisposable
{
    private readonly IDisposable? _lease;
    public bool IsManaged { get; }
    public bool CanUpdate => _lease is not null && !IsManaged;

    internal InstallationStartupLease(IDisposable? lease, bool managed)
    {
        _lease = lease;
        IsManaged = managed;
    }

    public void Dispose() => _lease?.Dispose();
}
