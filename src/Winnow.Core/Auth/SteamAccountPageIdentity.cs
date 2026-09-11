namespace Winnow.Core.Auth;

/// <summary>
/// Keeps both captured pages in one observed account context. A changed or lost
/// identity poisons the capture, so earlier HTML cannot escape with a later label.
/// </summary>
public sealed class SteamAccountPageIdentity
{
    private bool _started;
    public string? SteamId { get; private set; }
    public bool Rejected { get; private set; }

    public bool TryAccept(string? expected, string? before, string? after)
    {
        expected = Clean(expected);
        before = Clean(before);
        after = Clean(after);
        if (Rejected || !string.Equals(before, after, StringComparison.Ordinal)
            || expected is not null && !string.Equals(expected, before, StringComparison.Ordinal)
            || _started && !string.Equals(SteamId, before, StringComparison.Ordinal))
        {
            Rejected = true;
            return false;
        }
        _started = true;
        SteamId = before;
        return true;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    public override string ToString() => $"SteamAccountPageIdentity(known={SteamId is not null}, rejected={Rejected})";
}
