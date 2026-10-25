using Winnow.Core.Domain;
using Winnow.Core.Repositories;

namespace Winnow.App.Services;

/// <summary>Selections stay on the edited work; only reads and explicit reset traverse confirmed links.</summary>
public sealed class ArtworkSelectionService(IArtworkChoiceRepository choices, IIdentityLinkRepository links)
{
    public async Task<ArtworkChoice?> GetAsync(long workId, ArtworkSlot slot, CancellationToken ct = default)
        => await choices.GetEffectiveAsync(await GroupAsync(workId, ct), slot, ct);

    public Task<long> SaveAsync(ArtworkChoice choice, CancellationToken ct = default) => choices.SetAsync(choice, ct);

    public async Task ResetAsync(long workId, ArtworkSlot slot, CancellationToken ct = default)
        => await choices.ResetAsync(await GroupAsync(workId, ct), slot, ct);

    public async Task<IReadOnlyList<long>> GroupAsync(long workId, CancellationToken ct = default)
        => (await links.GetResolutionAsync(ct)).SameGame.GroupOf(workId);
}
