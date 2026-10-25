using System.Text.RegularExpressions;

namespace Winnow.App.Services;

/// <summary>A browser handoff selects an official package, never a download URL or filesystem path.</summary>
public sealed partial record PluginInstallRequest(string PluginId, string ReleaseTag)
{
    public static bool TryParseUri(string? value, out PluginInstallRequest? request)
    {
        request = null;
        const string prefix = "winnow://plugins/install?";
        if (value is not { Length: <= 256 } || !value.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var fields = value[prefix.Length..].Split('&');
        if (fields.Length != 2) return false;
        string? id = null, release = null;
        foreach (var field in fields)
        {
            if (field.StartsWith("id=", StringComparison.Ordinal) && id is null) id = field[3..];
            else if (field.StartsWith("release=", StringComparison.Ordinal) && release is null) release = field[8..];
            else return false;
        }
        if (!IsOfficialId(id) || !IsReleaseTag(release)) return false;
        request = new(id!, release!);
        return true;
    }

    internal static bool IsOfficialId(string? id) => id is "psn" or "xbox" or "steamgriddb";
    internal static bool IsReleaseTag(string? tag) => tag is { Length: <= 80 } && ReleaseTagPattern().IsMatch(tag);
    internal bool IsValid => IsOfficialId(PluginId) && IsReleaseTag(ReleaseTag);

    [GeneratedRegex(@"\Av(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?\z", RegexOptions.CultureInvariant)]
    private static partial Regex ReleaseTagPattern();
}

public enum PluginInstallOutcome { Installed, AlreadyInstalled, Failed }
public sealed record PluginInstallProgress(string Message);
public sealed record PluginInstallResult(PluginInstallOutcome Outcome, string PluginId, string Message);

public interface IOfficialPluginInstaller
{
    Task<PluginInstallResult> InstallAsync(PluginInstallRequest request,
        IProgress<PluginInstallProgress>? progress = null, CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
}
