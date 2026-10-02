using System.Text.Json;
using Winnow.App.ViewModels;
using Winnow.Core.Identity;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Enrich.Igdb;
using Winnow.Enrich.Igdb.Model;
using Winnow.Enrich.Igdb.Storage;
using Winnow.Enrich.Stores;

namespace Winnow.App.Services;

public sealed record EditionAcquisition(ReleaseEditionEvidence? Evidence, bool Conflicting = false);

public interface IReleaseEditionEvidenceAcquirer
{
    Task<IReadOnlyDictionary<TargetKey, EditionAcquisition>> AcquireAsync(
        IReadOnlyList<EnrichmentTarget> targets, CancellationToken ct = default);
}

/// <summary>Native store evidence remains independent of GamesDB's game-level metadata routes.</summary>
public sealed class ReleaseEditionEvidenceAcquirer(
    IIgdbClient igdb, IgdbOptions options, IMetadataCache cache, StorefrontClient storefront,
    IReleaseEditionEvidenceRepository evidenceRepository) : IReleaseEditionEvidenceAcquirer
{
    public async Task<IReadOnlyDictionary<TargetKey, EditionAcquisition>> AcquireAsync(
        IReadOnlyList<EnrichmentTarget> targets, CancellationToken ct = default)
    {
        var results = targets.ToDictionary(target => new TargetKey(target.Provider, target.ProviderId), _ => new EditionAcquisition(null));
        var routes = new List<NativeRoute>();
        var epicIds = targets.Where(target => target.Provider == "epic").Select(target => target.ProviderId).ToArray();
        var local = await cache.GetManyAsync(SqliteEpicLaunchKeyStore.Provider, epicIds, ct);
        var remote = await cache.GetManyAsync(SqliteEpicCatalogCache.Provider, epicIds, ct);
        foreach (var target in targets)
        {
            if (target.Provider is "steam" or "gog")
            {
                if (target.ProviderId.Length is > 0 and <= 20 && target.ProviderId.All(char.IsAsciiDigit)
                    && target.ProviderId.Any(character => character != '0'))
                    routes.Add(new(target, target.Provider == "steam" ? options.SteamExternalGameSourceId : options.GogExternalGameSourceId,
                        [target.ProviderId], [], DateTime.MaxValue));
                continue;
            }
            if (target.Provider != "epic") continue;
            var localPayload = local.GetValueOrDefault(target.ProviderId).PayloadJson;
            var remotePayload = remote.GetValueOrDefault(target.ProviderId).PayloadJson;
            var launch = ParseLaunch(target.ProviderId, localPayload) ?? ParseLaunch(target.ProviderId, remotePayload);
            if (launch is null) continue;
            var page = await storefront.GetEpicEditionIdsAsync(launch, ct);
            if (page is null) continue;
            if (page.Conflicting)
            {
                results[new(target.Provider, target.ProviderId)] = new(null, true);
                continue;
            }
            if (page.NativeIds.Count == 0) continue;
            routes.Add(new(target, options.EpicExternalGameSourceId, page.NativeIds,
                [CachedEvidenceSource.FromPayload(SqliteEpicLaunchKeyStore.Provider, target.ProviderId, localPayload),
                 CachedEvidenceSource.FromPayload(SqliteEpicCatalogCache.Provider, target.ProviderId, remotePayload), page.Source],
                page.ValidUntilUtc));
        }

        var observations = new Dictionary<TargetKey, ReleaseEditionEvidence>();
        foreach (var source in routes.GroupBy(route => route.SourceId))
        {
            var matches = await igdb.ResolveEditionsByExternalIdsAsync(source.Key, source.SelectMany(route => route.Uids), ct: ct);
            foreach (var route in source)
            {
                var key = new TargetKey(route.Target.Provider, route.Target.ProviderId);
                if (route.Uids.Any(uid => !matches.ContainsKey(uid))) continue;
                var answers = route.Uids.Select(uid => matches[uid]).ToArray();
                var editions = answers.Where(answer => answer.Status == IgdbEditionMatchStatus.Edition).ToArray();
                var identities = editions.Select(answer => (answer.EditionGameId, answer.VersionParentId, answer.VersionTitle)).Distinct().ToArray();
                if (answers.Any(answer => answer.Status == IgdbEditionMatchStatus.Ambiguous)
                    || identities.Length > 1
                    || editions.Length > 0 && answers.Any(answer => answer.Status == IgdbEditionMatchStatus.NotEdition))
                {
                    results[key] = new(null, true);
                    continue;
                }
                if (editions.Length == 0) continue;
                var edition = editions[0];
                if (edition.EditionGameId is not > 0 || edition.VersionParentId is not > 0 || string.IsNullOrWhiteSpace(edition.VersionTitle)) continue;
                observations[key] = new ReleaseEditionEvidence
                {
                    ReleaseId = route.Target.ReleaseId, WorkId = route.Target.WorkId,
                    Provider = key.Provider, ProviderId = key.ProviderId,
                    EditionGameId = edition.EditionGameId.Value, VersionParentId = edition.VersionParentId.Value,
                    VersionTitle = edition.VersionTitle,
                    Sources = route.Sources.Concat(answers.Select(answer => answer.Source)).Distinct()
                        .OrderBy(item => item.Provider, StringComparer.Ordinal).ThenBy(item => item.Key, StringComparer.Ordinal).ToArray(),
                    ValidUntilUtc = answers.Select(answer => answer.ValidUntilUtc).Append(route.ValidUntilUtc).Min(),
                };
            }
        }
        foreach (var group in targets.GroupBy(target => target.ReleaseId))
        {
            var keys = group.Select(target => new TargetKey(target.Provider, target.ProviderId)).ToArray();
            var positive = keys.Where(observations.ContainsKey).Select(key => observations[key]).ToArray();
            if (keys.Any(key => results[key].Conflicting) || positive.Select(evidence => evidence.EditionGameId).Distinct().Count() > 1)
            {
                foreach (var key in keys) results[key] = new(null, true);
                continue;
            }
            // Every current native identifier on the release must be accounted for.
            if (positive.Length != keys.Length) continue;
            foreach (var key in keys) results[key] = new(await evidenceRepository.RecordAsync(observations[key], ct));
        }
        return results;
    }

    private static EpicEditionTarget? ParseLaunch(string catalogId, string? payload)
    {
        if (payload is null) return null;
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var names = root.EnumerateObject().Select(property => property.Name).ToArray();
            if (names.Distinct(StringComparer.Ordinal).Count() != names.Length) return null;
            var ns = Text(root, "Namespace");
            var app = Text(root, "AppName");
            return EpicLaunchKey.Create(ns, catalogId, app) is not null ? new(ns!, catalogId, app!) : null;
        }
        catch (JsonException) { return null; }
        static string? Text(JsonElement root, string name)
            => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private sealed record NativeRoute(EnrichmentTarget Target, int SourceId, IReadOnlyList<string> Uids,
        IReadOnlyList<CachedEvidenceSource> Sources, DateTime ValidUntilUtc);
}
