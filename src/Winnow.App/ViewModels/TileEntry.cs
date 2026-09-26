using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Identity;

namespace Winnow.App.ViewModels;

/// <summary>
/// One store entry of one game — the ownership row a tile used to be, and
/// since TASK-70.6 one of the several a tile is made of.
///
/// <para>Minutes and last-played on this record belong to the same entry
/// and are never crossed. It implements <see cref="IPlayedEntry"/> so the
/// tile's own figures, when it needs them, come out of the same fold the
/// library and the details modal use.</para>
/// </summary>
public sealed record TileEntry : IPlayedEntry
{
    /// <summary>The ownership row this entry is.</summary>
    public required long OwnershipId { get; init; }

    /// <summary>The release the ownership is a licence for.</summary>
    public required long ReleaseId { get; init; }

    /// <summary>This entry's own work, UNRESOLVED — what enrichment targets.</summary>
    public required long WorkId { get; init; }

    /// <summary>The store as stored: <c>steam</c>, <c>epic</c>, <c>gog</c>.</summary>
    public required string Store { get; init; }

    public Winnow.Core.Lifecycle.GameLifecycle? Lifecycle { get; init; }

    /// <summary>This entry's own minutes.</summary>
    public required long PlaytimeMinutes { get; init; }

    /// <summary>This entry's own last-played, or null.</summary>
    public DateTime? LastPlayedAt { get; init; }

    /// <summary>
    /// Three-valued, for the reason the tile's own remarks give: no row is
    /// "nothing looked", which is not "not on disk".
    /// </summary>
    public bool? Installed { get; init; }

    /// <summary>Install directory when installed and known.</summary>
    public string? InstallPath { get; init; }

    /// <summary>Validated Steam appid, or null.</summary>
    public string? SteamAppId { get; init; }

    /// <summary>Validated GOG product id, or null.</summary>
    public string? GogProductId { get; init; }

    /// <summary>Epic's composite launch key, or null when all three parts are not held.</summary>
    public EpicLaunchKey? EpicLaunchKey { get; init; }

    public Winnow.Core.Repositories.StorefrontDetails? Storefront { get; init; }
    public PluginEntryActions? PluginActions { get; init; }
    public string? LibrarySourceLabel => PluginActions?.SourceLabel;

    /// <summary>True only when a source looked and found it on disk.</summary>
    public bool IsOnDisk => Installed == true;

    /// <summary>The store's display name — "Steam", "Epic", "GOG".</summary>
    public string StoreName => StoreNaming.Label(Store);

    /// <summary>The chip's face: the store name, uppercased.</summary>
    public string StoreBadge => StoreNaming.Badge(Store);

    /// <summary>
    /// The compact resting mark's face — one letter. The front of a tile at
    /// the 108px density floor cannot hold word-chips, so the resting mark
    /// is initials and the words arrive on hover and in the modal; see
    /// <see cref="StoreNaming.Initial"/>.
    /// </summary>
    public string StoreInitial => StoreNaming.Initial(Store);

    /// <summary>
    /// <c>Play</c> when this copy is on disk, <c>Install</c> when it is not,
    /// and null when this app cannot honestly name either (§10.3).
    /// </summary>
    public GameLink? PrimaryAction => PluginActions?.Play ?? StoreActions.PrimaryFor(
        Store, Installed, SteamAppId, GogProductId, EpicLaunchKey);

    /// <summary>
    /// Why Band 3 cannot get the user into this entry's copy, or
    /// <see cref="ViewModels.NoWayIn.None"/> when it can. Derived from the
    /// same store ids and install state as <see cref="PrimaryAction"/>.
    /// </summary>
    public NoWayIn NoWayIn => PluginActions?.Play is not null || PluginActions?.Store is not null ? NoWayIn.None : StoreActions.WhyNoWayIn(
        Store, Installed, SteamAppId, GogProductId, EpicLaunchKey, Storefront);

    /// <summary>Builds the entry for one ownership row.</summary>
    public static TileEntry For(
        long ownershipId,
        long releaseId,
        long workId,
        string store,
        long playtimeMinutes,
        DateTime? lastPlayedAt,
        Ownership? ownership = null,
        string? steamAppId = null,
        string? gogProductId = null,
        EpicLaunchKey? epicLaunchKey = null,
        Winnow.Core.Repositories.StorefrontDetails? storefront = null)
        => new()
        {
            OwnershipId = ownershipId,
            ReleaseId = releaseId,
            WorkId = workId,
            Store = store,
            PlaytimeMinutes = playtimeMinutes,
            LastPlayedAt = lastPlayedAt,
            Installed = ownership?.Installed,
            InstallPath = string.IsNullOrWhiteSpace(ownership?.InstallPath) ? null : ownership!.InstallPath,
            SteamAppId = GameLink.IsSteamAppId(steamAppId) ? steamAppId : null,
            GogProductId = StoreActions.IsGogProductId(gogProductId) ? gogProductId : null,
            EpicLaunchKey = epicLaunchKey,
            Storefront = storefront,
        };
}
