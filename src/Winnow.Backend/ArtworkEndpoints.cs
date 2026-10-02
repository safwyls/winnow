using Winnow.Covers;
using Winnow.App.Services;
using Winnow.Core.Domain;

namespace Winnow.Backend;

public static class ArtworkEndpoints
{
    // Bounds decoded and encoded images across all attached frontends.
    private static readonly SemaphoreSlim Images = new(4);

    public static void MapArtworkApi(this WebApplication app)
    {
        app.MapGet("/api/v1/artwork/sources", (ArtworkApplication artwork) => artwork.Sources);
        app.MapGet("/api/v1/works/{workId:long}/backdrop", (long workId, double? aspectRatio, ArtworkApplication artwork, CancellationToken ct)
            => artwork.BackdropAsync(workId, aspectRatio ?? 16d / 9, ct));
        var group = app.MapGroup("/api/v1/works/{workId:long}/artwork/{slot}");
        group.MapGet("", (long workId, ArtworkSlot slot, ArtworkApplication artwork, CancellationToken ct) => artwork.StateAsync(workId, slot, ct));
        group.MapGet("/browse", (long workId, ArtworkSlot slot, string source, string? cursor, ArtworkApplication artwork, CancellationToken ct)
            => artwork.BrowseAsync(workId, slot, source, cursor, ct));
        group.MapPut("", (long workId, ArtworkSlot slot, ArtworkSaveRequest request, ArtworkApplication artwork, CancellationToken ct)
            => artwork.SaveAsync(workId, slot, request, ct));
        group.MapPost("/reset", (long workId, ArtworkSlot slot, ArtworkResetRequest request, ArtworkApplication artwork, CancellationToken ct)
            => artwork.ResetAsync(workId, slot, request.Revision, ct));
        group.MapPost("/url", (long workId, ArtworkSlot slot, ArtworkUrlRequest request, ArtworkApplication artwork, CancellationToken ct)
            => artwork.ImportUrlAsync(workId, slot, request, ct));
        group.MapPost("/image", async (long workId, ArtworkSlot slot, string revision, HttpRequest request, ArtworkApplication artwork, CancellationToken ct) =>
        {
            const int limit = 16 * 1024 * 1024;
            if (request.ContentLength > limit) return Results.StatusCode(413);
            using var data = new MemoryStream();
            var buffer = new byte[81920];
            int count;
            while ((count = await request.Body.ReadAsync(buffer, ct)) > 0)
            {
                if (data.Length + count > limit) return Results.StatusCode(413);
                data.Write(buffer, 0, count);
            }
            return Results.Ok(await artwork.ImportAsync(workId, slot, revision, data.ToArray(), ct));
        });
        app.MapGet("/api/v1/artwork/image", async (string provider, string id, int? width,
            CoverPipeline pipeline, IEnumerable<ICoverSource> sources, CancellationToken ct) =>
        {
            if (provider.Length is < 1 or > 80 || id.Length is < 1 or > 256 ||
                provider.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not ':' and not '.') ||
                id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '.'))
                return Results.BadRequest();
            var key = new CoverKey(provider, id);
            if (!sources.Any(source => source.CanHandle(key))) return Results.NotFound();
            var requestedWidth = width ?? 1920;
            if (requestedWidth is < 64 or > 3840) return Results.BadRequest();
            var bytes = await pipeline.GetPngAsync(key, requestedWidth, Images, ct);
            return bytes is null ? Results.NotFound() : Results.Bytes(bytes, "image/png");
        });
    }
}
