using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;

namespace Winnow.App.Design;

internal sealed class PreviewSteamReportedActivity : ISteamPlaytimeObservationRepository
{
    public static SteamReportedActivityViewModel Create() => new(new PreviewSteamReportedActivity(),
        new Dictionary<long, string> { [1] = "Stardew Valley" });
    public Task ObserveAsync(SteamPlaytimeObservation observation, CancellationToken ct = default) => Task.CompletedTask;
    public Task<IReadOnlyList<SteamPlaytimeObservation>> GetByOwnershipAsync(long ownershipId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SteamPlaytimeObservation>>([]);
    public Task<IReadOnlyList<SteamReportedActivity>> GetActivityAsync(IReadOnlyCollection<long> ownershipIds,
        DateTime asOfUtc, string? accountRef = null, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SteamReportedActivity>>([new()
        {
            Id = 1, OwnershipId = 1, AccountRef = "1", SteamDeltaMinutes = 31,
            WindowStartedAt = asOfUtc.AddHours(-3), WindowEndedAt = asOfUtc.AddHours(-2),
            MatchedRecordedMinutes = 0, UnexplainedMinutes = 31
        }]);
}
