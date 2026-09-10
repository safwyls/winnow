using System.Text.Json;
using Microsoft.Extensions.Logging;
using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Enrich.GamesDb.Model;

namespace Winnow.App.Services;

/// <summary>Applies gamesdb's exact store joins as reversible work identities.</summary>
public sealed class GamesDbIdentitySyncService(
    IReleaseRepository releases,
    IIdentityLinkRepository links,
    IMergeCandidateRepository candidates,
    IWorkIgdbPinRepository pins,
    EnrichmentLookupPlanner planner,
    ILogger<GamesDbIdentitySyncService> logger)
{
    public async Task<int> SyncAsync(CancellationToken ct = default)
    {
        var linked = 0;
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
                        continue;
                    }

                    var evidence = JsonSerializer.Serialize(new
                    {
                        source = "gamesdb", gameId = game.GameId,
                        epicCatalogId = key.ProviderId, epicArtifactId = game.ExternalId,
                        provider = twin.Platform, providerId = twin.ExternalId,
                    });
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
                                EvidenceJson = evidence,
                                Note = "Same game identified by gamesdb store IDs.",
                            }, ct);
                        }
                        catch (IdentityLinkRefusedException ex)
                        {
                            logger.LogDebug(ex, "Identity changed while gamesdb was resolving a pair; skipped.");
                            continue;
                        }
                        linked++;
                    }

                    // Close other pending pairs now answered by this group too.
                    foreach (var pending in decisions.Where(c => c.Status == MergeCandidateStatuses.Pending))
                    {
                        if (identities.TryGetValue(pending.LeftReleaseId, out var left)
                            && identities.TryGetValue(pending.RightReleaseId, out var right)
                            && members.Contains(left.WorkId) && members.Contains(right.WorkId))
                        {
                            await candidates.WithdrawPendingAsync(pending.Id, ct);
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Gamesdb identity sync stopped after {Count} links; the next pass can resume.", linked);
        }

        logger.LogInformation("Gamesdb identity sync created {Count} same-game links.", linked);
        return linked;
    }
}
