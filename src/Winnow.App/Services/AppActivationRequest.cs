using System.Globalization;

namespace Winnow.App.Services;

internal enum AppActivationKind : byte { Activate = 1, Fullscreen = 2, LaunchGame = 3 }

/// <summary>Shell actions identify local ownerships; launch targets come from the library.</summary>
internal sealed record AppActivationRequest
{
    private AppActivationRequest(AppActivationKind kind, long ownershipId = 0)
        => (Kind, OwnershipId) = (kind, ownershipId);

    public AppActivationKind Kind { get; }
    public long OwnershipId { get; }
    public static AppActivationRequest Activate { get; } = new(AppActivationKind.Activate);
    public static AppActivationRequest Fullscreen { get; } = new(AppActivationKind.Fullscreen);
    public static AppActivationRequest ForGame(long ownershipId) => ownershipId > 0
        ? new(AppActivationKind.LaunchGame, ownershipId)
        : throw new ArgumentOutOfRangeException(nameof(ownershipId));

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
