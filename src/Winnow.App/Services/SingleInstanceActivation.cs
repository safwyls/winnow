using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace Winnow.App.Services;

/// <summary>A per-library, current-user channel that queues launches until the UI is ready.</summary>
internal sealed class SingleInstanceActivation : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private readonly Task _listener;
    private Action<AppActivationRequest>? _activate;
    private readonly Queue<AppActivationRequest> _pending = new();

    public SingleInstanceActivation(string directory)
    {
        var name = SingleInstanceGuard.ActivationNameFor(directory);
        var pipe = CreatePipe(name);
        _listener = Task.Run(() => ListenAsync(name, pipe));
    }

    public void SetHandler(Action activate) => SetHandler(_ => activate());

    public void SetHandler(Action<AppActivationRequest> activate)
    {
        lock (_gate)
        {
            _activate = activate;
            while (_pending.TryDequeue(out var request)) activate(request);
        }
    }

    public bool Enqueue(AppActivationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            if (_activate is { } activate) activate(request);
            else
            {
                if (_pending.Count > 0 && _pending.Last() == request) return true;
                if (_pending.Count >= 64) return false;
                _pending.Enqueue(request);
            }
            return true;
        }
    }

    private static NamedPipeServerStream CreatePipe(string name) => OperatingSystem.IsWindows()
        ? WindowsActivationSecurity.CreatePipe(name)
        : new(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

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
                    AppActivationRequest? action = request[0] switch
                    {
                        1 => AppActivationRequest.Activate,
                        2 => AppActivationRequest.Fullscreen,
                        _ => null
                    };
                    if (request[0] == 3)
                    {
                        var payload = new byte[8];
                        await pipe.ReadExactlyAsync(payload, exchange.Token);
                        var id = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(payload);
                        if (id > 0) action = AppActivationRequest.ForGame(id);
                    }
                    await pipe.WriteAsync(new byte[] { action is not null && Enqueue(action) ? (byte)1 : (byte)0 }, exchange.Token);
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException) { }
            }
            if (_stop.IsCancellationRequested) break;
            pipe = CreatePipe(name);
        }
    }

    public static Task<bool> RequestAsync(string directory, TimeSpan? timeout = null)
        => RequestAsync(directory, AppActivationRequest.Activate, timeout);

    public static async Task<bool> RequestAsync(string directory, AppActivationRequest request, TimeSpan? timeout = null)
    {
        using var deadline = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(3));
        try
        {
            using var pipe = new NamedPipeClientStream(".", SingleInstanceGuard.ActivationNameFor(directory),
                PipeDirection.InOut, PipeOptions.Asynchronous |
                    (OperatingSystem.IsWindows() ? PipeOptions.None : PipeOptions.CurrentUserOnly));
            await pipe.ConnectAsync(deadline.Token);
            if (OperatingSystem.IsWindows()) WindowsActivationSecurity.VerifyServer(pipe);
            var process = new byte[4];
            await pipe.ReadExactlyAsync(process, deadline.Token);
            // A newly launched process can pass its foreground permission to the existing UI.
            if (OperatingSystem.IsWindows()) AllowSetForegroundWindow(BitConverter.ToInt32(process));
            var payload = new byte[request.Kind == AppActivationKind.LaunchGame ? 9 : 1];
            payload[0] = (byte)request.Kind;
            if (payload.Length == 9)
                System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(1), request.OwnershipId);
            await pipe.WriteAsync(payload, deadline.Token);
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
