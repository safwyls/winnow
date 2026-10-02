using System.Collections.Concurrent;
using System.Net;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Details;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Covers;
using Winnow.Covers.Igdb;

namespace Winnow.App.Services;

/// <summary>Keeps each editor's observed revision; writes never silently reread a newer revision.</summary>
public sealed class ApiWorkMetadataEditService(WinnowApiClient api) : IWorkMetadataEditService
{
    private readonly ConcurrentDictionary<long, string> _revisions = new();
    private readonly DetailsClient _details = new(api);

    public async Task<WorkMetadataSnapshot?> GetAsync(long workId, CancellationToken ct = default)
    {
        try
        {
            var response = await _details.GetMetadataAsync(workId, ct);
            _revisions[workId] = response.Revision;
            return new(response.WorkId, response.Title, response.IsPinned,
                response.Fields.Select(x => new WorkMetadataField(x.Field, x.Value, x.Source)).ToArray());
        }
        catch (BackendApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return null; }
    }

    public async Task<WorkFieldEditOutcome> SetFieldAsync(long workId, string field, string? value, CancellationToken ct = default)
    {
        if (!_revisions.TryGetValue(workId, out var revision)) return WorkFieldEditOutcome.InvalidValue;
        try
        {
            var outcome = await _details.SetMetadataAsync(workId, new(field, value, revision), ct);
            if (outcome.Outcome == nameof(WorkFieldEditOutcome.Applied)) await GetAsync(workId, ct);
            return Enum.TryParse<WorkFieldEditOutcome>(outcome.Outcome, out var result) ? result : WorkFieldEditOutcome.InvalidValue;
        }
        catch (BackendApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict) { return WorkFieldEditOutcome.Conflict; }
        catch (BackendApiException) { return WorkFieldEditOutcome.InvalidValue; }
    }

    public async Task<WorkFieldEditOutcome> ResetFieldAsync(long workId, string field, CancellationToken ct = default)
    {
        if (!_revisions.TryGetValue(workId, out var revision)) return WorkFieldEditOutcome.InvalidValue;
        try
        {
            var outcome = await _details.ResetMetadataAsync(workId, new(field, revision), ct);
            if (outcome.Outcome == nameof(WorkFieldEditOutcome.Applied)) await GetAsync(workId, ct);
            return Enum.TryParse<WorkFieldEditOutcome>(outcome.Outcome, out var result) ? result : WorkFieldEditOutcome.InvalidValue;
        }
        catch (BackendApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict) { return WorkFieldEditOutcome.Conflict; }
        catch (BackendApiException) { return WorkFieldEditOutcome.InvalidValue; }
    }

    public async Task<WorkArtEditOutcome> SetArtFromFileAsync(long workId, string field, string filePath, CancellationToken ct = default)
    {
        if (!_revisions.TryGetValue(workId, out var revision)) return WorkArtEditOutcome.Failed;
        try
        {
            if (!File.Exists(filePath)) return WorkArtEditOutcome.FileNotFound;
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > UserArtStore.MaxBytes) return WorkArtEditOutcome.TooLarge;
            var bytes = new byte[checked((int)stream.Length)];
            await stream.ReadExactlyAsync(bytes, ct);
            var outcome = await api.SendAsync<UploadMetadataArtRequest, MutationOutcome>(HttpMethod.Post,
                $"games/{workId}/metadata/art-upload", new(field, bytes, revision), ct: ct);
            if (outcome.Outcome == nameof(WorkArtEditOutcome.Applied)) await GetAsync(workId, ct);
            return Enum.TryParse<WorkArtEditOutcome>(outcome.Outcome, out var result) ? result : WorkArtEditOutcome.Failed;
        }
        catch (IOException) { return WorkArtEditOutcome.Unreadable; }
        catch (UnauthorizedAccessException) { return WorkArtEditOutcome.Unreadable; }
        catch (BackendApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict) { return WorkArtEditOutcome.Conflict; }
        catch (BackendApiException) { return WorkArtEditOutcome.Failed; }
    }

    public async Task<WorkArtEditOutcome> SetArtFromUrlAsync(long workId, string field, string url, CancellationToken ct = default)
    {
        if (!_revisions.TryGetValue(workId, out var revision)) return WorkArtEditOutcome.Failed;
        try
        {
            var outcome = await api.SendAsync<DownloadMetadataArtRequest, MutationOutcome>(HttpMethod.Post,
                $"games/{workId}/metadata/art-download", new(field, url, revision), ct: ct);
            if (outcome.Outcome == nameof(WorkArtEditOutcome.Applied)) await GetAsync(workId, ct);
            return Enum.TryParse<WorkArtEditOutcome>(outcome.Outcome, out var result) ? result : WorkArtEditOutcome.Failed;
        }
        catch (BackendApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict) { return WorkArtEditOutcome.Conflict; }
        catch (BackendApiException) { return WorkArtEditOutcome.Failed; }
    }

    public CoverKey? ArtKeyFor(string? value) => ArtKeys.Resolve(value);
}

