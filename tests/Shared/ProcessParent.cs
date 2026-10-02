using System.Diagnostics;
using System.Reflection;

namespace Winnow.TestFixtures;

internal static class ProcessParent
{
    internal static Process Start(params string[] arguments)
    {
        var assembly = typeof(ProcessParent).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(value => value.Key == "ProcessParentFixture").Value!;
        if (!Path.IsPathFullyQualified(assembly) || !File.Exists(assembly))
            throw new FileNotFoundException("Build the process parent fixture before running these contracts.", assembly);
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        start.ArgumentList.Add(assembly);
        start.ArgumentList.Add("--process-parent-fixture");
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new IOException("Could not start the process parent fixture.");
    }

    internal static async Task<string> ReadLineAsync(Process parent, TimeSpan timeout)
    {
        var error = parent.StandardError.ReadToEndAsync();
        try
        {
            return await parent.StandardOutput.ReadLineAsync().WaitAsync(timeout)
                ?? throw new IOException("The parent fixture closed its output before readiness.");
        }
        catch (Exception failure)
        {
            if (!parent.HasExited) parent.Kill(entireProcessTree: true);
            await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            throw new IOException($"Parent fixture {parent.Id} exited {parent.ExitCode} before readiness. {await error.WaitAsync(TimeSpan.FromSeconds(5))}", failure);
        }
    }
}
