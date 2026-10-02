using System.Diagnostics;
using System.Globalization;

namespace Winnow.Update.Helper;

public static class UpdateHelperCommands
{
    public static async Task<int> RunAsync(string[] args, TextReader input, TextWriter output, TextWriter error)
    {
        try
        {
            if (args.Length == 0) throw new ArgumentException("Commands: stage, prepare, discard, handoff, installed-handoff, frontend, apply, recover, resume.");
            string? Value(string name)
            {
                var indices = args.Select((value, index) => (value, index)).Where(x => x.value == name).ToArray();
                if (indices.Length > 1) throw new ArgumentException("Duplicate " + name);
                if (indices.Length == 0) return null;
                var index = indices[0].index;
                return index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)
                    ? args[index + 1] : throw new ArgumentException("Missing value for " + name);
            }
            string Required(string name) => Value(name) ?? throw new ArgumentException("Missing " + name);
            int Pid() => int.Parse(Required("--pid"), NumberStyles.None, CultureInfo.InvariantCulture);
            switch (args[0])
            {
                case "stage":
                    await output.WriteLineAsync(PortableUpdateEngine.Stage(Required("--archive"), Required("--sha256"), Required("--version"), Required("--runtime"), Required("--installation"), Required("--data-dir"), Required("--executable"), args.Where(a => a is "--fullscreen" or "--no-sync").ToArray(), transactionId: Value("--transaction")));
                    break;
                case "prepare":
                    await output.WriteLineAsync(PortableUpdateEngine.PrepareHelper(Required("--journal"), Required("--transaction")));
                    break;
                case "discard":
                    PortableUpdateEngine.DiscardStaged(Required("--journal"), Required("--transaction"));
                    break;
                case "handoff":
                    using (var parent = FrontendInstallationHost.Parent(Pid()))
                        await HandoffAsync(Required("--journal"), Required("--transaction"), parent);
                    break;
                case "installed-handoff":
                    using (var parent = FrontendInstallationHost.Parent(Pid()))
                        await InstalledUpdateHandoff.PrepareAsync(Required("--installer"), Required("--sha256"), Required("--installation"), Required("--executable"), Required("--data-dir"), parent, args);
                    break;
                case "frontend":
                    using (var parent = FrontendInstallationHost.Parent(Pid()))
                        await FrontendInstallationHost.RunAsync(Required("--installation"), Required("--data-dir"), parent, input, output);
                    break;
                case "apply":
                    await PortableUpdateEngine.ApplyAsync(Required("--journal"), Value("--pid") is { } pid ? int.Parse(pid, CultureInfo.InvariantCulture) : null, Value("--start-ticks") is { } ticks ? long.Parse(ticks, CultureInfo.InvariantCulture) : null, expectedTransactionId: Value("--transaction"));
                    break;
                case "recover":
                    PortableUpdateEngine.Recover(Required("--journal"), args.Contains("--restore-backup"));
                    break;
                case "resume":
                    PortableUpdateEngine.Resume(Required("--journal"));
                    break;
                default: throw new ArgumentException("Unknown command.");
            }
            await output.FlushAsync();
            return 0;
        }
        catch (Exception failure)
        {
            await error.WriteLineAsync(failure.Message);
            await error.FlushAsync();
            return 1;
        }
    }

    private static async Task HandoffAsync(string journal, string transaction, Process parent)
    {
        var helper = PortableUpdateEngine.PrepareHelper(journal, transaction);
        var root = Path.GetDirectoryName(Path.GetFullPath(journal))!;
        try
        {
            var start = HandoffProcess.StartInfo(helper);
            foreach (var argument in new[] { "apply", "--journal", Path.GetFullPath(journal), "--transaction", transaction, "--pid", parent.Id.ToString(CultureInfo.InvariantCulture), "--start-ticks", parent.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture) })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new IOException("Could not start the portable update helper.");
            HandoffProcess.Drain(process);
            var timer = Stopwatch.StartNew();
            var marker = Path.Combine(root, "helper-ready");
            while (!File.Exists(marker) || await File.ReadAllTextAsync(marker) != transaction)
            {
                if (parent.HasExited || process.HasExited || timer.Elapsed >= TimeSpan.FromSeconds(20))
                    throw new IOException("The portable update handoff did not become ready.");
                await Task.Delay(50);
            }
            await File.WriteAllTextAsync(Path.Combine(root, "proceed"), transaction);
        }
        catch
        {
            await File.WriteAllTextAsync(Path.Combine(root, "cancel"), transaction);
            throw;
        }
    }
}