public sealed class ApiIgdbAssignmentService(WinnowApiClient api) : IIgdbAssignmentService
{
    private readonly DetailsClient _details = new(api);
    private readonly Dictionary<long, string> _revisions = [];
    public async Task<IReadOnlyList<IgdbCandidate>> SearchAsync(string title, CancellationToken ct = default)
        => (await _details.SearchIgdbAsync(title, ct)).Select(Candidate).ToArray();
    public async Task<IgdbCandidate?> GetCandidateByIdAsync(long igdbId, CancellationToken ct = default)
    {
        try { return Candidate(await _details.GetIgdbCandidateAsync(igdbId, ct)); }
        catch (BackendApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return null; }
    }
    public async Task<IgdbAssignmentOutcome> AssignAsync(long workId, long igdbId, CancellationToken ct = default)
    {
        if (!_revisions.TryGetValue(workId, out var revision)) return IgdbAssignmentOutcome.MappingChanged;
        try
        {
            var response = await _details.AssignIgdbAsync(workId, new(igdbId, revision), ct);
            return Enum.TryParse<IgdbAssignmentOutcome>(response.Outcome, out var result) ? result : IgdbAssignmentOutcome.Failed;
        }
        catch (BackendApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        { return IgdbAssignmentOutcome.MappingChanged; }
    }
    public async Task<IgdbClaimingGame?> FindClaimingGameAsync(long igdbId, CancellationToken ct = default)
    {
        try
        {
            var result = await _details.GetIgdbClaimingGameAsync(igdbId, ct);
            return new(result.WorkId, result.Title, result.CoverUrl, result.FirstReleaseYear);
        }
        catch (BackendApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return null; }
    }
    public async Task<bool> ClearAsync(long workId, CancellationToken ct = default)
    {
        if (!_revisions.TryGetValue(workId, out var revision)) return false;
        try { return await _details.ClearIgdbAsync(workId, new(revision), ct); }
        catch (BackendApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict) { return false; }
    }
    public async Task<WorkIgdbPin?> GetPinAsync(long workId, CancellationToken ct = default)
    {
        try
        {
            var state = await _details.GetIgdbStateAsync(workId, ct);
            _revisions[workId] = state.Revision;
            return state.Pin;
        }
        catch (BackendApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return null; }
    }
    public async Task<IReadOnlySet<long>> GetLivePinnedWorkIdsAsync(CancellationToken ct = default)
        => (await api.GetAsync<Winnow.Api.Contracts.Library.LibraryWorkspaceResponse>("library/workspace", ct)).PinnedWorkIds.ToHashSet();
    private static IgdbCandidate Candidate(IgdbCandidateResponse value) => new(value.IgdbId, value.Name, value.CoverUrl, value.FirstReleaseYear, value.Platforms);
}

public sealed class ApiUpdateFlagService(WinnowApiClient api) : IUpdateFlagService
{
    private readonly DetailsClient _details = new(api);
    public async Task<UpdateFlagOutcome> DismissAsync(long releaseId, IReadOnlyList<UpdateEvent> events, CancellationToken ct = default)
    {
        var result = await _details.AcknowledgeUpdatesAsync(releaseId, new(events.Where(x => x.ReleaseId == releaseId).Select(x => x.Id).ToArray()), ct);
        return Outcome(result);
    }
    public async Task<UpdateFlagOutcome> RestoreAsync(long releaseId, CancellationToken ct = default)
        => Outcome(await _details.RestoreUpdatesAsync(releaseId, ct));
    public async Task<DateTime?> GetStandingAsync(long releaseId, CancellationToken ct = default)
        => (await api.GetAsync<AcknowledgementResponse>($"releases/{releaseId}/acknowledgement", ct)).AcknowledgedThrough;
    public DateTime? ReadThrough(long releaseId, IReadOnlyList<UpdateEvent> events, DateTime? acknowledgedThrough)
    {
        if (acknowledgedThrough is not { } watermark) return null;
        return events.Where(x => x.ReleaseId == releaseId && x.Kind == UpdateEventKinds.Announcement
                && Math.Abs((x.OccurredAt - watermark).TotalDays) <= BucketThresholds.Default.UpdateCorrelationWindowDays)
            .Select(x => x.OccurredAt).Append(watermark).Max();
    }
    private static UpdateFlagOutcome Outcome(AcknowledgementResponse response)
        => new(Enum.TryParse<UpdateFlagResult>(response.Result, out var result) ? result : UpdateFlagResult.NotStored, response.AcknowledgedThrough);
}
