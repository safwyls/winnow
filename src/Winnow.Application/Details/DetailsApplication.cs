using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Winnow.Api.Contracts.Details;
using Winnow.Api.Contracts.Library;
using Winnow.App.Services;
using Winnow.Application.Library;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;

namespace Winnow.Application.Details;

public sealed class DetailsApplication(ILibraryApplication library, ILibraryQueryRepository queries,
    IUnitOfWorkFactory transactions, IUpdateEventRepository updates, IUpdateAcknowledgementRepository acknowledgements,
    IPlaytimeSnapshotRepository snapshots, ISessionRepository sessions, IWorkRatingRepository ratings,
    IWorkImageRepository images, IAccountAcquisitionReader acquisitions, IAchievementQueryRepository achievements,
    IGameListRepository lists, IWorkMetadataEditService metadata, IUpdateFlagService flags,
    IActivityRepository activity, IGameplayStatsRepository gameplay, IAccountStatsRepository accounts, ILifecycleRepository lifecycle,
    ISteamPlaytimeObservationRepository steamObservations, ISettingsRepository settings,
    IApplicationChangePublisher changes, IWorkRepository works, IWorkIgdbPinRepository igdbPins,
    IIgdbAssignmentService? igdb = null) : IDetailsApplication
{
    public async Task<VisibilityCountsResponse> GetVisibilityCountsAsync(CancellationToken ct = default)
    {
        using var transaction = transactions.Begin();
        var preferences = await library.GetPreferencesAsync(ct);
        var thresholds = BucketThresholds.Default with
        {
            ShowNonGameEntries = preferences.ShowNonGameEntries, ShowExplicitContent = preferences.ShowExplicitContent,
            MaturityCap = BucketThresholds.ParseMaturityCap(preferences.MaturityCap),
        };
        var result = new VisibilityCountsResponse(await queries.CountHiddenByAccountScopeAsync(thresholds, ct),
            await queries.CountHiddenByExplicitFilterAsync(thresholds, ct), await queries.CountHiddenByRatingCapAsync(thresholds, ct));
        transaction.Commit();
        return result;
    }

    public async Task ExemptFromDerelictAsync(DerelictExemptionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request.WorkIds);
        using var transaction = transactions.Begin();
        var snapshot = await ReadVisibleAsync(null, ct);
        var roots = request.WorkIds.Select(snapshot.IdentityResolution.SameGame.Resolve).ToHashSet();
        var releaseIds = snapshot.Buckets.Where(x => roots.Contains(x.ResolvedWorkId)).Select(x => x.ReleaseId).Distinct().ToArray();
        await lifecycle.ExemptFromDerelictAsync(releaseIds, ct);
        transaction.Commit();
        changes.Publish("library.changed", "derelict-exemptions");
    }

    public async Task<GameDetailsResponse> GetDetailsAsync(long workId, LibraryPreferences? preferences = null, CancellationToken ct = default)
    {
        using var transaction = transactions.Begin();
        var snapshot = await ReadVisibleAsync(preferences, ct);
        var root = snapshot.IdentityResolution.SameGame.Resolve(workId);
        var visible = snapshot.Buckets.Where(x => x.ResolvedWorkId == root).ToArray();
        if (visible.Length == 0) throw new ApplicationNotFoundException("Game is not in the visible library.");
        var releaseIds = visible.Select(x => x.ReleaseId).Distinct().ToArray();
        var ownershipIds = visible.Select(x => x.OwnershipId).Distinct().ToArray();
        var events = new List<UpdateEvent>();
        var watermarks = new Dictionary<long, DateTime>();
        foreach (var releaseId in releaseIds)
        {
            events.AddRange(await updates.GetByReleaseAsync(releaseId, ct));
            if (await acknowledgements.GetStandingAsync(releaseId, ct) is { } ack) watermarks[releaseId] = ack.AcknowledgedThrough;
        }
        var history = new Dictionary<long, IReadOnlyList<PlaytimeSnapshot>>();
        var sittings = new Dictionary<long, IReadOnlyList<Session>>();
        var journal = new List<SessionJournalEntry>();
        foreach (var ownershipId in ownershipIds)
        {
            history[ownershipId] = await snapshots.GetByOwnershipAsync(ownershipId, ct);
            sittings[ownershipId] = await sessions.GetByOwnershipAsync(ownershipId, ct);
            journal.AddRange(await sessions.GetJournalEntriesByOwnershipAsync(ownershipId, ct));
        }
        var selectedOwnerships = snapshot.Ownerships.Where(x => ownershipIds.Contains(x.Id)).ToArray();
        var result = new GameDetailsResponse(root, DateTime.UtcNow,
            events.OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id).ToArray(), watermarks,
            history, sittings, await ratings.GetForWorkAsync(root, ct),
            await BackdropImages.LoadAsync(images, root, visible.Select(x => x.WorkId), ct),
            await acquisitions.ProjectAsync(selectedOwnerships, ct), journal,
            await lists.GetMembershipForGameAsync(root, ct), await achievements.GetSummariesAsync(releaseIds, ct));
        transaction.Commit();
        return result;
    }

    public async Task<SessionPromptResponse> GetSessionPromptAsync(long sessionId, CancellationToken ct = default)
    {
        var session = await sessions.GetAsync(sessionId, ct);
        if (session?.EndedAt is null) throw new ApplicationNotFoundException("Completed session was not found.");
        return new(session.Id, session.OwnershipId, session.DurationSeconds ?? 0);
    }

    public async Task<JournalResponse> GetJournalAsync(long sessionId, CancellationToken ct = default)
    {
        if (await sessions.GetAsync(sessionId, ct) is null) throw new ApplicationNotFoundException("Session was not found.");
        var note = await sessions.GetNoteAsync(sessionId, ct);
        return Journal(sessionId, note?.Note, note?.Rating);
    }

    public async Task<JournalResponse> SaveJournalAsync(long sessionId, SaveJournalRequest request, CancellationToken ct = default)
    {
        if (request.Rating is < 1 or > 5) throw new ArgumentException("A session rating must be between 1 and 5.");
        using var transaction = transactions.Begin();
        var previous = await GetJournalAsync(sessionId, ct);
        CheckRevision(previous.Revision, request.ExpectedRevision);
        await sessions.SetNoteAsync(new SessionNote { SessionId = sessionId, Note = request.Note, Rating = request.Rating }, ct);
        var result = await GetJournalAsync(sessionId, ct);
        transaction.Commit();
        changes.Publish("library.changed", $"sessions/{sessionId}/journal");
        return result;
    }

    public async Task DeleteJournalAsync(long sessionId, string expectedRevision, CancellationToken ct = default)
    {
        using var transaction = transactions.Begin();
        CheckRevision((await GetJournalAsync(sessionId, ct)).Revision, expectedRevision);
        await sessions.DeleteNoteAsync(sessionId, ct);
        transaction.Commit();
        changes.Publish("library.changed", $"sessions/{sessionId}/journal");
    }

    public async Task<AcknowledgementResponse> AcknowledgeUpdatesAsync(long releaseId, UpdateAcknowledgementRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request.ObservedEventIds);
        var observed = request.ObservedEventIds.ToHashSet();
        var events = (await updates.GetByReleaseAsync(releaseId, ct)).Where(x => observed.Contains(x.Id)).ToArray();
        var outcome = await flags.DismissAsync(releaseId, events, ct);
        if (outcome.Saved) changes.Publish("library.changed", $"releases/{releaseId}/updates");
        return new(outcome.Result.ToString(), outcome.AcknowledgedThrough);
    }

    public async Task<AcknowledgementResponse> RestoreUpdatesAsync(long releaseId, CancellationToken ct = default)
    {
        var outcome = await flags.RestoreAsync(releaseId, ct);
        if (outcome.Saved) changes.Publish("library.changed", $"releases/{releaseId}/updates");
        return new(outcome.Result.ToString(), outcome.AcknowledgedThrough);
    }

    public async Task<AcknowledgementResponse> GetAcknowledgementAsync(long releaseId, CancellationToken ct = default)
        => await acknowledgements.GetStandingAsync(releaseId, ct) is { } value
            ? new("Stored", value.AcknowledgedThrough) : new("NothingToDo", null);

    public async Task<MetadataResponse> GetMetadataAsync(long workId, CancellationToken ct = default)
    {
        var state = await metadata.GetAsync(workId, ct) ?? throw new ApplicationNotFoundException("Game metadata was not found.");
        var fields = state.Fields.Select(x => new MetadataFieldResponse(x.Field, x.Value, x.Source)).ToArray();
        var response = new MetadataResponse(state.WorkId, state.Title, state.IsPinned, fields, "");
        return response with { Revision = Hash(JsonSerializer.SerializeToUtf8Bytes(response, DetailsJsonContext.Default.MetadataResponse)) };
    }

    public async Task<MutationOutcome> SetMetadataAsync(long workId, EditMetadataRequest request, CancellationToken ct = default)
    {
        using var transaction = transactions.Begin();
        CheckRevision((await GetMetadataAsync(workId, ct)).Revision, request.ExpectedRevision);
        var outcome = await metadata.SetFieldAsync(workId, request.Field, request.Value, ct);
        transaction.Commit();
        if (outcome == WorkFieldEditOutcome.Applied) changes.Publish("library.changed", $"games/{workId}/metadata");
        return new(outcome.ToString());
    }

    public async Task<MutationOutcome> ResetMetadataAsync(long workId, ResetMetadataRequest request, CancellationToken ct = default)
    {
        using var transaction = transactions.Begin();
        CheckRevision((await GetMetadataAsync(workId, ct)).Revision, request.ExpectedRevision);
        var outcome = await metadata.ResetFieldAsync(workId, request.Field, ct);
        transaction.Commit();
        if (outcome == WorkFieldEditOutcome.Applied) changes.Publish("library.changed", $"games/{workId}/metadata");
        return new(outcome.ToString());
    }

    public async Task<IReadOnlyList<IgdbCandidateResponse>> SearchIgdbAsync(string title, CancellationToken ct = default)
        => igdb is null ? [] : (await igdb.SearchAsync(title, ct)).Select(Candidate).ToArray();

    public async Task<MutationOutcome> UploadArtAsync(long workId, UploadMetadataArtRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request.Content);
        if (request.Content.LongLength > Winnow.Covers.UserArtStore.MaxBytes) return new("TooLarge");
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, request.Content, ct);
            using var transaction = transactions.Begin();
            CheckRevision((await GetMetadataAsync(workId, ct)).Revision, request.ExpectedRevision);
            var result = await metadata.SetArtFromFileAsync(workId, request.Field, path, ct);
            transaction.Commit();
            if (result == WorkArtEditOutcome.Applied) changes.Publish("library.changed", $"games/{workId}/metadata");
            return new(result.ToString());
        }
        finally { File.Delete(path); }
    }

    public async Task<MutationOutcome> DownloadArtAsync(long workId, DownloadMetadataArtRequest request, CancellationToken ct = default)
    {
        using var transaction = transactions.Begin();
        CheckRevision((await GetMetadataAsync(workId, ct)).Revision, request.ExpectedRevision);
        var result = await metadata.SetArtFromUrlAsync(workId, request.Field, request.Url, ct);
        transaction.Commit();
        if (result == WorkArtEditOutcome.Applied) changes.Publish("library.changed", $"games/{workId}/metadata");
        return new(result.ToString());
    }
    public async Task<IgdbCandidateResponse?> GetIgdbCandidateAsync(long igdbId, CancellationToken ct = default)
        => igdb is not null && await igdb.GetCandidateByIdAsync(igdbId, ct) is { } candidate ? Candidate(candidate) : null;
    public async Task<IgdbClaimingGameResponse?> GetIgdbClaimingGameAsync(long igdbId, CancellationToken ct = default)
        => igdb is not null && await igdb.FindClaimingGameAsync(igdbId, ct) is { } holder
            ? new(holder.WorkId, holder.Title, holder.CoverUrl, holder.FirstReleaseYear) : null;
    public async Task<MutationOutcome> AssignIgdbAsync(long workId, AssignIgdbRequest request, CancellationToken ct = default)
    {
        using var transaction = transactions.Begin();
        CheckRevision((await ReadIgdbStateAsync(workId, ct)).Revision, request.ExpectedRevision);
        var outcome = igdb is null ? IgdbAssignmentOutcome.MetadataUnavailable : await igdb.AssignAsync(workId, request.IgdbId, ct);
        if (outcome == IgdbAssignmentOutcome.Assigned)
        {
            transaction.Commit();
            changes.Publish("library.changed", $"games/{workId}/metadata");
        }
        return new(outcome.ToString());
    }
    public async Task<bool> ClearIgdbAsync(long workId, ClearIgdbRequest request, CancellationToken ct = default)
    {
        using var transaction = transactions.Begin();
        CheckRevision((await ReadIgdbStateAsync(workId, ct)).Revision, request.ExpectedRevision);
        var result = igdb is not null && await igdb.ClearAsync(workId, ct);
        if (result)
        {
            transaction.Commit();
            changes.Publish("library.changed", $"games/{workId}/metadata");
        }
        return result;
    }
    public Task<WorkIgdbPin?> GetIgdbPinAsync(long workId, CancellationToken ct = default)
        => igdb?.GetPinAsync(workId, ct) ?? Task.FromResult<WorkIgdbPin?>(null);

    public async Task<IgdbStateResponse> GetIgdbStateAsync(long workId, CancellationToken ct = default)
    {
        using var transaction = transactions.Begin();
        var state = await ReadIgdbStateAsync(workId, ct);
        transaction.Commit();
        return state;
    }

    private async Task<IgdbStateResponse> ReadIgdbStateAsync(long workId, CancellationToken ct)
    {
        var work = await works.GetAsync(workId, ct) ?? throw new ApplicationNotFoundException("Game was not found.");
        var state = new IgdbStateResponse(workId, work.IgdbMappingRevision, await igdbPins.GetAsync(workId, ct), "", Available: igdb is not null);
        return state with { Revision = Hash(JsonSerializer.SerializeToUtf8Bytes(state, DetailsJsonContext.Default.IgdbStateResponse)) };
    }

    public async Task<ActivityPage> GetActivityAsync(ActivityRequest request, CancellationToken ct = default)
    {
        using var transaction = transactions.Begin();
        var snapshot = await ReadVisibleAsync(null, ct);
        var root = request.WorkId is { } workId ? snapshot.IdentityResolution.SameGame.Resolve(workId) : (long?)null;
        var ownershipIds = snapshot.Buckets.Where(x => root is null || x.ResolvedWorkId == root).Select(x => x.OwnershipId).Distinct().ToArray();
        var result = await activity.GetPageAsync(ownershipIds, request.FromUtc, request.UntilUtc, request.Section,
            request.After, request.PageSize, ct);
        transaction.Commit();
        return result;
    }

    public async Task<GameplayStats> GetGameplayStatsAsync(GameplayStatisticsRequest request, CancellationToken ct = default)
    {
        using var transaction = transactions.Begin();
        var snapshot = await ReadVisibleAsync(null, ct);
        var result = await gameplay.GetAsync(new GameplayStatsRequest
        {
            Ownerships = snapshot.Buckets.Select(x => new GameplayOwnershipScope(x.OwnershipId, x.ResolvedWorkId)).ToArray(),
            FromUtc = request.FromUtc, UntilUtc = request.UntilUtc, AsOfUtc = request.AsOfUtc,
            TimeBins = request.TimeBins, Store = request.Store,
        }, ct);
        transaction.Commit();
        return result;
    }

    public async Task<SteamActivityResponse> GetSteamActivityAsync(SteamActivityRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request.OwnershipIds);
        using var transaction = transactions.Begin();
        var snapshot = await ReadVisibleAsync(null, ct);
        var requested = request.OwnershipIds.ToHashSet();
        var visible = snapshot.Buckets.Where(x => requested.Contains(x.OwnershipId)).Select(x => x.OwnershipId).Distinct().ToArray();
        var ownOnly = AccountScope.IsOwnOnly(await settings.GetAsync(AccountScope.SettingKey, ct));
        var account = ownOnly ? SteamOwnedAccount.Clean(await settings.GetAsync(SteamOwnedAccount.RefSettingKey, ct)) : null;
        if (uint.TryParse(account, out var id)) account = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (ownOnly && account is null) return new(true, []);
        var rows = await steamObservations.GetActivityAsync(visible, DateTime.UtcNow, account, ct);
        var result = new SteamActivityResponse(false, rows.Where(x => (account is null || x.AccountRef == account)
            && (x.ComparisonUnavailable || x.UnexplainedMinutes > 0)).OrderByDescending(x => x.WindowEndedAt).ToArray());
        transaction.Commit();
        return result;
    }

    public Task<AccountStats> GetAccountStatsAsync(string source, CancellationToken ct = default)
        => accounts.GetAsync(source, ct);

    private async Task<LibrarySnapshot> ReadVisibleAsync(LibraryPreferences? preferences, CancellationToken ct)
    {
        preferences ??= await library.GetPreferencesAsync(ct);
        return await queries.GetSnapshotAsync(BucketThresholds.Default with
        {
            ShowNonGameEntries = preferences.ShowNonGameEntries, ShowExplicitContent = preferences.ShowExplicitContent,
            MaturityCap = BucketThresholds.ParseMaturityCap(preferences.MaturityCap),
        }, ct);
    }
    private static IgdbCandidateResponse Candidate(IgdbCandidate value) => new(value.IgdbId, value.Name, value.CoverUrl, value.FirstReleaseYear, value.Platforms);
    private static JournalResponse Journal(long id, string? note, int? rating)
    {
        var response = new JournalResponse(id, note, rating, "");
        return response with { Revision = JournalRevision.For(id, note, rating) };
    }
    private static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value));
    private static void CheckRevision(string actual, string expected)
    {
        if (string.IsNullOrWhiteSpace(expected)) throw new ArgumentException("The loaded revision is required.");
        if (actual != expected) throw new ApplicationConflictException("This value changed. Reload it before editing.");
    }
}

[JsonSerializable(typeof(JournalResponse))]
[JsonSerializable(typeof(MetadataResponse))]
[JsonSerializable(typeof(IgdbStateResponse))]
internal sealed partial class DetailsJsonContext : JsonSerializerContext;
