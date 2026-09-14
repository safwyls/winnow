using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Winnow.App.Services;

internal sealed record JumpListGame(long OwnershipId, string Title, string? IconPath = null);

/// <summary>Windows owns the menu; Winnow supplies launch destinations and a fullscreen task.</summary>
internal static class WindowsJumpList
{
    private static readonly object Gate = new();
    private static readonly Guid ObjectArrayId = new("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9");
    private static readonly Guid ShellLinkId = new("000214F9-0000-0000-C000-000000000046");

    internal static string GetAppId(string dataDirectory)
    {
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDirectory));
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (path.Equals(Path.Combine(local, "Winnow"), StringComparison.OrdinalIgnoreCase) ||
            path.Equals(Path.Combine(local, "Hoard"), StringComparison.OrdinalIgnoreCase))
            return "Winnow";
        return "Winnow.Isolated." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())))[..24];
    }

    internal static void SetAppId(string dataDirectory)
    {
        if (!OperatingSystem.IsWindows()) return;
        try { Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(GetAppId(dataDirectory))); }
        catch (Exception ex) { Trace.TraceWarning("Could not set Winnow taskbar identity: {0}", ex.Message); }
    }

    internal static bool Publish(string dataDirectory, IReadOnlyList<JumpListGame> games)
    {
        if (!OperatingSystem.IsWindows()) return false;
        return RunSta(() =>
        {
            if (OperatingSystem.IsWindows()) PublishCore(dataDirectory, games);
        });
    }

    // Kept separate for isolated shell smoke tests; normal shutdown retains the list.
    internal static void Delete(string dataDirectory)
    {
        if (!OperatingSystem.IsWindows()) return;
        RunSta(() =>
        {
            if (!OperatingSystem.IsWindows()) return;
            var list = Create<ICustomDestinationList>("77F10CF0-3DB5-4966-B520-B7C54FD35ED6");
            try { list.DeleteList(GetAppId(dataDirectory)); }
            finally { Release(list); }
        });
    }

    [SupportedOSPlatform("windows")]
    private static bool RunSta(Action action)
    {
        // Shell COM objects stay in one apartment and are released before it exits.
        lock (Gate)
        {
            var succeeded = false;
            var thread = new Thread(() =>
            {
                try { action(); succeeded = true; }
                catch (Exception ex) { Trace.TraceWarning("Could not update Winnow Jump List: {0}", ex.Message); }
            }) { IsBackground = true, Name = "Winnow Jump List" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            return succeeded;
        }
    }

    [SupportedOSPlatform("windows")]
    private static void PublishCore(string dataDirectory, IReadOnlyList<JumpListGame> games)
    {
        var list = Create<ICustomDestinationList>("77F10CF0-3DB5-4966-B520-B7C54FD35ED6");
        IObjectArray? removed = null;
        var begun = false;
        try
        {
            list.SetAppID(GetAppId(dataDirectory));
            var iid = ObjectArrayId;
            list.BeginList(out var slots, ref iid, out removed);
            begun = true;
            var removedPath = Path.Combine(dataDirectory, "jump-list-removed.json");
            var excluded = File.Exists(removedPath)
                ? JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(removedPath)) ?? []
                : new HashSet<string>(StringComparer.Ordinal);
            removed.GetCount(out var removedCount);
            var changed = false;
            for (uint i = 0; i < removedCount; i++)
            {
                object? item = null;
                try
                {
                    iid = ShellLinkId;
                    removed.GetAt(i, ref iid, out item);
                    var arguments = new StringBuilder(32768);
                    ((IShellLink)item).GetArguments(arguments, arguments.Capacity);
                    changed |= excluded.Add(arguments.ToString());
                }
                catch (COMException ex) when (ex.HResult == unchecked((int)0x80004002))
                {
                    // A removed shell item is not one of Winnow's argument-bearing links.
                }
                finally { Release(item); }
            }
            // Windows clears its removed list on commit. Retain these choices across refreshes.
            if (changed)
            {
                var temporary = removedPath + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(excluded));
                File.Move(temporary, removedPath, true);
            }
            var recent = Create<IObjectCollection>("2D3468C1-36A7-43B6-AC24-D3F02FD9607A");
            try
            {
                uint count = 0;
                foreach (var game in games.DistinctBy(g => g.OwnershipId))
                {
                    if (count >= Math.Min(slots, 10u)) break;
                    var arguments = BuildArguments(dataDirectory, "--jump-list-game " + game.OwnershipId.ToString(CultureInfo.InvariantCulture));
                    if (excluded.Contains(arguments)) continue;
                    AddLink(recent, game.Title, arguments, game.IconPath);
                    count++;
                }
                if (count > 0)
                {
                    // Privacy settings can prohibit destinations while still permitting Tasks.
                    var result = list.AppendCategory("Recently Played", recent);
                    if (result < 0) Trace.TraceWarning("Jump List recent games unavailable: 0x{0:X8}", result);
                }
            }
            finally { Release(recent); }
            var tasks = Create<IObjectCollection>("2D3468C1-36A7-43B6-AC24-D3F02FD9607A");
            try
            {
                AddLink(tasks, "Switch to Fullscreen Mode", BuildArguments(dataDirectory, "--jump-list-fullscreen"), null);
                list.AddUserTasks(tasks);
            }
            finally { Release(tasks); }
            list.CommitList();
            begun = false;
        }
        finally
        {
            if (begun) { try { list.AbortList(); } catch (COMException) { } }
            Release(removed);
            Release(list);
        }
    }

    internal static string BuildArguments(string dataDirectory, string action)
    {
        var assembly = typeof(WindowsJumpList).Assembly.Location;
        var prefix = string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase)
            ? QuoteArgument(assembly) + " " : "";
        return prefix + action + " --data-dir " + QuoteArgument(Path.GetFullPath(dataDirectory));
    }

    internal static string QuoteArgument(string value)
    {
        var result = new StringBuilder("\"");
        var slashes = 0;
        foreach (var character in value)
        {
            if (character == '\\') { slashes++; continue; }
            result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
            result.Append(character);
            slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }

    [SupportedOSPlatform("windows")]
    private static void AddLink(IObjectCollection collection, string title, string arguments, string? iconPath)
    {
        var link = Create<IShellLink>("00021401-0000-0000-C000-000000000046");
        try
        {
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("No process executable is available.");
            link.SetPath(executable);
            link.SetArguments(arguments);
            link.SetWorkingDirectory(AppContext.BaseDirectory);
            link.SetIconLocation(iconPath ?? executable, 0);
            var store = (IPropertyStore)link;
            var key = new PropertyKey { FormatId = new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), PropertyId = 2 };
            var value = new PropVariant { Type = 31, Pointer = Marshal.StringToCoTaskMemUni(title) };
            try { store.SetValue(ref key, ref value); store.Commit(); }
            finally { Marshal.FreeCoTaskMem(value.Pointer); }
            collection.AddObject(link);
        }
        finally { Release(link); }
    }

    [SupportedOSPlatform("windows")]
    private static T Create<T>(string clsid) => (T)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid(clsid), true)!)!;

    [SupportedOSPlatform("windows")]
    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey { public Guid FormatId; public uint PropertyId; }

    // PROPVARIANT's union is two native pointers wide (24 bytes total on x64).
    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant { public ushort Type; public ushort Reserved1, Reserved2, Reserved3; public IntPtr Pointer; public IntPtr Reserved4; }

    [ComImport, Guid("6332DEBF-87B5-4670-90C0-5E57B408A49E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICustomDestinationList
    {
        void SetAppID([MarshalAs(UnmanagedType.LPWStr)] string appId);
        void BeginList(out uint slots, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IObjectArray removed);
        [PreserveSig] int AppendCategory([MarshalAs(UnmanagedType.LPWStr)] string category, IObjectArray objects);
        void AppendKnownCategory(int category);
        void AddUserTasks(IObjectArray tasks);
        void CommitList();
        void GetRemovedDestinations(ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object removed);
        void DeleteList([MarshalAs(UnmanagedType.LPWStr)] string appId);
        void AbortList();
    }

    [ComImport, Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectArray
    {
        void GetCount(out uint count);
        void GetAt(uint index, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object value);
    }

    [ComImport, Guid("5632B1A4-E38A-400A-928A-D4CD63230295"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectCollection : IObjectArray
    {
        new void GetCount(out uint count);
        new void GetAt(uint index, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object value);
        void AddObject([MarshalAs(UnmanagedType.IUnknown)] object value);
        void AddFromArray(IObjectArray array);
        void RemoveObjectAt(uint index);
        void Clear();
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLink
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int capacity, IntPtr findData, uint flags);
        void GetIDList(out IntPtr idList);
        void SetIDList(IntPtr idList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int capacity);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int capacity);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int capacity);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int command);
        void SetShowCmd(int command);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int capacity, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
