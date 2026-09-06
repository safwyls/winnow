using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Winnow.App.Services;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// TASK-22 (F36), the decision half: what the boundary around the startup spine
/// does with what escapes it.
///
/// <para>The boundary itself is three lines in <c>Program.Main</c> and cannot be
/// entered without launching a process. What it DECIDES — shutdown or fault,
/// what the user is told, what the exit code carries — is <see
/// cref="StartupFailure"/>, and that is what these hold.</para>
/// </summary>
public class StartupErrorBoundaryTests
{
    /// <summary>The channel, captured instead of shown, so no test puts a modal
    /// dialog on the machine running it.</summary>
    private sealed class Channel
    {
        internal readonly List<(string Title, string Text)> Shown = [];

        internal Action<string, string> Surface => (title, text) => Shown.Add((title, text));
    }

    [Fact]
    public void A_startup_fault_is_surfaced_and_leaves_a_nonzero_exit_code()
    {
        var channel = new Channel();

        var code = StartupFailure.Report(
            new InvalidOperationException("the migration runner gave up"),
            @"C:\Users\someone\AppData\Local\Winnow",
            NullLoggerFactory.Instance,
            channel.Surface);

        // AC3: the fault reaches the user rather than ending the process in
        // silence, and the exit code says so to whatever launched it.
        Assert.Equal(StartupFailure.ExitCode, code);
        var (title, _) = Assert.Single(channel.Shown);
        Assert.Equal(StartupFailure.Title, title);
    }

    [Fact]
    public void The_sentence_names_the_failure_and_the_library_it_failed_on()
    {
        var sentence = StartupFailure.SentenceFor(
            new InvalidOperationException("the migration runner gave up"),
            @"C:\throwaway\winnow");

        // The exception's own message, verbatim: this is the only place the
        // failure is ever stated, so a summary would leave nothing to search
        // for or report.
        Assert.Contains("the migration runner gave up", sentence, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), sentence, StringComparison.Ordinal);

        // And WHICH library, because --data-dir means the one that failed may
        // not be the one the user assumes.
        Assert.Contains(@"C:\throwaway\winnow", sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void A_library_with_no_resolved_path_still_produces_a_readable_sentence()
    {
        // DataLocation.Root is the empty string until Main resolves it, so a
        // failure early enough to precede that must not show a dangling "at ."
        var sentence = StartupFailure.SentenceFor(new IOException("no"), string.Empty);

        Assert.Contains("its data directory", sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void Closing_the_window_mid_startup_is_a_shutdown_and_not_a_fault()
    {
        var channel = new Channel();

        var code = StartupFailure.Report(
            new OperationCanceledException(),
            @"C:\throwaway\winnow",
            NullLoggerFactory.Instance,
            channel.Surface);

        // Quitting while the first pass is still running is an ordinary thing
        // to do. Nothing is shown and the exit code stays clean.
        Assert.Equal(0, code);
        Assert.Empty(channel.Shown);
        Assert.True(StartupFailure.IsShutdown(new TaskCanceledException()));
    }

    [Fact]
    public void A_failure_early_enough_to_have_no_logger_is_still_surfaced()
    {
        var channel = new Channel();

        // The host is the thing that failed, so there is no logger factory to
        // ask. AC2's "logged" has nowhere to go; "surfaced" still must.
        var code = StartupFailure.Report(
            new InvalidOperationException("the container would not build"),
            @"C:\throwaway\winnow",
            logs: null,
            channel.Surface);

        Assert.Equal(StartupFailure.ExitCode, code);
        Assert.Single(channel.Shown);
    }

    [Fact]
    public void The_boundary_never_becomes_the_second_exception()
    {
        // A logger out of a half-disposed host and a message box on a machine
        // with no user32 are both real. A boundary that threw would be worse
        // than the crash it replaces, so the exit code has to survive both
        // halves failing.
        var code = StartupFailure.Report(
            new InvalidOperationException("first"),
            @"C:\throwaway\winnow",
            new ThrowingLoggerFactory(),
            (_, _) => throw new EntryPointNotFoundException("no message box here"));

        Assert.Equal(StartupFailure.ExitCode, code);
    }

    [Fact]
    public void The_startup_fault_code_is_distinct_from_the_data_dir_refusal()
    {
        // Program.Main exits 2 for an unusable --data-dir. A script driving the
        // app has to be able to tell "you pointed me somewhere I cannot use"
        // apart from "the library would not open".
        Assert.NotEqual(2, StartupFailure.ExitCode);
        Assert.NotEqual(0, StartupFailure.ExitCode);
    }

    private sealed class ThrowingLoggerFactory : ILoggerFactory
    {
        public void AddProvider(ILoggerProvider provider) => throw new ObjectDisposedException(nameof(ThrowingLoggerFactory));

        public ILogger CreateLogger(string categoryName) => throw new ObjectDisposedException(nameof(ThrowingLoggerFactory));

        public void Dispose()
        {
        }
    }
}
