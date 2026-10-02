using System.Diagnostics;
using Winnow.Api.Client;

namespace Winnow.App.Api;

internal static class BackendProcessLauncher
{
    public static async Task<Services.DataLocation> ResolveDataLocationAsync(string[] arguments, CancellationToken ct)
    {
        var start = CreateStartInfo();
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.ArgumentList.Add("--resolve-data-location");
        for (var index = 0; index < arguments.Length; index++)
        {
            if (arguments[index].StartsWith("--data-dir=", StringComparison.Ordinal))
                start.ArgumentList.Add(arguments[index]);
            else if (arguments[index] == "--data-dir")
            {
                start.ArgumentList.Add(arguments[index]);
                if (index + 1 < arguments.Length) start.ArgumentList.Add(arguments[++index]);
            }
        }
        using var process = Process.Start(start) ?? throw new IOException("The Winnow backend could not start.");
        var output = process.StandardOutput.ReadToEndAsync(ct);
        var error = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        if (process.ExitCode == 2) throw new Services.DataDirectoryOverrideException(await error);
        if (process.ExitCode != 0) throw new IOException(await error);
        return System.Text.Json.JsonSerializer.Deserialize<Services.DataLocation>(await output,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("The backend did not return a data location.");
    }

    public static async Task<WinnowApiClient> AttachOrStartAsync(string dataDirectory,
        IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var api = WinnowApiClient.Attach(dataDirectory);
        if (await IsReadyAsync(api, ct)) return api;

        var start = CreateStartInfo();
        start.RedirectStandardError = true;
        start.RedirectStandardOutput = true;
        start.ArgumentList.Add("--data-dir");
        start.ArgumentList.Add(dataDirectory);
        foreach (var option in new[] { "--no-sync", "--seed-sample" })
            if (arguments.Contains(option)) start.ArgumentList.Add(option);
        var process = Process.Start(start) ?? throw new IOException("The Winnow backend could not start.");
        var output = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
        var failure = CaptureFailureAsync(process);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            // Another frontend may win startup. Read discovery each time and attach to that owner.
            while (!await IsReadyAsync(api, deadline.Token).ConfigureAwait(false))
            {
                if (process.HasExited)
                {
                    var detail = await failure.ConfigureAwait(false);
                    // A competing launch can own the data directory before it publishes discovery.
                    if (!detail.Contains("already owns this data directory", StringComparison.Ordinal))
                        throw new IOException(string.IsNullOrWhiteSpace(detail)
                            ? "The Winnow backend exited before becoming ready." : detail.Trim());
                }
                await Task.Delay(150, deadline.Token).ConfigureAwait(false);
            }
            return api;
        }
        catch (Exception exception)
        {
            api.Dispose();
            if (exception is IOException) throw;
            throw new IOException("The Winnow backend did not become ready. Check the backend log in the data directory.", exception);
        }
        finally
        {
            _ = Task.WhenAll(output, failure).ContinueWith(_ => process.Dispose(), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    private static async Task<string> CaptureFailureAsync(Process process)
    {
        // Drain without awaiting the long-lived child during successful startup. Keep a bounded
        // tail and release the pipe/process handles when this independently running child exits.
        var tail = new System.Text.StringBuilder();
        var buffer = new char[1024];
        int count;
        while ((count = await process.StandardError.ReadAsync(buffer).ConfigureAwait(false)) != 0)
        {
            tail.Append(buffer, 0, count);
            if (tail.Length > 8192) tail.Remove(0, tail.Length - 8192);
        }
        await process.WaitForExitAsync().ConfigureAwait(false);
        return tail.ToString();
    }

    private static ProcessStartInfo CreateStartInfo()
    {
        var directory = AppContext.BaseDirectory;
        var executableName = OperatingSystem.IsWindows() ? "Winnow.Backend.exe" : "Winnow.Backend";
        var executable = new[]
        {
            Path.Combine(directory, "backend", executableName),
            Path.Combine(directory, executableName)
        }.FirstOrDefault(File.Exists);
        var library = new[]
        {
            Path.Combine(directory, "backend", "Winnow.Backend.dll"),
            Path.Combine(directory, "Winnow.Backend.dll")
        }.FirstOrDefault(File.Exists);
        if (executable is null && library is null)
        {
            throw new FileNotFoundException("The Winnow backend is missing. Rebuild or reinstall Winnow.");
        }
        var start = new ProcessStartInfo(executable ?? "dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(executable ?? library!)!
        };
        if (executable is null) start.ArgumentList.Add(library!);
        return start;
    }

    private static async Task<bool> IsReadyAsync(WinnowApiClient api, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(1));
        try { return (await api.GetHealthAsync(timeout.Token)).ApiVersion == "1"; }
        catch (Exception ex) when (ex is IOException or HttpRequestException or
            System.Text.Json.JsonException or OperationCanceledException)
        {
            ct.ThrowIfCancellationRequested();
            return false;
        }
    }
}
