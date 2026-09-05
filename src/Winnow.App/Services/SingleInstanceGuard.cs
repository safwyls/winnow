using System.Security.Cryptography;
using System.Text;

namespace Winnow.App.Services;

/// <summary>
/// TASK-23 (F39). One Winnow per data directory. Everything that writes the
/// library — the session watcher, the snapshot scheduler, the remote
/// ownership scheduler, the update poller — is a per-process singleton, so a
/// second copy against the same files double-records sessions, races the
/// SQLite writer, and doubles the external request traffic against services
/// that are rate-limited by volunteer goodwill.
/// <para>
/// Keyed on the data directory rather than on the product: two copies against
/// the SAME files are the failure, and a second copy pointed at a throwaway
/// <c>--data-dir</c> (the documented way to click around safely) is not one.
/// </para>
/// </summary>
internal static class SingleInstanceGuard
{
    /// <summary>
    /// Acquires the guard, or returns <c>null</c> when the name already exists —
    /// which means another copy is running against this data directory, and is
    /// the caller's cue to show a sentence and exit. The caller must keep the
    /// returned mutex alive for the process lifetime: a collected <see
    /// cref="Mutex"/>'s finalizer would close the handle and release the mutex
    /// while the process was still running.
    /// </summary>
    public static Mutex? TryAcquire(string dataDirectory)
    {
        ArgumentNullException.ThrowIfNull(dataDirectory);

        // createdNew, not WaitOne: a mutex's ownership is per THREAD, so a
        // second WaitOne on the same thread would succeed against a mutex
        // that thread already holds, and the test that has to demonstrate the
        // refusal runs on one thread. Whether the NAME already existed is per
        // object, and that is the question a second copy has to be refused on.
        // A crashed holder needs no AbandonedMutexException handling either:
        // the named mutex is destroyed when the last handle closes with the
        // process, so the next launch finds no name and starts clean.
        var mutex = new Mutex(initiallyOwned: true, NameFor(dataDirectory), out var createdNew);
        if (!createdNew)
        {
            // Another copy created it first: this process is the second one.
            mutex.Dispose();
            return null;
        }

        return mutex;
    }

    /// <summary>
    /// The sentence a second copy shows before it exits. Channel selection —
    /// console when a terminal is present, message box when there is none —
    /// is <see cref="StartupAlert"/>'s concern.
    /// </summary>
    public static void RefuseToStart(string dataDirectory)
    {
        var text = $"Winnow is already running against {dataDirectory}." +
            " Close the other copy and try again.";

        StartupAlert.Notice("Winnow is already running", text);
    }

    private static string NameFor(string dataDirectory)
    {
        // Local\, not Global\: the two copies that happen are one user's, in
        // one login session, and a machine-wide mutex would also pin a second
        // user's unrelated %LOCALAPPDATA% behind fast-user-switching.
        var hash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(Normalized(dataDirectory))));
        return $"Local\\Winnow.Data.{hash}";
    }

    private static string Normalized(string dataDirectory)
    {
        // A trailing separator and a case difference are the difference
        // between two spellings of one directory on Windows, and the mutex
        // name has to agree about it.
        return Path.GetFullPath(dataDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
    }
}
