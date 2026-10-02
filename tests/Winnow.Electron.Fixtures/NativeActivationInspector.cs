using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Winnow.App.Services;

namespace Winnow.Electron.Fixtures;

[SupportedOSPlatform("windows")]
internal static class NativeActivationInspector
{
    public static async Task InspectAsync(string directory)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var mutexName = SingleInstanceGuard.NameFor(directory, "Electron");
        var pipeName = SingleInstanceGuard.ActivationNameFor(directory, "Electron");
        using var mutex = Mutex.OpenExisting(mutexName);
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await pipe.ConnectAsync(deadline.Token);
        var process = new byte[4];
        await pipe.ReadExactlyAsync(process, deadline.Token);
        var state = new
        {
            currentSid = identity.User!.Value, mutexName, pipeName,
            activationProcessId = BitConverter.ToInt32(process),
            mutex = Describe(mutex.GetAccessControl()), pipe = Describe(pipe.GetAccessControl())
        };
        // Finish the exchange with an invalid opcode; inspecting must not activate the window.
        await pipe.WriteAsync(new byte[] { 0 }, deadline.Token);
        await pipe.ReadExactlyAsync(new byte[1], deadline.Token);
        Console.WriteLine(JsonSerializer.Serialize(state));
    }

    private static object Describe(ObjectSecurity security) => new
    {
        ownerSid = security.GetOwner(typeof(SecurityIdentifier))!.Value,
        isProtected = security.AreAccessRulesProtected,
        rules = ((CommonObjectSecurity)security).GetAccessRules(true, true, typeof(SecurityIdentifier))
            .Cast<AccessRule>().Select(rule => new
            {
                type = rule.AccessControlType.ToString(), sid = rule.IdentityReference.Value,
                inherited = rule.IsInherited,
                rights = rule is MutexAccessRule mutex ? (int)mutex.MutexRights : (int)((PipeAccessRule)rule).PipeAccessRights
            }).ToArray()
    };
}
