using System.Collections.Concurrent;
using System.Net.Http.Json;
using Winnow.App.Services;
using Winnow.Core.Domain;

namespace Winnow.Api.Client;

public sealed class ApiArtworkBrowserService(WinnowApiClient api) : IArtworkBrowserService
{
    public IArtworkBrowserService CreateSession() => new ApiArtworkBrowserService(api);
    public async Task<IReadOnlyList<ArtworkBrowserSource>> GetSourcesAsync(CancellationToken ct = default)
    {
        await RefreshSourcesAsync(ct);
        return Sources;
    }
    private readonly ConcurrentDictionary<(long Work, ArtworkSlot Slot), string> _revisions = new();
    public IReadOnlyList<ArtworkBrowserSource> Sources { get; private set; } =
        [new("steam", "Steam", [ArtworkSlot.Hero, ArtworkSlot.Cover, ArtworkSlot.Icon]),
         new("igdb", "IGDB", [ArtworkSlot.Hero, ArtworkSlot.Cover])];

    public async Task RefreshSourcesAsync(CancellationToken ct = default)
        => Sources = await api.GetAsync<ArtworkBrowserSource[]>("artwork/sources", ct);

    public async Task<ArtworkCandidate?> GetCurrentAsync(long workId, ArtworkSlot slot, CancellationToken ct = default)
    {
        var state = await api.GetAsync<ArtworkState>(Route(workId, slot), ct);
        _revisions[(workId, slot)] = state.Revision;
        return state.Current;
    }

    public Task<ArtworkBrowserPage> BrowseAsync(long workId, ArtworkSlot slot, string sourceId, string? cursor = null, CancellationToken ct = default)
        => api.GetAsync<ArtworkBrowserPage>($"{Route(workId, slot)}/browse?source={Uri.EscapeDataString(sourceId)}" +
            (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor)), ct);

    public Task<ArtworkSaveResult> SaveAsync(long workId, ArtworkSlot slot, ArtworkCandidate candidate, CancellationToken ct = default)
        => CommittedAsync(workId, slot, api.SendAsync<ArtworkSaveRequest, ArtworkSaveResult>(HttpMethod.Put, Route(workId, slot),
            new(candidate.OfferId ?? throw new InvalidOperationException("Reload the artwork browser before saving."), Revision(workId, slot)), ct: ct), ct);

    public Task<ArtworkSaveResult> ResetAsync(long workId, ArtworkSlot slot, CancellationToken ct = default)
        => CommittedAsync(workId, slot, api.SendAsync<ArtworkResetRequest, ArtworkSaveResult>(HttpMethod.Post,
            Route(workId, slot) + "/reset", new(Revision(workId, slot)), ct: ct), ct);

    public async Task<ArtworkSaveResult> ImportFileAsync(long workId, ArtworkSlot slot, string path, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        if (stream.Length is <= 0 or > 16 * 1024 * 1024)
            return new(false, "Choose a static image up to 16 MB.");
        using var content = new StreamContent(stream);
        return await CommittedAsync(workId, slot, api.UploadArtworkAsync(
            Route(workId, slot) + "/image?revision=" + Uri.EscapeDataString(Revision(workId, slot)), content, ct), ct);
    }

    public Task<ArtworkSaveResult> ImportUrlAsync(long workId, ArtworkSlot slot, string url, CancellationToken ct = default)
        => CommittedAsync(workId, slot, api.SendAsync<ArtworkUrlRequest, ArtworkSaveResult>(HttpMethod.Post,
            Route(workId, slot) + "/url", new(url, Revision(workId, slot)), ct: ct), ct);

    private string Revision(long workId, ArtworkSlot slot) => _revisions.TryGetValue((workId, slot), out var revision)
        ? revision : throw new InvalidOperationException("Load current artwork before saving.");

    private async Task<ArtworkSaveResult> CommittedAsync(long workId, ArtworkSlot slot, Task<ArtworkSaveResult> operation, CancellationToken ct)
    {
        try
        {
            var result = await operation;
            if (result.Success) await GetCurrentAsync(workId, slot, ct);
            return result;
        }
        catch (BackendApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            return new(false, "Artwork changed in another window. Reopen the artwork browser to review the latest choice before saving.");
        }
    }

    private static string Route(long workId, ArtworkSlot slot) => $"works/{workId}/artwork/{slot}";
}

public sealed partial class WinnowApiClient
{
    internal async Task<ArtworkSaveResult> UploadArtworkAsync(string route, HttpContent content, CancellationToken ct)
    {
        using var request = await RequestAsync(HttpMethod.Post, route, ct);
        request.Content = content;
        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<ArtworkSaveResult>(Json, ct)
            ?? throw new InvalidDataException("The backend returned no artwork result.");
    }
}
