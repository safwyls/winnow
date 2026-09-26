using Avalonia;
using Winnow.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Winnow.App;

public static class Program
{
    /// <summary>
    /// The generic host backing the app. Built before Avalonia starts;
    /// <see cref="App"/> resolves view models from its service provider.
    /// </summary>
    public static IHost? AppHost { get; private set; }

    /// <summary>
    /// Cancels frontend subscriptions when its lifetime ends. The backend stays independent.
    /// </summary>
    private static readonly CancellationTokenSource Shutdown = new();
    internal static CancellationToken ShutdownToken => Shutdown.Token;

    /// <summary>
    /// The single-instance mutex held for this run (TASK-23): null in a second
    /// copy, which activates the existing session. A static field, not a local in <see
    /// cref="Main"/>, because the mutex protects the process only while the
    /// handle stays open, and a local the JIT considered dead would release it
    /// mid-run.
    /// </summary>
    private static Mutex? SingleInstance;
    internal static SingleInstanceActivation? InstanceActivation { get; private set; }
    private static IDisposable? UpdateLease;
    private static string? UpdateJournalPath;
    internal static bool PortableLeaseAvailable = true;

    internal static void CompleteUpdateStartup()
    {
        if (UpdateJournalPath is { } journal) Winnow.Update.PortableUpdateEngine.MarkReady(journal);
    }

    /// <summary>
    /// Where this run's database, covers, themes and WebView2 profile live, and
    /// whether getting there involved moving them out of the Hoard folder.
    /// Resolved once, in <see cref="Main"/>, before anything is registered.
    /// </summary>
    public static DataLocation DataLocation { get; private set; } =
        new(string.Empty, string.Empty, DataMigrationOutcome.None);

    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            Run(args);
        }
        catch (Exception fault)
        {
            Environment.ExitCode = StartupFailure.Report(
                fault, DataLocation.Root, AppHost is { } host ? LoggerFactoryOrNull(host) : null);
        }
        finally
        {
            AppHost = null;
            InstanceActivation?.Dispose();
            InstanceActivation = null;
            SingleInstance?.Dispose();
            SingleInstance = null;
            UpdateLease?.Dispose();
            UpdateLease = null;
            Shutdown.Dispose();
        }
    }

    private static void Run(string[] args)
    {
        if (!AppActivationRequest.TryReadStartup(args, out var activationRequest))
        {
            Services.ConsoleAuthPrompt.AttachConsoleIfNeeded();
            Console.Error.WriteLine("The Winnow plugin link is invalid. Open the plugins page and try again.");
            Environment.ExitCode = 2;
            return;
        }
        // A second library in the same portable installation must not race replacement.
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "release-info.json")) &&
            !UpdateInstallation.IsManagedLinux(AppContext.BaseDirectory) &&
            !new WindowsUpdateInstaller().IsSupported)
        {
            try { UpdateLease = Winnow.Update.PortableUpdateEngine.AcquireApplicationLease(AppContext.BaseDirectory); }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) &&
                !File.Exists(Winnow.Update.PortableUpdateEngine.GetJournalPath(AppContext.BaseDirectory)))
            {
                // Read-only portable media can still use release links and manual updates.
                PortableLeaseAvailable = false;
            }
            var pendingJournal = Winnow.Update.PortableUpdateEngine.GetJournalPath(AppContext.BaseDirectory);
            if (File.Exists(pendingJournal))
            {
                var phase = Winnow.Update.PortableUpdateEngine.ReadJournal(pendingJournal).Phase;
                if (phase is not (Winnow.Update.UpdatePhase.Staged or Winnow.Update.UpdatePhase.Ready or
                    Winnow.Update.UpdatePhase.Restored or Winnow.Update.UpdatePhase.Installed))
                    throw new IOException("An interrupted portable update needs recovery. Keep the library and update workspace; follow the portable recovery instructions in the release documentation before starting Winnow again.");
            }
        }
        var builder = Host.CreateApplicationBuilder(args);
        // Validate frontend logging before starting a backend that can migrate or refresh data.
        using (var configurationServices = builder.Services.BuildServiceProvider())
            _ = configurationServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<LoggerFilterOptions>>().Value;

        // Data migration and SQLite probes run only in the backend executable.
        try
        {
            DataLocation = Api.BackendProcessLauncher.ResolveDataLocationAsync(args, Shutdown.Token).GetAwaiter().GetResult();
        }
        catch (DataDirectoryOverrideException refused)
        {
            Services.ConsoleAuthPrompt.AttachConsoleIfNeeded();
            Console.Error.WriteLine(refused.Message);
            Environment.ExitCode = 2;
            return;
        }

        // Repeated Avalonia launches activate this frontend; backend ownership has its own guard.
        SingleInstance = Services.SingleInstanceGuard.TryAcquire(DataLocation.Root);
        if (SingleInstance is null)
        {
            if (!SingleInstanceActivation.RequestAsync(DataLocation.Root, activationRequest).GetAwaiter().GetResult())
                System.Diagnostics.Trace.TraceWarning("The existing Winnow session did not acknowledge activation.");
            return;
        }
        InstanceActivation = new SingleInstanceActivation(DataLocation.Root);
        if (activationRequest.Kind != AppActivationKind.Activate) InstanceActivation.Enqueue(activationRequest);

        DiagnosticLogging.Configure(builder.Logging, DataLocation.Root);

        if (UpdateLease is not null)
        {
            var journal = Winnow.Update.PortableUpdateEngine.GetJournalPath(AppContext.BaseDirectory);
            if (File.Exists(journal))
            {
                var state = Winnow.Update.PortableUpdateEngine.ReadJournal(journal);
                if (state.Phase is not (Winnow.Update.UpdatePhase.Staged or Winnow.Update.UpdatePhase.Ready or Winnow.Update.UpdatePhase.Restored))
                {
                    Winnow.Update.PortableUpdateEngine.ValidateStartup(journal, AppContext.BaseDirectory, DataLocation.Root);
                    UpdateJournalPath = journal;
                }
            }
        }

        using var api = Api.BackendProcessLauncher.AttachOrStartAsync(DataLocation.Root, args, Shutdown.Token)
            .GetAwaiter().GetResult();
        Api.FrontendServiceRegistration.AddWinnowFrontend(builder.Services, DataLocation, api, args);
        using var host = builder.Build();
        AppHost = host;
        try
        {
            if (args.Contains(Services.EpicLoginConsole.Argument))
            {
                Environment.ExitCode = Services.EpicLoginConsole.RunAsync(host.Services,
                    Services.EpicLoginConsole.CodeFrom(args), Shutdown.Token).GetAwaiter().GetResult();
                return;
            }
            if (args.Contains(Services.EpicSignInLauncher.Argument))
            {
                Environment.ExitCode = Services.EpicSignInLauncher.Run(host.Services, BuildAvaloniaApp, Shutdown.Token);
                return;
            }
            BuildAvaloniaApp().AfterPlatformServicesSetup(_ => host.Start())
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            Shutdown.Cancel();
            host.StopAsync().GetAwaiter().GetResult();
        }
    }
    private static ILoggerFactory? LoggerFactoryOrNull(IHost host)
    {
        try
        {
            return host.Services.GetService<ILoggerFactory>();
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
