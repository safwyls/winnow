using Microsoft.Extensions.Logging;

namespace Winnow.App.Services;

/// <summary>
/// TASK-22 (F36). The error boundary around the startup spine, and the sentence
/// it shows.
///
/// <para>Everything before the window exists runs on one thread with no handler
/// above it: the migration runner, the hosted services, the theme read, the
/// composition root. Winnow is a <c>WinExe</c>, so an exception escaping any of
/// them terminates the process with nothing written anywhere the user can see —
/// no console, no window, no line in a log. A fresh install against a database
/// that failed to migrate, or a library folder that is a stale reparse point,
/// looks from outside like double-clicking an icon and nothing happening.</para>
///
/// <para>Split from the call site so the decision is testable: the boundary
/// itself lives in <c>Program.Main</c>, where it can wrap the spine, but what it
/// decides — shutdown or fault, what it says, what it exits with — is here.</para>
/// </summary>
internal static class StartupFailure
{
    /// <summary>
    /// The exit code a startup fault leaves behind. Distinct from the
    /// <c>--data-dir</c> refusal's 2, so a script driving the app can tell an
    /// unusable override apart from a library that would not open.
    /// </summary>
    internal const int ExitCode = 3;

    /// <summary>The title on the message box, when a message box is the channel.</summary>
    internal const string Title = "Winnow could not start";

    /// <summary>
    /// Whether the exception is the window closing rather than the run failing.
    /// A cancellation during startup means the user quit while the first pass
    /// was still going, which is an ordinary thing to do and not a fault.
    /// </summary>
    internal static bool IsShutdown(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception is OperationCanceledException;
    }

    /// <summary>
    /// What the user is told. The exception's own message is included rather
    /// than summarised: this is the only place the failure is ever stated, and
    /// "an error occurred" would leave nothing to search for or report. The data
    /// directory is named because the overwhelmingly likely cause is the files
    /// in it, and because <c>--data-dir</c> means the failing library may not be
    /// the one the user assumes.
    /// </summary>
    internal static string SentenceFor(Exception exception, string dataDirectory)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(dataDirectory);

        var where = string.IsNullOrWhiteSpace(dataDirectory)
            ? "its data directory"
            : dataDirectory;

        return $"Winnow could not start.{Environment.NewLine}{Environment.NewLine}"
            + $"{exception.GetType().Name}: {exception.Message}{Environment.NewLine}{Environment.NewLine}"
            + $"Your library is at {where} and has not been changed by this run.";
    }

    /// <summary>
    /// Logs the fault and shows it, returning the exit code to leave behind —
    /// or 0 when the "fault" was a shutdown, which is not one.
    ///
    /// <para>Logged first and surfaced second, and neither is allowed to swallow
    /// the other: the logger comes out of a host that may itself be the thing
    /// that failed, and a message box on a machine with no <c>user32</c> is a
    /// missing entry point. A boundary that threw would be worse than the crash
    /// it exists to replace.</para>
    /// </summary>
    /// <param name="exception">What escaped the startup spine.</param>
    /// <param name="dataDirectory">The library this run was opening.</param>
    /// <param name="logs">
    /// The host's logger factory when the host got far enough to have one;
    /// <c>null</c> when it did not, in which case the sentence is the only
    /// record and the channel below is the only place it goes.
    /// </param>
    /// <param name="surface">
    /// The channel, injectable so a test can read what the user would have been
    /// shown without putting a modal dialog on the machine running it.
    /// </param>
    internal static int Report(
        Exception exception,
        string dataDirectory,
        ILoggerFactory? logs,
        Action<string, string>? surface = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(dataDirectory);

        if (IsShutdown(exception))
        {
            return 0;
        }

        var sentence = SentenceFor(exception, dataDirectory);

        try
        {
            logs?.CreateLogger(typeof(Program).FullName!)
                .LogCritical(exception, "Startup failed; the library at {DataDirectory} was not opened.", dataDirectory);
        }
        catch (ObjectDisposedException)
        {
            // The host was disposed underneath us. The channel below still works.
        }

        try
        {
            (surface ?? StartupAlert.Error)(Title, sentence);
        }
        catch (Exception)
        {
            // There is nothing above this to tell. Exiting with the code below
            // is the last thing left that carries the information.
        }

        return ExitCode;
    }
}
