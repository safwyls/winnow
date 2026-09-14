using Winnow.App.Services;
using Xunit;

namespace Winnow.Tests;

public sealed class SingleInstanceActivationTests
{
    [Fact]
    public async Task Typed_requests_preserve_order_until_ready_including_cold_start()
    {
        var directory = DirectoryName();
        using var server = new SingleInstanceActivation(directory);
        Assert.True(server.Enqueue(AppActivationRequest.ForGame(23)));
        Assert.True(await SingleInstanceActivation.RequestAsync(directory, AppActivationRequest.Fullscreen));
        Assert.True(await SingleInstanceActivation.RequestAsync(directory, AppActivationRequest.ForGame(long.MaxValue)));
        var received = new List<AppActivationRequest>();
        server.SetHandler(received.Add);
        Assert.Equal(new[] { AppActivationRequest.ForGame(23), AppActivationRequest.Fullscreen,
            AppActivationRequest.ForGame(long.MaxValue) }, received);
        Assert.True(await SingleInstanceActivation.RequestAsync(directory, AppActivationRequest.Activate));
        Assert.Equal(AppActivationRequest.Activate, received[^1]);
    }

    [Theory]
    [InlineData(3, 0)]
    [InlineData(3, -1)]
    [InlineData(255, 42)]
    public async Task Invalid_payload_is_rejected_without_dispatch(byte command, long id)
    {
        var directory = DirectoryName();
        using var server = new SingleInstanceActivation(directory);
        var received = new List<AppActivationRequest>();
        server.SetHandler(received.Add);
        using var pipe = new System.IO.Pipes.NamedPipeClientStream(".",
            SingleInstanceGuard.ActivationNameFor(directory), System.IO.Pipes.PipeDirection.InOut,
            System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await pipe.ConnectAsync(timeout.Token);
        await pipe.ReadExactlyAsync(new byte[4], timeout.Token);
        var payload = new byte[command == 3 ? 9 : 1];
        payload[0] = command;
        if (payload.Length == 9)
            System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(1), id);
        await pipe.WriteAsync(payload, timeout.Token);
        var reply = new byte[1];
        await pipe.ReadExactlyAsync(reply, timeout.Token);
        Assert.Equal(0, reply[0]);
        Assert.Empty(received);
    }

    [Fact]
    public void Startup_queue_is_bounded_and_recovers_after_handler_is_ready()
    {
        using var server = new SingleInstanceActivation(DirectoryName());
        for (var id = 1; id <= 64; id++) Assert.True(server.Enqueue(AppActivationRequest.ForGame(id)));
        Assert.False(server.Enqueue(AppActivationRequest.Fullscreen));
        var received = new List<AppActivationRequest>();
        server.SetHandler(received.Add);
        Assert.Equal(64, received.Count);
        Assert.True(server.Enqueue(AppActivationRequest.Fullscreen));
        Assert.Equal(AppActivationRequest.Fullscreen, received[^1]);
    }

    [Fact]
    public void Shell_arguments_allow_only_one_typed_command()
    {
        Assert.Equal(AppActivationRequest.Fullscreen, AppActivationRequest.FromArguments(["--jump-list-fullscreen"]));
        Assert.Equal(AppActivationRequest.ForGame(42), AppActivationRequest.FromArguments(
            ["--data-dir", "C:\\Temp\\test library", "--jump-list-game", "42"]));
        foreach (var args in new[] { new[] { "--jump-list-game" }, ["--jump-list-game", "0"],
                     ["--jump-list-game", "-1"], ["--jump-list-game", "steam://run/42"],
                     ["--jump-list-game", "9223372036854775808"], ["--jump-list-unknown"],
                     ["--jump-list-fullscreen", "--jump-list-game", "42"] })
            Assert.Equal(AppActivationRequest.Activate, AppActivationRequest.FromArguments(args));
    }

    private static string DirectoryName() => Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("", 0)]
    [InlineData("--jump-list-fullscreen", 0)]
    [InlineData("--jump-list-game", 42)]
    public async Task Second_process_activates_owner_and_exits_before_database_initialization(string command, long ownershipId)
    {
        var directory = DirectoryName();
        using var guard = SingleInstanceGuard.TryAcquire(directory);
        Assert.NotNull(guard);
        using var server = new SingleInstanceActivation(directory);
        var activated = new TaskCompletionSource<AppActivationRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.SetHandler(request => activated.TrySetResult(request));
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
        if (command.Length > 0) start.ArgumentList.Add(command);
        if (ownershipId > 0) start.ArgumentList.Add(ownershipId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using var process = System.Diagnostics.Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await process.WaitForExitAsync(deadline.Token);
            Assert.True(process.ExitCode == 0, await errors);
            Assert.True(activated.Task.IsCompleted, await output);
            Assert.Equal(command == "--jump-list-game" ? AppActivationRequest.ForGame(ownershipId)
                : command == "--jump-list-fullscreen" ? AppActivationRequest.Fullscreen : AppActivationRequest.Activate,
                await activated.Task);
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
