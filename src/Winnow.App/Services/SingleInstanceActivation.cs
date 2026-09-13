using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace Winnow.App.Services;

/// <summary>A per-library, current-user channel that queues launches until the UI is ready.</summary>
internal sealed class SingleInstanceActivation : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private readonly Task _listener;
    private Action? _activate;
    private bool _pending;

    public SingleInstanceActivation(string directory)
    {
        var name = SingleInstanceGuard.ActivationNameFor(directory);
        var pipe = CreatePipe(name);
        _listener = Task.Run(() => ListenAsync(name, pipe));
    }

    public void SetHandler(Action activate)
    {
        lock (_gate)
        {
            _activate = activate;
            if (_pending) { _pending = false; activate(); }
        }
    }

    private static NamedPipeServerStream CreatePipe(string name) => new(name, PipeDirection.InOut,
        1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private async Task ListenAsync(string name, NamedPipeServerStream pipe)
    {
        while (true)
        {
            using (pipe)
            {
                try
                {
                    await pipe.WaitForConnectionAsync(_stop.Token);
                    using var exchange = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                    exchange.CancelAfter(TimeSpan.FromSeconds(2));
                    await pipe.WriteAsync(BitConverter.GetBytes(Environment.ProcessId), exchange.Token);
                    var request = new byte[1];
                    await pipe.ReadExactlyAsync(request, exchange.Token);
                    if (request[0] == 1)
                    {
                        lock (_gate)
                        {
                            if (_activate is { } activate) activate();
                            else _pending = true;
                        }
                        await pipe.WriteAsync(new byte[] { 1 }, exchange.Token);
                    }
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException) { }
            }
            if (_stop.IsCancellationRequested) break;
            pipe = CreatePipe(name);
        }
    }

    public static async Task<bool> RequestAsync(string directory, TimeSpan? timeout = null)
    {
        using var deadline = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(3));
        try
        {
            using var pipe = new NamedPipeClientStream(".", SingleInstanceGuard.ActivationNameFor(directory),
                PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(deadline.Token);
            var process = new byte[4];
            await pipe.ReadExactlyAsync(process, deadline.Token);
            // A newly launched process can pass its foreground permission to the existing UI.
            if (OperatingSystem.IsWindows()) AllowSetForegroundWindow(BitConverter.ToInt32(process));
            await pipe.WriteAsync(new byte[] { 1 }, deadline.Token);
            var reply = new byte[1];
            await pipe.ReadExactlyAsync(reply, deadline.Token);
            return reply[0] == 1;
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.GetAwaiter().GetResult();
        _stop.Dispose();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);
}
