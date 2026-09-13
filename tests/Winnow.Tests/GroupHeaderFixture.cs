using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Data.Repositories;
using Winnow.Resolve;

namespace Winnow.Tests;

public sealed class GroupHeaderFixture : IDisposable
{
    public TempDatabase Db { get; } = new();
    public WorkRepository Works { get; }
    public ReleaseRepository Releases { get; }
    public OwnershipRepository Ownership { get; }
    public IdentityLinkRepository Links { get; }
    public GroupHeaderPreferenceRepository Preferences { get; }
    public (long Work, long Release) Steam { get; private set; }
    public (long Work, long Release) Gog { get; private set; }

    private GroupHeaderFixture()
    {
        Works = new(Db.Factory); Releases = new(Db.Factory); Ownership = new(Db.Factory);
        Links = new(Db.Factory); Preferences = new(Db.Factory);
    }

    public static async Task<GroupHeaderFixture> CreateAsync()
    {
        var fixture = new GroupHeaderFixture();
        fixture.Steam = await fixture.AddAsync("Steam title", "steam");
        fixture.Gog = await fixture.AddAsync("GOG title", "gog");
        await fixture.LinkAsync(fixture.Steam.Work, fixture.Gog.Work);
        return fixture;
    }

    public async Task<(long Work, long Release)> AddAsync(string title, string store)
    {
        var work = await Works.InsertAsync(new Work { Name = title, FirstReleaseYear = 2011 });
        var release = await Releases.InsertAsync(new Release { WorkId = work, Name = title });
        await Ownership.UpsertAsync(new OwnershipUpsert(release, store, null, null, null, null));
        return (work, release);
    }

    public Task<long> LinkAsync(long parent, long child) => Links.LinkAsync(new IdentityLinkRequest
        { ParentWorkId = parent, ChildWorkIds = [child], Kind = IdentityLinkKinds.SameGame });

    public LibraryViewModel Library() => new(new LibraryQueryRepository(Db.Factory), Ownership, Releases, Works,
        new UpdateEventRepository(Db.Factory), identityLinks: Links, groupHeaders: Preferences);

    public MergeQueueViewModel Queue(LibraryViewModel? library = null) => new(new MergeCandidateRepository(Db.Factory),
        Releases, Works, Links, Ownership,
        new LibraryExpansionScan(Releases, Links, new ExpansionRefusalRepository(Db.Factory)),
        new ExpansionRefusalRepository(Db.Factory), new LibraryQueryRepository(Db.Factory),
        settings: new SettingsRepository(Db.Factory), groupHeaders: Preferences, library: library);

    public void Dispose() => Db.Dispose();
}
