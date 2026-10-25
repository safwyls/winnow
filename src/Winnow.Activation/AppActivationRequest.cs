using System.Globalization;

namespace Winnow.App.Services;

internal enum AppActivationKind : byte { Activate = 1, Fullscreen = 2, LaunchGame = 3, InstallPlugin = 4 }

/// <summary>Shell actions select local games or official plugins, never arbitrary launch or download targets.</summary>
internal sealed record AppActivationRequest
{
    private AppActivationRequest(AppActivationKind kind, long ownershipId = 0, PluginInstallRequest? plugin = null)
        => (Kind, OwnershipId, Plugin) = (kind, ownershipId, plugin);

    public AppActivationKind Kind { get; }
    public long OwnershipId { get; }
    public PluginInstallRequest? Plugin { get; }
    public static AppActivationRequest Activate { get; } = new(AppActivationKind.Activate);
    public static AppActivationRequest Fullscreen { get; } = new(AppActivationKind.Fullscreen);
    public static AppActivationRequest ForGame(long ownershipId) => ownershipId > 0
        ? new(AppActivationKind.LaunchGame, ownershipId)
        : throw new ArgumentOutOfRangeException(nameof(ownershipId));

    public static AppActivationRequest ForPlugin(PluginInstallRequest plugin)
        => new(AppActivationKind.InstallPlugin, plugin: plugin);

    // Validate shell input before configuration or data-directory selection. A URI must
    // be one argument, never a source of additional application options.
    public static bool TryReadStartup(string[] args, out AppActivationRequest request)
    {
        if (args.Any(value => value.StartsWith("--uri=", StringComparison.Ordinal))) { request = Activate; return false; }
        var uriIndex = Array.FindIndex(args, value => value == "--uri" || value.StartsWith("winnow:", StringComparison.OrdinalIgnoreCase));
        if (uriIndex < 0) { request = FromArguments(args); return true; }
        request = Activate;
        var explicitFlag = args[uriIndex] == "--uri";
        var valueIndex = uriIndex + (explicitFlag ? 1 : 0);
        if (valueIndex != args.Length - 1) return false;
        // Explicit data directories remain available for isolated development and tests.
        if (uriIndex != 0 && (uriIndex != 2 || args[0] != "--data-dir" || string.IsNullOrWhiteSpace(args[1]))) return false;
        if (!PluginInstallRequest.TryParseUri(args[valueIndex], out var plugin) || plugin is null) return false;
        request = ForPlugin(plugin);
        return true;
    }

    public static AppActivationRequest FromArguments(string[] args)
    {
        AppActivationRequest? request = null;
        for (var index = 0; index < args.Length; index++)
        {
            if (!args[index].StartsWith("--jump-list-", StringComparison.Ordinal)) continue;
            if (request is not null) return Activate;
            if (args[index] == "--jump-list-fullscreen") request = Fullscreen;
            else if (args[index] == "--jump-list-game" && index + 1 < args.Length &&
                     long.TryParse(args[++index], NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0)
                request = ForGame(id);
            else return Activate;
        }
        return request ?? Activate;
    }
}
