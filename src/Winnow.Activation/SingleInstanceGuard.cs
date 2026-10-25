using System.Security.Cryptography;
using System.Text;

namespace Winnow.App.Services;

/// <summary>
/// Activates the existing named frontend for this data directory. Other
/// frontend applications can connect alongside it. The backend owns a separate
/// guard for database access and background workers.
/// </summary>
internal static class SingleInstanceGuard
{
    /// <summary>
    /// Acquires the guard, or returns <c>null</c> when the name already exists —
    /// which means another copy is running against this data directory, and is
    /// the caller's cue to activate that copy and exit. The caller must keep the
    /// returned mutex alive for the process lifetime: a collected <see
    /// cref="Mutex"/>'s finalizer would close the handle and release the mutex
    /// while the process was still running.
    /// </summary>
    public static Mutex? TryAcquire(string dataDirectory, string frontend = "Avalonia")
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
        bool createdNew;
        var mutex = OperatingSystem.IsWindows()
            ? WindowsActivationSecurity.CreateMutex(NameFor(dataDirectory, frontend), out createdNew)
            : new Mutex(initiallyOwned: true, NameFor(dataDirectory, frontend), out createdNew);
        if (!createdNew)
        {
            // Another copy created it first: this process is the second one.
            mutex.Dispose();
            return null;
        }

        return mutex;
    }

    internal static string ActivationNameFor(string dataDirectory, string frontend = "Avalonia")
        => NameFor(dataDirectory, frontend).Replace("Local\\", "") + ".Activate";

    internal static string NameFor(string dataDirectory, string frontend = "Avalonia")
    {
        if (frontend is not ("Avalonia" or "Electron"))
            throw new ArgumentException("Unknown frontend activation namespace.", nameof(frontend));
        // Local\, not Global\: the two copies that happen are one user's, in
        // one login session, and a machine-wide mutex would also pin a second
        // user's unrelated %LOCALAPPDATA% behind fast-user-switching.
        var hash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(Normalized(dataDirectory))));
        return $"Local\\Winnow.{frontend}.{hash}";
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
