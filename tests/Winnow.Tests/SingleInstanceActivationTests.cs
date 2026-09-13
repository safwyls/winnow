using Winnow.App.Services;
using Xunit;

namespace Winnow.Tests;

public sealed class SingleInstanceActivationTests
{
    private static string DirectoryName() => Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Second_process_activates_owner_and_exits_before_database_initialization()
    {
        var directory = DirectoryName();
        using var guard = SingleInstanceGuard.TryAcquire(directory);
        Assert.NotNull(guard);
        using var server = new SingleInstanceActivation(directory);
        var activated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.SetHandler(() => activated.TrySetResult());
        var start = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        var testAssembly = typeof(SingleInstanceActivationTests).Assembly.Location;
        foreach (var argument in new[] { "exec", "--runtimeconfig", Path.ChangeExtension(testAssembly, ".runtimeconfig.json"),
                     "--depsfile", Path.ChangeExtension(testAssembly, ".deps.json"),
                     typeof(Winnow.App.Program).Assembly.Location, "--data-dir", directory })
            start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await process.WaitForExitAsync(deadline.Token);
            Assert.True(process.ExitCode == 0, await errors);
            Assert.True(activated.Task.IsCompleted, await output);
            Assert.False(File.Exists(Path.Combine(directory, "winnow.db")));
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Launches_queue_until_ready_and_repeated_requests_reach_the_same_session()
    {
        var directory = DirectoryName();
        using var server = new SingleInstanceActivation(directory);
        Assert.True(await SingleInstanceActivation.RequestAsync(directory));
        Assert.True(await SingleInstanceActivation.RequestAsync(directory + Path.DirectorySeparatorChar));
        var activations = 0;
        server.SetHandler(() => Interlocked.Increment(ref activations));
        Assert.Equal(1, activations);
        Assert.True(await SingleInstanceActivation.RequestAsync(directory));
        Assert.True(await SingleInstanceActivation.RequestAsync(directory));
        Assert.Equal(3, activations);
    }

    [Fact]
    public async Task Connection_waits_for_listener_startup()
    {
        var directory = DirectoryName();
        var request = SingleInstanceActivation.RequestAsync(directory);
        using var server = new SingleInstanceActivation(directory);
        Assert.True(await request);
    }

    [Fact]
    public async Task Other_directories_and_stopped_sessions_do_not_activate()
    {
        var directory = DirectoryName();
        var activations = 0;
        using (var server = new SingleInstanceActivation(directory))
        {
            server.SetHandler(() => Interlocked.Increment(ref activations));
            Assert.False(await SingleInstanceActivation.RequestAsync(DirectoryName(), TimeSpan.FromMilliseconds(100)));
            Assert.Equal(0, activations);
        }
        Assert.False(await SingleInstanceActivation.RequestAsync(directory, TimeSpan.FromMilliseconds(100)));
        using var restarted = new SingleInstanceActivation(directory);
        Assert.True(await SingleInstanceActivation.RequestAsync(directory));
    }
}
