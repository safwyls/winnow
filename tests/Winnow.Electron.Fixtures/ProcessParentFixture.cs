using System.Diagnostics;
using System.Text;

namespace Winnow.Electron.Fixtures;

// A real parent/pipe lifetime for process contracts, without a shell, HTTP host or database.
internal static class ProcessParentFixture
{
    internal const string Argument = "--process-parent-fixture";

    internal static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length < 2 || args[0] != Argument) throw new ArgumentException("Invalid parent fixture mode.");
            string Value(string name)
            {
                var index = Array.IndexOf(args, name);
                if (index < 2 || index + 1 == args.Length || args.Count(value => value == name) != 1)
                    throw new ArgumentException("Missing or repeated parent fixture argument: " + name);
                return args[index + 1];
            }
            if (args[1] == "idle")
            {
                await Console.Out.WriteLineAsync(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                await Console.Out.FlushAsync();
                await Console.In.ReadLineAsync();
                return 0;
            }
            var target = Path.GetFullPath(Value("--target"));
            if (!File.Exists(target)) throw new FileNotFoundException("The process fixture target does not exist.", target);
            var start = new ProcessStartInfo(args[1] == "activation" ? "dotnet" : target)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            var parent = Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (args[1] == "activation")
            {
                foreach (var argument in new[] { target, "--frontend-activation-helper", "--parent-pid", parent, "--data-dir", Value("--data-dir") })
                    start.ArgumentList.Add(argument);
            }
            else if (args[1] == "handoff")
            {
                foreach (var argument in new[] { "handoff", "--journal", Value("--journal"), "--transaction", Value("--transaction"), "--pid", parent })
                    start.ArgumentList.Add(argument);
            }
            else throw new ArgumentException("Unknown parent fixture mode.");
            using var child = Process.Start(start) ?? throw new IOException("Could not start the parent fixture child.");
            await Console.Error.WriteLineAsync($"parent:{Environment.ProcessId} child:{child.Id} mode:{args[1]}");
            var error = DrainAsync(child.StandardError);
            try
            {
                if (args[1] == "activation")
                {
                    await child.StandardInput.WriteLineAsync("{\"version\":1,\"activation\":{\"kind\":\"show\"}}");
                    await child.StandardInput.FlushAsync();
                    var frame = await child.StandardOutput.ReadLineAsync();
                    if (frame is null) throw new IOException("Activation helper closed before readiness: " + await error);
                    await Console.Out.WriteLineAsync(frame);
                    await Console.Out.FlushAsync();
                    await Console.In.ReadLineAsync();
                    child.StandardInput.Close();
                    await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }
                else
                {
                    child.StandardInput.Close();
                    var output = DrainAsync(child.StandardOutput);
                    // The copied worker must not keep either command pipe open.
                    // Do not acknowledge merely because the handoff process exited.
                    async Task Observe(Task task, string phase)
                    {
                        await task;
                        await Console.Error.WriteLineAsync(phase);
                    }
                    await Task.WhenAll(Observe(child.WaitForExitAsync(), "command-exited"), Observe(output, "stdout-eof"), Observe(error, "stderr-eof"));
                    if (child.ExitCode != 0) throw new IOException($"Handoff exited {child.ExitCode}: {await output} {await error}");
                    await Console.Out.WriteLineAsync("handoff:0");
                    await Console.Out.FlushAsync();
                    await Console.In.ReadLineAsync();
                }
                return child.ExitCode;
            }
            finally
            {
                if (!child.HasExited)
                {
                    child.Kill(entireProcessTree: true);
                    await child.WaitForExitAsync();
                }
            }
        }
        catch (Exception error)
        {
            await Console.Error.WriteLineAsync(error.ToString());
            return 1;
        }
    }

    private static async Task<string> DrainAsync(StreamReader reader)
    {
        var text = new StringBuilder();
        var buffer = new char[1024];
        int read;
        while ((read = await reader.ReadAsync(buffer)) != 0)
            if (text.Length < 8192) text.Append(buffer, 0, Math.Min(read, 8192 - text.Length));
        return text.ToString();
    }
}
