using System.Diagnostics;
using Winnow.Api.Client;
using Xunit;

namespace Winnow.Backend.Tests;

public sealed partial class EpicSignInParityTests
{
    [Fact]
    public async Task Terminal_waits_for_delayed_winner_after_the_ownership_loser_has_already_exited()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var polled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var winner = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var polls = 0;
        var pending = ConsoleBackend.WaitForReadyAsync(_ =>
        {
            if (Interlocked.Increment(ref polls) >= 2) polled.TrySetResult();
            return Task.FromResult(winner.Task.IsCompleted);
        }, Task.FromResult("Another Winnow process already owns this data directory."), timeout.Token);
        await polled.Task.WaitAsync(timeout.Token);
        Assert.False(pending.IsCompleted);
        winner.SetResult();
        await pending.WaitAsync(timeout.Token);
        var error = await Assert.ThrowsAsync<IOException>(() => ConsoleBackend.WaitForReadyAsync(
            _ => Task.FromResult(false), Task.FromResult("private startup fixture detail"), timeout.Token));
        Assert.DoesNotContain("private startup fixture detail", error.Message);
    }

    [Theory]
    [InlineData("invalid_client", "Epic rejected the OAuth client itself")]
    [InlineData("invalid_grant", "Epic rejected the code.")]
    public async Task Terminal_preserves_safe_specific_provider_remedies(string providerError, string remedy)
    {
        await using var host = await Host.Start();
        host.Handler.Error = providerError;
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(1, await EpicConsoleCommand.SignInAsync(new ConnectionStoreApi(host.Api), Code,
            TextReader.Null, output, error, _ => Assert.Fail("A supplied code does not open a browser")));
        Assert.Contains(remedy, output.ToString());
        Assert.DoesNotContain(Code, output.ToString() + error.ToString());
    }

    [Theory]
    [InlineData("separate", "", 0)]
    [InlineData("equals", "", 0)]
    [InlineData("stdin", "  SECRET-FIXTURE-CODE  \n", 0)]
    [InlineData("stdin", "", 1)]
    [InlineData("refused", "", 1)]
    public async Task Terminal_executable_uses_selected_real_backend_without_GUI_and_preserves_code_forms_and_EOF(
        string kind, string stdin, int expectedExit)
    {
        await using var host = await Host.Start();
        if (kind == "refused") host.Handler.Error = "invalid_grant";
        var args = new List<string> { "--epic-login", "--data-dir", host.DirectoryPath };
        if (kind is "separate" or "refused") args.AddRange(["--code", Code]);
        if (kind == "equals") args.Add("--code=" + Code);
        var result = await RunConsole(args, stdin);
        Assert.Equal(expectedExit, result.ExitCode);
        Assert.DoesNotContain(Code, result.Output + result.Error);
        Assert.DoesNotContain(Access, result.Output + result.Error);
        Assert.False(Directory.Exists(Path.Combine(host.DirectoryPath, "electron-userdata")));
        Assert.False(Directory.Exists(Path.Combine(host.DirectoryPath, "WebView2")));
        Assert.Contains("/api/v1/connections/stores/sign-in/cancel", host.Routes);
        Assert.Equal("1", (await host.Api.GetHealthAsync()).ApiVersion);
        if (kind == "stdin" && stdin.Length == 0)
        {
            Assert.Empty(host.Handler.Requests);
            Assert.DoesNotContain("Paste the code here:", result.Output);
            Assert.Contains("Sign-in cancelled.", result.Error);
        }
        else
        {
            Assert.Single(host.Handler.Requests);
            Assert.Equal(Code, host.Handler.Requests.Single().Form["code"]);
            Assert.Equal(expectedExit == 0, (await host.Snapshot()).Epic is not null);
        }
    }

    [Fact]
    public async Task Terminal_consent_and_URL_precede_browser_failure_and_the_same_attempt_can_finish()
    {
        await using var host = await Host.Start();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var opened = 0;
        var result = await EpicConsoleCommand.SignInAsync(new ConnectionStoreApi(host.Api), null,
            new StringReader("\n" + Code + "\n"), output, error, url =>
            {
                opened++;
                var printed = output.ToString();
                Assert.True(printed.IndexOf("Winnow is a 3rd party service", StringComparison.Ordinal) < printed.IndexOf(url.AbsoluteUri, StringComparison.Ordinal));
                Assert.Contains(url.AbsoluteUri, printed);
                Assert.Contains("Press Enter", printed);
                throw new InvalidOperationException("browser fixture unavailable");
            });
        Assert.Equal(0, result);
        Assert.Equal(1, opened);
        Assert.Contains("Open the URL above manually.", error.ToString());
        Assert.Contains("Signed in.", output.ToString());
        Assert.Contains("/api/v1/connections/stores/sign-in/cancel", host.Routes);
    }

    [Fact]
    public async Task Terminal_cancellation_of_waiting_stdin_releases_the_real_attempt()
    {
        await using var host = await Host.Start();
        using var input = new BlockingReader();
        using var output = new StringWriter();
        using var error = new StringWriter();
        using var cancellation = new CancellationTokenSource();
        var pending = EpicConsoleCommand.SignInAsync(new ConnectionStoreApi(host.Api), null, input, output, error,
            _ => Assert.Fail("Cancelled input must not open a browser"), cancellation.Token);
        await input.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        Assert.Equal(1, await pending.WaitAsync(TimeSpan.FromSeconds(6)));
        Assert.Contains("/api/v1/connections/stores/sign-in/cancel", host.Routes);
        Assert.Empty(host.Handler.Requests);
        input.Release();
    }

    [Fact]
    public async Task Terminal_refuses_an_unusable_selected_directory_with_exit_two()
    {
        var path = Path.GetTempFileName();
        try
        {
            var result = await RunConsole(["--epic-login", "--data-dir=" + path, "--code=not-sent"], "");
            Assert.Equal(2, result.ExitCode);
            Assert.DoesNotContain("not-sent", result.Output + result.Error);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Terminal_cold_start_leaves_one_independent_backend_healthy_after_EOF(int concurrent)
    {
        var directory = Path.Combine(Path.GetTempPath(), "winnow-console-cold-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var results = await Task.WhenAll(Enumerable.Range(0, concurrent).Select(_ =>
                RunConsole(["--epic-login", "--no-sync", "--data-dir", directory], "")));
            Assert.All(results, result => Assert.True(result.ExitCode == 1,
                $"Expected cancellation exit 1, received {result.ExitCode}: {result.Error}"));
            using var api = WinnowApiClient.Attach(directory);
            Assert.Equal("1", (await api.GetHealthAsync()).ApiVersion);
            Assert.False(Directory.Exists(Path.Combine(directory, "electron-userdata")));
        }
        finally
        {
            if (File.Exists(Path.Combine(directory, "backend", "endpoint.json")))
            {
                var endpoint = await BackendConnection.ReadAsync(directory);
                using var api = WinnowApiClient.Attach(directory);
                using var process = Process.GetProcessById(endpoint.ProcessId);
                await api.SendAsync<object?>(HttpMethod.Post, "lifecycle/shutdown", null);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Terminal_failed_backend_start_keeps_exit_three_and_the_original_database()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winnow-console-startup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var database = Path.Combine(directory, "winnow.db");
        var original = "deliberately invalid SQLite fixture"u8.ToArray();
        await File.WriteAllBytesAsync(database, original);
        try
        {
            var result = await RunConsole(["--epic-login", "--no-sync", "--data-dir", directory], "");
            Assert.Equal(3, result.ExitCode);
            Assert.Contains("could not start", result.Error);
            Assert.Equal(original, await File.ReadAllBytesAsync(database));
            Assert.False(File.Exists(Path.Combine(directory, "backend", "endpoint.json")));
        }
        finally { Directory.Delete(directory, true); }
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunConsole(IEnumerable<string> args, string stdin)
    {
        var info = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        info.ArgumentList.Add(typeof(BackendApplication).Assembly.Location);
        foreach (var arg in args) info.ArgumentList.Add(arg);
        foreach (var key in info.Environment.Keys.ToArray())
            if (new[] { "IGDB", "TWITCH", "STEAM", "EPIC", "GOG" }.Any(prefix =>
                key.StartsWith(prefix + "_", StringComparison.OrdinalIgnoreCase))) info.Environment.Remove(key);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.StandardInput.WriteAsync(stdin);
            process.StandardInput.Close();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            await Task.WhenAll(output, error).WaitAsync(TimeSpan.FromSeconds(5));
            return (process.ExitCode, await output, await error);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        }
    }

    private sealed class BlockingReader : TextReader
    {
        private readonly ManualResetEventSlim _release = new();
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override string? ReadLine() { Entered.TrySetResult(); _release.Wait(); return null; }
        public void Release() => _release.Set();
        protected override void Dispose(bool disposing) { _release.Set(); base.Dispose(disposing); }
    }
}
