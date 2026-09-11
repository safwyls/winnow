using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

public sealed class AcquisitionExportTests
{
    [Fact]
    public async Task Export_preserves_acquisition_facts_and_quotes_titles()
    {
        using var db = new TempDatabase();
        var work = await new WorkRepository(db.Factory).InsertAsync(new Work { Name = "A, \"game\"\r\npart two" });
        var release = await new ReleaseRepository(db.Factory).InsertAsync(new Release { WorkId = work, Name = "Store title" });
        var owned = new OwnershipRepository(db.Factory);
        await owned.InsertAsync(new Ownership
        {
            ReleaseId = release, Store = "steam", AcquiredAt = new DateTime(2024, 2, 3, 0, 0, 0, DateTimeKind.Utc),
            LicenseType = "purchase", PricePaidCents = 1299, PriceSource = "steam_purchase_history",
        });
        await owned.InsertAsync(new Ownership { ReleaseId = release, Store = "gog", PricePaidCents = 0 });
        await owned.InsertAsync(new Ownership { ReleaseId = release, Store = "epic" });

        var result = await new AcquisitionExport(owned, new ReleaseRepository(db.Factory)).ReadAsync();

        Assert.Equal(3, result.OwnershipCount);
        Assert.StartsWith("schema_version,ownership_id,release_id,title,store,acquired_at,license_type,price_paid_cents,price_source,account_ref\r\n", result.Content);
        Assert.Contains("\"A, \"\"game\"\"\r\npart two\"", result.Content);
        Assert.Contains("\"steam\",\"2024-02-03T00:00:00.0000000Z\",\"purchase\",\"1299\",\"steam_purchase_history\",\r\n", result.Content);
        Assert.Contains("\"gog\",,,\"0\",,\r\n", result.Content);
        Assert.Contains("\"epic\",,,,,\r\n", result.Content);
    }

    [Theory]
    [InlineData(true, false, "Exported 0 ownership records.")]
    [InlineData(false, false, "Export cancelled.")]
    [InlineData(false, true, "Could not save the export. Try another location.")]
    public async Task Command_reports_saved_cancelled_and_failed_destinations(bool saved, bool fail, string expected)
    {
        using var db = new TempDatabase();
        var destination = new Destination(saved, fail);
        var vm = new LibrarySettingsViewModel(
            acquisitionExport: new AcquisitionExport(new OwnershipRepository(db.Factory), new ReleaseRepository(db.Factory)),
            exportDestination: destination);
        await vm.ExportAcquisitionsCommand.ExecuteAsync(null);

        Assert.Equal(expected, vm.AcquisitionExportStatus);
        Assert.StartsWith("schema_version,", destination.Content);
        Assert.Empty(await new OwnershipRepository(db.Factory).GetAllAsync());
    }

    [Fact]
    public void Missing_export_service_disables_the_command()
        => Assert.False(new LibrarySettingsViewModel().ExportAcquisitionsCommand.CanExecute(null));

    private sealed class Destination(bool saved, bool fail) : IAcquisitionExportDestination
    {
        public string Content { get; private set; } = "";
        public Task<bool> SaveAsync(string csv, CancellationToken ct = default)
        {
            Content = csv;
            if (fail) throw new IOException("Disk full");
            return Task.FromResult(saved);
        }
    }
}
