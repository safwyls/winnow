namespace Winnow.App.Services;

/// <summary>Which launcher's local files a fingerprint describes.</summary>
public enum LibraryScanSource
{
    Steam,
    Epic,
}

/// <summary>
/// The launcher file state one scan-and-resolve pass read, as the install
/// fingerprints report it. A null means the reader could not produce a complete
/// inventory, which is never the same as "nothing changed".
/// </summary>
public readonly record struct LibraryScanState(string? Steam, string? Epic);

/// <summary>
/// What the local scans have already read, shared by every trigger that can
/// start one.
///
/// <para>Four passes over byte-identical files used to run inside the first five
/// seconds of a launch: the startup pipeline's local sync, the remote backfill's
/// install-state re-read on the way back from HTTP, and the first stable
/// manifest read of each install watcher — which treats whatever it finds at
/// launch as a change, because it has published nothing yet. None of the four
/// triggers can be dropped; each covers a case the others do not. They coalesce
/// onto the first pass here instead.</para>
///
/// <para><see cref="Read"/> is called BEFORE a pass scans, never after. A
/// manifest rewritten while the pass runs then leaves a fingerprint that does
/// not match what was covered, so the watcher still publishes the change;
/// recording the post-scan state would silently swallow it.</para>
/// </summary>
public sealed class LibraryScanBaseline
{
    private readonly Func<string?> _steamFingerprint;
    private readonly Func<string?> _epicFingerprint;
    private readonly object _sync = new();

    private LibraryScanState _covered;
    private bool _anyPassCompleted;
    private int _expected;

    /// <param name="steamFingerprint">
    /// Steam's install fingerprint reader, or null in a test that only needs the
    /// expectation half. Both readers default to answering null, which reports
    /// every state as moved and so leaves the pre-coalescing behaviour intact.
    /// </param>
    /// <param name="epicFingerprint">Epic's manifest fingerprint reader.</param>
    public LibraryScanBaseline(
        Func<string?>? steamFingerprint = null,
        Func<string?>? epicFingerprint = null)
    {
        _steamFingerprint = steamFingerprint ?? (static () => null);
        _epicFingerprint = epicFingerprint ?? (static () => null);
    }

    /// <summary>Reads the launcher state a pass is about to cover.</summary>
    public LibraryScanState Read() => new(_steamFingerprint(), _epicFingerprint());

    /// <summary>Records the state a completed pass covered.</summary>
    public void Publish(LibraryScanState state)
    {
        lock (_sync)
        {
            _covered = state;
            _anyPassCompleted = true;
        }
    }

    /// <summary>
    /// True when a completed pass read exactly this launcher state. A null
    /// fingerprint is never covered: the readers use null for "could not read a
    /// complete inventory", and a watcher must not adopt an answer nobody has.
    /// </summary>
    public bool Covered(LibraryScanSource source, string? fingerprint)
    {
        if (fingerprint is null)
        {
            return false;
        }

        lock (_sync)
        {
            if (!_anyPassCompleted)
            {
                return false;
            }

            var known = source is LibraryScanSource.Steam ? _covered.Steam : _covered.Epic;
            return string.Equals(known, fingerprint, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// True while a pass that has not finished yet is expected to. The watchers
    /// wait rather than scan: at launch the startup pipeline is usually still
    /// mid-scan, and a watcher that stops waiting reads the same files again.
    /// </summary>
    public bool PassExpected => Volatile.Read(ref _expected) > 0;

    /// <summary>
    /// Declares that a pass is about to run. Dispose the result when it has
    /// finished OR failed — a watcher must not wait forever on a pipeline that
    /// threw, so the caller's finally block owns this, not its success path.
    /// Disposing twice is a no-op.
    /// </summary>
    public IDisposable Expect()
    {
        Interlocked.Increment(ref _expected);
        return new Expectation(this);
    }

    private sealed class Expectation(LibraryScanBaseline owner) : IDisposable
    {
        private LibraryScanBaseline? _owner = owner;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _owner, null) is { } baseline)
            {
                Interlocked.Decrement(ref baseline._expected);
            }
        }
    }
}
