using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Enrich.GamesDb.Model;

namespace Winnow.App.Services;

/// <summary>Applies GamesDB references corroborated by independent native-store edition evidence.</summary>
public sealed class GamesDbIdentitySyncService(
    IReleaseRepository releases,
    IIdentityLinkRepository links,
    IMergeCandidateRepository candidates,
    IWorkIgdbPinRepository pins,
    EnrichmentLookupPlanner planner,
    ILogger<GamesDbIdentitySyncService> logger,
    IReleaseEditionEvidenceAcquirer? editions = null)
{
    public GamesDbIdentitySyncResult LastResult { get; private set; } = new(0, 0, 0, 0, 0, 0);

    public async Task<int> SyncAsync(CancellationToken ct = default)
    {
        var linked = 0;
        var eligible = 0;
        var observed = 0;
        var unresolvedEdition = 0;
        var conflictingEdition = 0;
        var refused = 0;
        try
        {
            var ids = await releases.GetAllExternalIdsAsync(ct);
            var identities = (await releases.GetIdentitiesAsync(ct)).ToDictionary(r => r.ReleaseId);
            var byKey = ids.ToDictionary(e => new TargetKey(e.Provider, e.ProviderId));
            var targets = ids.Where(e => e.Provider == ExternalIdProviders.Epic
                    && identities.ContainsKey(e.ReleaseId))
                .Select(e => new EnrichmentTarget
                {
                    ReleaseId = e.ReleaseId,
                    WorkId = identities[e.ReleaseId].WorkId,
                    Provider = e.Provider,
                    ProviderId = e.ProviderId,
                }).ToArray();

            // This scan deliberately includes warm entries. A Steam counterpart
            // can be imported long after the Epic work finished enrichment.
            var plan = await planner.PlanAsync(targets, ct);
            var involved = plan.IdentityMatches.SelectMany(pair => pair.Value.Releases
                    .Select(twin => byKey.GetValueOrDefault(new(twin.Platform, twin.ExternalId))?.ReleaseId)
                    .Append(byKey[pair.Key].ReleaseId))
                .Where(id => id.HasValue).Select(id => id!.Value).ToHashSet();
            var editionTargets = ids.Where(id => involved.Contains(id.ReleaseId) && identities.ContainsKey(id.ReleaseId)
                    && id.Provider is ExternalIdProviders.Steam or ExternalIdProviders.Gog or ExternalIdProviders.Epic)
                .Select(id => new EnrichmentTarget { ReleaseId = id.ReleaseId, WorkId = identities[id.ReleaseId].WorkId,
                    Provider = id.Provider, ProviderId = id.ProviderId }).ToArray();
            var acquired = editions is null ? new Dictionary<TargetKey, EditionAcquisition>()
                : await editions.AcquireAsync(editionTargets, ct);
            foreach (var (key, game) in plan.IdentityMatches)
            {
                var epic = identities[byKey[key].ReleaseId];
                foreach (var twin in game.Releases.Distinct())
                {
                    ct.ThrowIfCancellationRequested();
                    if (twin.Platform is not (GamesDbPlatforms.Steam or GamesDbPlatforms.Gog)
                        || twin.ExternalId.Length == 0
                        || !twin.ExternalId.All(char.IsAsciiDigit)
                        || twin.ExternalId.All(c => c == '0')
                        || !byKey.TryGetValue(new TargetKey(twin.Platform, twin.ExternalId), out var external)
                        || !identities.TryGetValue(external.ReleaseId, out var other)
                        || epic.WorkId == other.WorkId)
                    {
                        continue;
                    }

                    observed++;
                    var epicAnswer = acquired.GetValueOrDefault(key);
                    var otherAnswer = acquired.GetValueOrDefault(new(twin.Platform, twin.ExternalId));
                    var epicEvidence = epicAnswer?.Evidence;
                    var otherEvidence = otherAnswer?.Evidence;
                    if (epicAnswer?.Conflicting == true || otherAnswer?.Conflicting == true
                        || epicEvidence is not null && otherEvidence is not null
                        && (epicEvidence.EditionGameId != otherEvidence.EditionGameId
                            || epicEvidence.VersionParentId != otherEvidence.VersionParentId
                            || epicEvidence.VersionTitle != otherEvidence.VersionTitle))
                    {
                        conflictingEdition++;
                        continue;
                    }
                    if (epicEvidence is null || otherEvidence is null
                        || epicEvidence.ReleaseId != epic.ReleaseId || otherEvidence.ReleaseId != other.ReleaseId
                        || epicEvidence.WorkId != epic.WorkId || otherEvidence.WorkId != other.WorkId)
                    {
                        unresolvedEdition++;
                        continue;
                    }

                    eligible++;
                    var resolution = await links.GetResolutionAsync(ct);
                    var members = resolution.SameGame.GroupOf(epic.WorkId)
                        .Concat(resolution.SameGame.GroupOf(other.WorkId)).ToHashSet();
                    var history = await links.GetHistoryAsync(ct: ct);
                    var separations = (await links.GetActsAsync(ct))
                        .Where(a => a.Kind == IdentityActKinds.Unlink).Select(a => a.Id).ToHashSet();
                    var decisions = await candidates.GetAllAsync(ct);
                    var pinned = await pins.GetLivePinnedWorkIdsAsync(ct);

                    // A hard ID does not overrule an explicit user separation,
                    // a pinned mapping, or an expansion/variant relationship.
                    if (members.Overlaps(pinned)
                        || history.Any(l => l.IsLive && l.Kind != IdentityLinkKinds.SameGame
                            && members.Contains(l.ChildWorkId))
                        || history.Any(l => l.RetractedByActId is { } act && separations.Contains(act)
                            && (members.Contains(l.ChildWorkId) || members.Contains(l.ParentWorkId)))
                        || decisions.Any(c => c.Status == MergeCandidateStatuses.Rejected
                            && identities.TryGetValue(c.LeftReleaseId, out var left)
                            && identities.TryGetValue(c.RightReleaseId, out var right)
                            && members.Contains(left.WorkId) && members.Contains(right.WorkId)))
                    {
                        refused++;
                        continue;
                    }

                    var evidence = JsonSerializer.Serialize(new GamesDbEditionLinkEvidence(
                        "gamesdb", game.GameId, key.ProviderId, game.ExternalId, twin.Platform, twin.ExternalId,
                        epicEvidence.EditionGameId, epic.ReleaseId, other.ReleaseId, epicEvidence, otherEvidence),
                        GamesDbEditionEvidenceJsonContext.Default.GamesDbEditionLinkEvidence);
                    var parent = resolution.SameGame.Resolve(other.WorkId);
                    var child = resolution.SameGame.Resolve(epic.WorkId);
                    if (resolution.SameGame.IsParent(child) && !resolution.SameGame.IsParent(parent))
                    {
                        (parent, child) = (child, parent);
                    }
                    if (parent != child)
                    {
                        try
                        {
                            await links.LinkAsync(new IdentityLinkRequest
                            {
                                ParentWorkId = parent,
                                ChildWorkIds = [child],
                                Source = IdentityLinkSources.HardId,
                                ExpectedSameGameRoots = new Dictionary<long, long>
                                {
                                    [epic.WorkId] = resolution.SameGame.Resolve(epic.WorkId),
                                    [other.WorkId] = resolution.SameGame.Resolve(other.WorkId),
                                },
                                ExpectedEditionEvidence = [epicEvidence, otherEvidence],
                                EvidenceJson = evidence,
                                Note = "GamesDB references corroborated by independent native-store mappings to the same IGDB edition game.",
                            }, ct);
                        }
                        catch (IdentityLinkRefusedException ex)
                        {
                            refused++;
                            logger.LogDebug(ex, "Identity changed while gamesdb was resolving a pair; skipped.");
                            continue;
                        }
                        linked++;
                    }
                }
            }

        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Gamesdb identity sync stopped after {Count} links; the next pass can resume.", linked);
        }

        LastResult = new(observed, eligible, linked, unresolvedEdition, conflictingEdition, refused);
        logger.LogInformation(
            "Gamesdb identity sync observed {Observed} store pairs, {Eligible} qualified and {Count} same-game links were created; "
            + "{UnresolvedEdition} lacked native-store edition evidence, {ConflictingEdition} had conflicting edition evidence "
            + "and {Refused} were protected or changed.",
            observed, eligible, linked, unresolvedEdition, conflictingEdition, refused);
        return linked;
    }
}

public sealed record GamesDbIdentitySyncResult(int Observed, int Eligible, int Linked, int UnresolvedEdition,
    int ConflictingEdition, int ProtectedOrChanged);

internal sealed record GamesDbEditionLinkEvidence(string Source, string GameId, string EpicCatalogId, string EpicArtifactId,
    string Provider, string ProviderId, long EditionGameId, long EpicReleaseId, long CounterpartReleaseId,
    ReleaseEditionEvidence EpicEvidence, ReleaseEditionEvidence CounterpartEvidence);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GamesDbEditionLinkEvidence))]
internal partial class GamesDbEditionEvidenceJsonContext : JsonSerializerContext;
