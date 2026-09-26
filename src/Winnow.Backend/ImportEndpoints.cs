using Winnow.Api.Contracts.Details;
using Winnow.Application.Details;
using Winnow.Core.Ingest;

namespace Winnow.Backend;

internal static class ImportEndpoints
{
    public static void MapImportEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1");
        api.MapGet("/exports/acquisitions", (ImportApplication service, CancellationToken ct) => service.ExportAcquisitionsAsync(ct));
        api.MapPost("/imports/steam/load-files", (SteamPageUploadRequest request, ImportApplication service, CancellationToken ct) => service.LoadPagesAsync(request, ct));
        api.MapPost("/imports/steam/pages", (SteamAccountPages request, ImportApplication service, CancellationToken ct) => service.ImportPagesAsync(request, ct));
    }
}
