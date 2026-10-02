using System.Diagnostics;
using System.Text.Json;
using Winnow.Api.Client;

namespace Winnow.Backend;

internal static class ConsoleBackend
{
    public static async Task<WinnowApiClient> AttachOrStartAsync(string directory, string[] args, CancellationToken ct)
    {
        var api = WinnowApiClient.Attach(directory);
        try
        {
            if (await Ready(api, ct)) return api;
            var executable = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "Winnow.Backend.exe" : "Winnow.Backend");
            var start = new ProcessStartInfo(File.Exists(executable) ? executable : "dotnet")
            {
                UseShellExecute = false, CreateNoWindow = true,
                WorkingDirectory = AppContext.BaseDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            if (!File.Exists(executable)) start.ArgumentList.Add(typeof(BackendApplication).Assembly.Location);
            start.ArgumentList.Add("--data-dir");
            start.ArgumentList.Add(directory);
            if (args.Contains("--no-sync")) start.ArgumentList.Add("--no-sync");
            var process = TerminalConsole.StartBackground(start);
            process.StandardInput.Close();
            // The backend has its own file log. Drain startup output without echoing diagnostics or
            // credentials to the terminal; closing this client never stops the independent backend.
            var output = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
            var failure = ReadFailureAsync(process);
            _ = Task.WhenAll(output, failure).ContinueWith(_ => process.Dispose(), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(45));
            await WaitForReadyAsync(token => Ready(api, token), failure, deadline.Token);
            return api;
        }
        catch { api.Dispose(); throw; }
    }

    internal static async Task WaitForReadyAsync(Func<CancellationToken, Task<bool>> ready,
        Task<string> failure, CancellationToken ct)
    {
        while (!await ready(ct))
        {
            if (failure.IsCompletedSuccessfully &&
                !(await failure).Contains("already owns this data directory", StringComparison.Ordinal))
                throw new IOException("The Winnow backend could not start. Check its log in the selected data directory.");
            await Task.Delay(150, ct);
        }
    }

    private static async Task<string> ReadFailureAsync(Process process)
    {
        var tail = new System.Text.StringBuilder();
        var buffer = new char[1024];
        int count;
        while ((count = await process.StandardError.ReadAsync(buffer)) > 0)
        {
            tail.Append(buffer, 0, count);
            if (tail.Length > 8192) tail.Remove(0, tail.Length - 8192);
        }
        await process.WaitForExitAsync();
        return tail.ToString();
    }

    private static async Task<bool> Ready(WinnowApiClient api, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(1));
        try { return (await api.GetHealthAsync(timeout.Token)).ApiVersion == "1"; }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException or OperationCanceledException)
        { ct.ThrowIfCancellationRequested(); return false; }
    }
}
