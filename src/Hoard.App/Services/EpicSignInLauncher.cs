using Avalonia;
using Avalonia.Threading;
using Hoard.Ingest.Epic.Web;
using Hoard.Ingest.Epic.Web.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace Hoard.App.Services;

/// <summary>
/// The minimal way to run the embedded-browser sign-in:
/// <c>dotnet run --project src/Hoard.App -- --epic-signin</c>.
///
/// <para><b>This is a trigger, not a feature.</b> Where a sign-in button lives
/// in the UI is a separate decision; this exists so the flow can be exercised
/// end to end — which is the only way the three capture routes can be told
/// apart, since none of them can be settled without a real Epic
/// account.</para>
///
/// <para><b>Why it starts Avalonia by hand instead of running the app.</b> The
/// embedded browser needs a window, and a window needs a running Avalonia
/// application — so unlike <c>--epic-login</c>, this cannot happen before
/// Avalonia starts. But starting the app normally would open the main window,
/// the sync, the scheduler and the session watcher underneath a flow that ends
/// in an exit code. <see cref="AppBuilder.SetupWithoutStarting"/> plus a
/// dispatcher loop gives exactly the window system and nothing else:
/// <c>App.OnFrameworkInitializationCompleted</c> sets a main window only for
/// <c>IClassicDesktopStyleApplicationLifetime</c>, and there is no lifetime
/// here, so it does nothing.</para>
/// </summary>
public static class EpicSignInLauncher
{
    /// <summary>The argument that selects this path.</summary>
    public const string Argument = "--epic-signin";

    /// <summary>
    /// Runs the flow and returns a process exit code: 0 on success, 1 on
    /// anything the user needs to act on.
    /// </summary>
    /// <param name="services">The host container, already migrated.</param>
    /// <param name="avalonia">
    /// <c>Program.BuildAvaloniaApp</c>. Passed in rather than called here so this
    /// file does not own Avalonia configuration — <c>Program</c> does.
    /// </param>
    /// <param name="ct">Cancellation, honoured by closing the window.</param>
    public static int Run(IServiceProvider services, Func<AppBuilder> avalonia, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(avalonia);

        var signIn = services.GetService<EpicSignInService>();
        if (signIn is null)
        {
            Console.Error.WriteLine("Epic sign-in is not registered in this build.");
            return 1;
        }

        ConsoleAuthPrompt.AttachConsoleIfNeeded();
        avalonia().SetupWithoutStarting();

        EpicSignInResult? result = null;
        using var loop = CancellationTokenSource.CreateLinkedTokenSource(ct);

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                result = await signIn.SignInAsync(loop.Token);
            }
            catch (OperationCanceledException)
            {
                // Shutting down. Leaves result null, which reports as a failure
                // below without pretending to know why.
            }
            finally
            {
                // Ends the message loop. Without this the process runs forever
                // with no window on screen — a GUI-subsystem binary, so it would
                // not even be obvious that it was still alive.
                loop.Cancel();
            }
        });

        Dispatcher.UIThread.MainLoop(loop.Token);

        if (result is { Succeeded: true } success)
        {
            Console.WriteLine();
            Console.WriteLine(
                "Signed in as {0}.",
                string.IsNullOrWhiteSpace(success.DisplayName) ? "(no display name)" : success.DisplayName);
            Console.WriteLine(
                success.Persisted
                    ? "The session is stored encrypted with DPAPI and will survive a restart."
                    : "This host cannot encrypt the session at rest, so it is held in memory for this run only.");

            // The finding this whole command exists to produce. Of the three
            // capture routes the embedded browser arms, only two were confirmed
            // by the spike and the third was an untested hypothesis — this line
            // says which one Epic actually exercised.
            Console.WriteLine("Captured by: " + (signIn.LastCaptureRoute ?? "route not recorded"));

            // Same verification report --epic-login ends on. The playtime unit is
            // still a reading rather than a verified fact, and this is the table
            // that settles it — so it must not depend on which sign-in route the
            // user happened to take.
            if (services.GetService<IEpicAccountClient>() is { } account)
            {
                EpicLoginConsole.ReportLibraryAsync(account, services, ct).GetAwaiter().GetResult();
            }

            return 0;
        }

        Console.Error.WriteLine();
        Console.Error.WriteLine(
            result is null
                ? "Sign-in did not complete. Nothing was changed."
                : EpicSignInService.Explain(result.Failure));
        return 1;
    }
}
