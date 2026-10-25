using System.Diagnostics;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Connections;
using Winnow.App.Services;
using Winnow.Core.Auth;
using Winnow.Ingest.Epic.Web.Auth;

namespace Winnow.Backend;

/// <summary>A terminal client of the independent backend, before HTTP-host or GUI startup.</summary>
public static class EpicConsoleCommand
{
    public const string Argument = "--epic-login";

    public static string? CodeFrom(IReadOnlyList<string> args)
    {
        for (var index = 0; index < args.Count; index++)
        {
            if (args[index] == "--code")
                return index + 1 < args.Count && !args[index + 1].StartsWith('-') ? args[index + 1] : null;
            if (args[index].StartsWith("--code=", StringComparison.Ordinal)) return args[index][7..];
        }
        return null;
    }

    public static async Task<int> RunAsync(string[] args)
    {
        TerminalConsole.Attach(PositiveArgument(args, "--terminal-parent-pid"));
        var location = WinnowDataLocation.ResolveFrom(args);
        using var lifetime = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; lifetime.Cancel(); };
        Console.CancelKeyPress += cancel;
        var owner = PositiveArgument(args, "--console-owner-pid");
        var monitor = WatchOwnerAsync(owner, lifetime);
        try
        {
            using var api = await ConsoleBackend.AttachOrStartAsync(location.Root, args, lifetime.Token);
            return await SignInAsync(new ConnectionStoreApi(api), CodeFrom(args), Console.In, Console.Out,
                Console.Error, OpenBrowser, lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            Console.Error.WriteLine(StoreSignInMessages.Cancelled);
            return 1;
        }
        finally
        {
            lifetime.Cancel();
            await monitor;
            Console.CancelKeyPress -= cancel;
        }
    }

    public static async Task<int> SignInAsync(IStoreConnectionApi api, string? presetCode,
        TextReader input, TextWriter output, TextWriter error, Action<Uri> openBrowser,
        CancellationToken ct = default)
    {
        EpicAuthChallenge? challenge = null;
        try
        {
            challenge = await api.BeginEpicAsync(ct);
            var code = presetCode;
            if (string.IsNullOrWhiteSpace(code))
            {
                await output.WriteLineAsync($"Sign in to {challenge.Request.ProviderName}");
                await output.WriteLineAsync(challenge.Request.ConsentNotice);
                await output.WriteLineAsync($"\nOpen this URL and sign in:\n{challenge.Request.StartUrl}\n");
                await output.WriteLineAsync("Copy the authorizationCode value, without quotes. You can also run Winnow --epic-login --code <code>.");
                await output.WriteAsync("Press Enter to open your browser, or paste the code here: ");
                await output.FlushAsync(ct);
                code = await ReadLineAsync(input, ct);
                // EOF is cancellation, not consent to launch a browser.
                if (code is not null && string.IsNullOrWhiteSpace(code))
                {
                    try { openBrowser(challenge.Request.StartUrl); }
                    catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
                        or InvalidOperationException or PlatformNotSupportedException or IOException)
                    { await error.WriteLineAsync("Your browser could not open. Open the URL above manually."); }
                    await output.WriteAsync("Paste the code here: ");
                    await output.FlushAsync(ct);
                    code = await ReadLineAsync(input, ct);
                }
            }
            if (string.IsNullOrWhiteSpace(code))
            {
                await error.WriteLineAsync(StoreSignInMessages.Cancelled);
                return 1;
            }
            if (code.Length > 16384)
            {
                await error.WriteLineAsync("The code is too long. Paste only the authorizationCode value.");
                return 1;
            }
            var result = await api.CompleteEpicAsync(challenge,
                AuthCodeResult.Captured(AuthCodeKind.AuthorizationCode, code.Trim(), "console"), ct);
            await output.WriteLineAsync(result.Succeeded
                ? "Signed in. The backend is refreshing your Epic library."
                : LegacyEpicSignInService.Explain(result.Failure));
            return result.Succeeded ? 0 : 1;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await error.WriteLineAsync(StoreSignInMessages.Cancelled);
            return 1;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            // API exception bodies can contain provider diagnostics. Never print a pasted credential.
            await error.WriteLineAsync("Could not complete Epic sign-in. Check the backend and try again.");
            return 1;
        }
        finally
        {
            if (challenge is not null)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try { await api.CancelAsync(challenge.AttemptId, cleanup.Token); }
                catch (Exception exception) when (exception is HttpRequestException or IOException or OperationCanceledException) { }
            }
        }
    }

    private static Task<string?> ReadLineAsync(TextReader input, CancellationToken ct) =>
        // Console's synchronized reader can block before returning its async operation. Keep that
        // wait off the lifetime thread so Ctrl+C/owner death still cancels and releases the attempt.
        Task.Run(input.ReadLine, CancellationToken.None).WaitAsync(ct);

    private static void OpenBrowser(Uri uri)
    {
        using var process = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    private static int? PositiveArgument(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        if (index < 0) return null;
        if (index + 1 < args.Length && int.TryParse(args[index + 1], out var value) && value > 0) return value;
        throw new ArgumentException($"Invalid {name}.");
    }

    private static async Task WatchOwnerAsync(int? pid, CancellationTokenSource lifetime)
    {
        if (pid is null) return;
        try
        {
            using var process = Process.GetProcessById(pid.Value);
            await process.WaitForExitAsync(lifetime.Token);
            lifetime.Cancel();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (ArgumentException) { lifetime.Cancel(); }
    }
}
