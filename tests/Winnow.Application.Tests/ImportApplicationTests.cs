using Microsoft.Extensions.DependencyInjection;
using Winnow.Api.Contracts.Details;
using Winnow.Application;
using Winnow.Application.Details;
using Winnow.Application.Library;
using Xunit;

namespace Winnow.Application.Tests;

public sealed class ImportApplicationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "winnow-import-tests", Guid.NewGuid().ToString("N"));
    private readonly ServiceProvider _services;
    private readonly ImportApplication _imports;
    public ImportApplicationTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IApplicationChangePublisher, Changes>();
        services.AddWinnowApplication(Path.Combine(_directory, "winnow.db"), pooling: false);
        services.AddWinnowImports();
        _services = services.BuildServiceProvider();
        _services.InitializeWinnowDatabase();
        _imports = _services.GetRequiredService<ImportApplication>();
    }

    [Fact]
    public async Task UploadedSavedPagesRetainUnknownAccountAndRepeatedImportDoesNotDuplicateFacts()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "steam-account-pages", "licenses-final-page.html");
        var loaded = await _imports.LoadPagesAsync(new([new("licenses.html", await File.ReadAllBytesAsync(path))]));
        Assert.True(loaded.AnythingLoaded);
        Assert.Equal("licenses.html", Assert.Single(loaded.Files).Path);
        Assert.Null(loaded.Pages.SteamId);
        var first = await _imports.ImportPagesAsync(loaded.Pages);
        Assert.True(first.LicenseFactsRecorded > 0);
        var second = await _imports.ImportPagesAsync(loaded.Pages);
        Assert.Equal(0, second.LicenseFactsRecorded);
        Assert.True(second.LicenseFactsAlreadyRecorded > 0);
    }

    [Fact]
    public async Task ExportKeepsQuotedTitlesAndUnknownPrices()
    {
        await _services.GetRequiredService<ILibraryApplication>().CreateManualGameAsync(new("A, \"game\""));
        var exported = await _imports.ExportAcquisitionsAsync();
        Assert.Equal(1, exported.OwnershipCount);
        Assert.Contains("\"A, \"\"game\"\"\"", exported.Content);
        Assert.Contains("\"manual\",,,,,", exported.Content);
    }

    public void Dispose()
    {
        _services.Dispose();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
    private sealed class Changes : IApplicationChangePublisher { public void Publish(string kind, string? resource = null) { } }
}
