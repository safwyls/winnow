using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Winnow.Auth.WebView;

/// <summary>
/// A browser profile directory that exists for exactly one operation.
///
/// <para>The second half of the ephemerality guarantee. The first half is
/// <see cref="WebView2Host"/>'s private mode, which keeps cookies, history and
/// cache off disk; this makes sure that whatever Chromium writes anyway
/// (crash-reporter state, shader caches, lock files) goes somewhere nobody will
/// look for it again and is gone when the operation ends.</para>
///
/// <para>Deleting is retried rather than attempted once. The browser process
/// releases its handles a moment after the controller closes, so the first
/// attempt loses a race it would be wrong to lose quietly. Retries are the
/// difference between "usually clean" and "clean".</para>
/// </summary>
internal sealed class EphemeralBrowserProfile
{
    private const int DeleteAttempts = 12;
    private static readonly TimeSpan DeleteRetryDelay = TimeSpan.FromMilliseconds(250);

    private readonly ILogger _log;

    private EphemeralBrowserProfile(string path, ILogger log)
    {
        Path = path;
        _log = log;
    }

    /// <summary>The directory to hand WebView2. Created by <see cref="Create"/>, deleted by <see cref="DeleteAsync"/>.</summary>
    public string Path { get; }

    /// <summary>
    /// Makes a directory nothing else will ever use.
    /// </summary>
    /// <param name="root">Where to put it. Typically the machine's temp directory.</param>
    /// <param name="log">Optional. Never told what the session did, only whether the directory went away.</param>
    public static EphemeralBrowserProfile Create(string root, ILogger? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        // A GUID rather than a stable name: two harvests running at once must not
        // share a profile, and a leftover directory from a previous run must not
        // be reused as one.
        var path = System.IO.Path.Combine(
            root, "winnow-steam-harvest-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(path);
        return new EphemeralBrowserProfile(path, log ?? NullLogger.Instance);
    }

    /// <summary>
    /// Deletes the directory, retrying while the browser process lets go.
    ///
    /// <para>Never throws. A profile that outlives its operation is a fault worth
    /// a log line, not a reason to fail a harvest that has already produced its
    /// answer.</para>
    /// </summary>
    public async Task<bool> DeleteAsync()
    {
        for (var attempt = 0; attempt < DeleteAttempts; attempt++)
        {
            try
            {
                if (!Directory.Exists(Path))
                {
                    return true;
                }

                Directory.Delete(Path, recursive: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or DirectoryNotFoundException)
            {
                // Chromium still holds a lock file, or a scanner has the
                // directory open. Both clear on their own within a second or two.
                await Task.Delay(DeleteRetryDelay).ConfigureAwait(false);
            }
        }

        // The path is not logged: it names a directory, not a session, but there
        // is nothing to be gained from writing either into a log file.
        _log.LogWarning(
            "Could not delete the temporary browser profile for the Steam page session after {Attempts} "
            + "attempts. It holds no cookies (the session was private) but it should not be there.",
            DeleteAttempts);

        return false;
    }
}
