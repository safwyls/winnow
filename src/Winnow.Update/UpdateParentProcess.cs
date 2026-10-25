using System.Diagnostics;
using System.Globalization;

namespace Winnow.Update;

internal static class UpdateParentProcess
{
    internal readonly record struct LinuxIdentity(ulong StartClockTicks, char State)
    {
        internal bool Exited => State is 'Z' or 'X' or 'x';
    }

    internal static LinuxIdentity ParseLinuxStat(int pid, string stat)
    {
        var opening = stat.IndexOf('(');
        var closing = stat.LastIndexOf(')');
        if (opening <= 0 || closing <= opening ||
            !int.TryParse(stat.AsSpan(0, opening).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var actual) || actual != pid)
            throw new IOException("The update parent process identity is invalid.");
        var fields = stat[(closing + 1)..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        // /proc/<pid>/stat field 22 is kernel clock ticks since boot. The comm
        // field can contain spaces/parentheses; UTC StartTime varies by runtime.
        if (fields.Length < 20 || fields[0].Length != 1 || !"RSDZTtWXxKPI".Contains(fields[0][0]) ||
            !ulong.TryParse(fields[19], NumberStyles.None, CultureInfo.InvariantCulture, out var birth) || birth == 0)
            throw new IOException("The update parent process identity is invalid.");
        return new LinuxIdentity(birth, fields[0][0]);
    }

    internal static LinuxIdentity? ReadLinuxIdentity(int pid)
    {
        if (pid <= 0) throw new ArgumentOutOfRangeException(nameof(pid));
        try { return ParseLinuxStat(pid, File.ReadAllText($"/proc/{pid}/stat")); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    internal static async Task WaitAsync(int pid, long startTicks, ulong? startClockTicks, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsLinux())
        {
            await WaitLinuxAsync(pid, startClockTicks, ReadLinuxIdentity, cancellationToken);
            return;
        }
        try
        {
            using var parent = Process.GetProcessById(pid);
            if (parent.StartTime.ToUniversalTime().Ticks == startTicks)
                await parent.WaitForExitAsync(cancellationToken);
        }
        catch (ArgumentException) { }
    }

    internal static async Task WaitLinuxAsync(int pid, ulong? expectedBirth, Func<int, LinuxIdentity?> read, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var initial = read(pid);
        if (initial is null || initial.Value.Exited) return;
        // Legacy callers supply UTC only. Wait conservatively for the current
        // kernel identity instead of skipping a live process on a UTC mismatch.
        var birth = expectedBirth ?? initial.Value.StartClockTicks;
        while (initial is { } current && !current.Exited && current.StartClockTicks == birth)
        {
            await Task.Delay(50, cancellationToken);
            initial = read(pid);
        }
    }
}
