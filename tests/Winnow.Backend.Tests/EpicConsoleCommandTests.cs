using System.Diagnostics;
using System.Text.Json;
using Winnow.Api.Client;
using Xunit;

namespace Winnow.Backend.Tests;

public sealed partial class EpicSignInParityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Terminal_waits_for_its_delayed_candidate_without_killing_it_when_another_owner_is_healthy(bool cancel)
    {
        await using var host = await Host.Start();
        var endpointPath = Path.Combine(host.DirectoryPath, "backend", "endpoint.json");
        var original = await File.ReadAllBytesAsync(endpointPath);
        var connection = await BackendConnection.ReadAsync(host.DirectoryPath);
        using var candidate = Winnow.TestFixtures.ProcessParent.Start("idle");
        Task<string>? output = null;
        var error = candidate.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource();
        Task? pending = null;
        try
        {
            Assert.Equal(candidate.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                await candidate.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)));
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            output = candidate.StandardOutput.ReadToEndAsync();
            // A healthy response from a different discovery generation is not ownership proof.
            await File.WriteAllTextAsync(endpointPath, JsonSerializer.Serialize(
                connection with { Epoch = Guid.NewGuid().ToString("N") }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            pending = ConsoleBackend.WaitForReadyAsync(host.DirectoryPath, candidate, output, error, timeout.Token);
            while (host.Routes.Count(route => route == "/api/v1/health") < 2)
                await Task.Delay(10, timeout.Token);
            Assert.False(pending.IsCompleted);
            Assert.False(candidate.HasExited);

            var polls = host.Routes.Count(route => route == "/api/v1/health");
            await File.WriteAllBytesAsync(endpointPath, original);
            while (host.Routes.Count(route => route == "/api/v1/health") < polls + 2)
                await Task.Delay(10, timeout.Token);
            Assert.False(pending.IsCompleted);
            Assert.False(candidate.HasExited);
            if (cancel)
            {
                timeout.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
                Assert.False(candidate.HasExited);
            }
            candidate.StandardInput.Close();
            if (!cancel) await pending.WaitAsync(timeout.Token);
            await candidate.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await Task.WhenAll(output, error).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(candidate.HasExited);
            Assert.True(output.IsCompletedSuccessfully);
            Assert.True(error.IsCompletedSuccessfully);
            Assert.Equal("1", (await host.Api.GetHealthAsync()).ApiVersion);
            Assert.Equal(Environment.ProcessId, (await BackendConnection.ReadAsync(host.DirectoryPath)).ProcessId);
        }
        finally
        {
            timeout.Cancel();
            // Observe cancellation without replacing an earlier assertion or skipping cleanup.
            if (pending is not null) { try { await pending; } catch { } }
            if (!candidate.HasExited) candidate.Kill();
            await candidate.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await Task.WhenAll(output ?? Task.CompletedTask, error).WaitAsync(TimeSpan.FromSeconds(5));
            await File.WriteAllBytesAsync(endpointPath, original);
        }
    }

    [Fact]
    public async Task Terminal_requires_matching_health_epoch_after_its_real_ownership_loser_has_exited()
    {
        await using var host = await Host.Start();
        var endpointPath = Path.Combine(host.DirectoryPath, "backend", "endpoint.json");
        var original = await File.ReadAllBytesAsync(endpointPath);
        var connection = await BackendConnection.ReadAsync(host.DirectoryPath);
        using var candidate = Winnow.TestFixtures.ProcessParent.Start("backend", "--data-dir", host.DirectoryPath);
        Task<string>? output = null;
        var error = candidate.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource();
        Task? pending = null;
        try
        {
            Assert.Equal(candidate.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                await candidate.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)));
            output = candidate.StandardOutput.ReadToEndAsync();
            await candidate.StandardInput.WriteLineAsync("start");
            candidate.StandardInput.Close();
            await candidate.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await Task.WhenAll(output, error).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotEqual(0, candidate.ExitCode);
            Assert.Contains("already owns this data directory", await error);

            await File.WriteAllTextAsync(endpointPath, JsonSerializer.Serialize(
                connection with { Epoch = Guid.NewGuid().ToString("N") }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            pending = ConsoleBackend.WaitForReadyAsync(host.DirectoryPath, candidate, output, error, timeout.Token);
            while (host.Routes.Count(route => route == "/api/v1/health") < 2)
                await Task.Delay(10, timeout.Token);
            Assert.False(pending.IsCompleted);
            await File.WriteAllBytesAsync(endpointPath, original);
            await pending.WaitAsync(timeout.Token);
            Assert.Equal("1", (await host.Api.GetHealthAsync()).ApiVersion);
        }
        finally
        {
            timeout.Cancel();
            if (pending is not null) { try { await pending; } catch { } }
            if (!candidate.HasExited) candidate.Kill(entireProcessTree: true);
            await candidate.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await Task.WhenAll(output ?? Task.CompletedTask, error).WaitAsync(TimeSpan.FromSeconds(5));
            await File.WriteAllBytesAsync(endpointPath, original);
        }
    }

    [Fact]
    public async Task Terminal_preserves_its_candidate_when_the_old_owner_exits_before_the_candidate_acquires_ownership()
    {
        await using var host = await Host.Start();
        var original = await BackendConnection.ReadAsync(host.DirectoryPath);
        using var candidate = Winnow.TestFixtures.ProcessParent.Start("backend", "--data-dir", host.DirectoryPath);
        Task<string>? output = null;
        var error = candidate.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource();
        Task? pending = null;
        try
        {
            Assert.Equal(candidate.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                await candidate.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)));
            output = candidate.StandardOutput.ReadToEndAsync();
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            pending = ConsoleBackend.WaitForReadyAsync(host.DirectoryPath, candidate, output, error, timeout.Token);
            while (host.Routes.Count(route => route == "/api/v1/health") < 2)
                await Task.Delay(10, timeout.Token);
            Assert.False(pending.IsCompleted);
            Assert.False(candidate.HasExited);

            await host.Stop();
            await candidate.StandardInput.WriteLineAsync("start");
            candidate.StandardInput.Close();
            await pending.WaitAsync(timeout.Token);
            Assert.False(candidate.HasExited);
            Assert.False(output.IsCompleted);
            Assert.False(error.IsCompleted);
            var current = await BackendConnection.ReadAsync(host.DirectoryPath);
            Assert.Equal(candidate.Id, current.ProcessId);
            Assert.NotEqual(original.Epoch, current.Epoch);
            using var api = WinnowApiClient.Attach(host.DirectoryPath);
            Assert.Equal("1", (await api.GetHealthAsync()).ApiVersion);
            await api.SendAsync<object?>(HttpMethod.Post, "lifecycle/shutdown", null);
            await candidate.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await Task.WhenAll(output, error).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, candidate.ExitCode);
        }
        finally
        {
            timeout.Cancel();
            if (pending is not null) { try { await pending; } catch { } }
            if (!candidate.HasExited) candidate.Kill(entireProcessTree: true);
            await candidate.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await Task.WhenAll(output ?? Task.CompletedTask, error).WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

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
