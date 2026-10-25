using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Winnow.App.Services;

/// <summary>Same-account activation across UAC elevation levels; never a general command channel.</summary>
[SupportedOSPlatform("windows")]
internal static class WindowsActivationSecurity
{
    internal static string Descriptor
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            var sid = identity.User!.Value;
            // Set the owner to the user SID, not the elevated token's Administrators owner.
            // Medium integrity permits Explorer to signal an elevated instance; the DACL
            // still excludes other accounts and network logons.
            return $"O:{sid}D:P(D;;GA;;;NU)(A;;GA;;;{sid})";
        }
    }

    internal static NamedPipeServerStream CreatePipe(string name)
    {
        var security = new PipeSecurity();
        security.SetSecurityDescriptorSddlForm(Descriptor);
        var pipe = NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security,
            additionalAccessRights: PipeAccessRights.TakeOwnership);
        try { SetMediumIntegrity(pipe.SafePipeHandle.DangerousGetHandle()); return pipe; }
        catch { pipe.Dispose(); throw; }
    }

    internal static Mutex CreateMutex(string name, out bool createdNew)
    {
        var security = new MutexSecurity();
        security.SetSecurityDescriptorSddlForm(Descriptor);
        var mutex = MutexAcl.Create(true, name, out createdNew, security);
        try
        {
            if (createdNew) SetMediumIntegrity(mutex.SafeWaitHandle.DangerousGetHandle());
            return mutex;
        }
        catch { mutex.Dispose(); throw; }
    }

    internal static void VerifyServer(NamedPipeClientStream pipe)
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!identity.User!.Equals(pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier))))
            throw new UnauthorizedAccessException("The activation server belongs to another Windows account.");
    }

    private static void SetMediumIntegrity(IntPtr handle)
    {
        // LABEL_SECURITY_INFORMATION sets only the mandatory label, without requiring
        // the audit-SACL privilege that attaching a SACL at object creation would need.
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor("S:(ML;;NW;;;ME)", 1, out var descriptor, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            if (!GetSecurityDescriptorSacl(descriptor, out _, out var acl, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var result = SetSecurityInfo(handle, 6, 0x10, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, acl);
            if (result != 0) throw new Win32Exception((int)result);
        }
        finally { LocalFree(descriptor); }
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string text, uint revision, out IntPtr descriptor, out uint size);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSecurityDescriptorSacl(IntPtr descriptor, [MarshalAs(UnmanagedType.Bool)] out bool present, out IntPtr acl, [MarshalAs(UnmanagedType.Bool)] out bool defaulted);
    [DllImport("advapi32.dll")]
    private static extern uint SetSecurityInfo(IntPtr handle, int objectType, uint information, IntPtr owner, IntPtr group, IntPtr dacl, IntPtr sacl);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
