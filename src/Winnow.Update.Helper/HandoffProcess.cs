using System.Diagnostics;

namespace Winnow.Update.Helper;

internal static class HandoffProcess
{
    internal static ProcessStartInfo StartInfo(string executable) => new(executable)
    {
        UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
        // A replacement worker outlives this command. Never inherit the frontend's protocol pipes.
        // Durable recovery diagnostics belong to the journal or installer handoff directory.
        RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
    };

    internal static void Drain(Process process)
    {
        process.StandardInput.Close();
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }
}
