using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Winnow.Core.Repositories;

namespace Winnow.App.Services;

public sealed record AcquisitionCsv(string Content, int OwnershipCount);

/// <summary>A local, versioned ownership export; missing prices stay empty, never zero.</summary>
public sealed class AcquisitionExport(IOwnershipRepository ownerships, IReleaseRepository releases)
{
    public async Task<AcquisitionCsv> ReadAsync(CancellationToken ct = default)
    {
        var identities = (await releases.GetIdentitiesAsync(ct)).ToDictionary(r => r.ReleaseId);
        var rows = await ownerships.GetAllAsync(ct);
        var csv = new StringBuilder("schema_version,ownership_id,release_id,title,store,acquired_at,license_type,price_paid_cents,price_source\r\n");
        foreach (var row in rows.OrderBy(r => r.Id))
        {
            ct.ThrowIfCancellationRequested();
            identities.TryGetValue(row.ReleaseId, out var identity);
            string?[] values =
            [
                "1", row.Id.ToString(CultureInfo.InvariantCulture),
                row.ReleaseId.ToString(CultureInfo.InvariantCulture), identity?.WorkName,
                row.Store, row.AcquiredAt?.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture),
                row.LicenseType, row.PricePaidCents?.ToString(CultureInfo.InvariantCulture), row.PriceSource,
            ];
            csv.AppendJoin(',', values.Select(Quote)).Append("\r\n");
        }

        return new AcquisitionCsv(csv.ToString(), rows.Count);
    }

    private static string Quote(string? value)
        => value is null ? "" : "\"" + value.Replace("\"", "\"\"") + "\"";
}

public interface IAcquisitionExportDestination
{
    /// <summary>Returns false when the user cancels; failures propagate to the visible status.</summary>
    Task<bool> SaveAsync(string csv, CancellationToken ct = default);
}

public sealed class TopLevelAcquisitionExportDestination : IAcquisitionExportDestination
{
    public async Task<bool> SaveAsync(string csv, CancellationToken ct = default)
    {
        var file = await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime
                { MainWindow: { } window } || !window.StorageProvider.CanSave)
            {
                throw new IOException("The save dialog is unavailable.");
            }

            return await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export acquisition CSV",
                SuggestedFileName = "winnow-acquisitions.csv",
                DefaultExtension = "csv",
                ShowOverwritePrompt = true,
                FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }],
            });
        });
        if (file is null) return false;
        using (file)
        {
            ct.ThrowIfCancellationRequested();
            await using var stream = await file.OpenWriteAsync();
            if (stream.CanSeek) stream.SetLength(0);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(true));
            await writer.WriteAsync(csv.AsMemory(), ct);
        }
        return true;
    }
}
