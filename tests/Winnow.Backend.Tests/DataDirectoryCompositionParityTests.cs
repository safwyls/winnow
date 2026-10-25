using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Winnow.Api.Client;
using Winnow.App.Services;
using Winnow.Core.Repositories;
using Winnow.Covers;
using Winnow.Data;
using Xunit;

namespace Winnow.Backend.Tests;

public sealed class DataDirectoryCompositionParityTests
{
    [Fact]
    public async Task The_backend_composition_and_authenticated_folder_route_share_the_selected_library_root()
    {
        var sandbox = Path.Combine(Path.GetTempPath(), "winnow-directory-composition", Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(sandbox, "selected library");
        Directory.CreateDirectory(directory);
        var app = BackendApplication.Build(["--data-dir", directory, "--no-sync"]);
        try
        {
            Assert.NotNull(app.Services.GetRequiredService<LifecycleSyncService>());
            Assert.NotNull(app.Services.GetRequiredService<ILifecycleRepository>());
            var database = app.Services.GetRequiredService<ISqliteConnectionFactory>();
            Assert.Equal(Path.Combine(directory, "winnow.db"), database.DatabasePath);
            Assert.Equal(Path.Combine(directory, "covers"),
                app.Services.GetRequiredService<CoverCacheOptions>().CacheDirectory);
            Assert.Equal(directory, app.Services.GetRequiredService<DataLocation>().Root);

            await app.StartAsync();
            var connection = await BackendConnection.ReadAsync(directory);
            using var client = new HttpClient { BaseAddress = new(connection.Address) };
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await client.GetAsync("api/v1/connections/plugins/directory")).StatusCode);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", connection.Token);
            using var folders = JsonDocument.Parse(await client.GetStringAsync("api/v1/connections/plugins/directory"));
            var plugins = folders.RootElement.GetProperty("directory").GetString();
            Assert.Equal(Path.Combine(directory, "plugins"), plugins);
            // Electron discovers the authored theme directory through this authenticated root.
            Assert.Equal(Path.Combine(directory, "themes"), Path.Combine(Path.GetDirectoryName(plugins!)!, "themes"));

            using (var lease = database.Open())
            {
                using var command = lease.CreateCommand();
                command.CommandText = "CREATE TABLE directory_composition_probe (id INTEGER PRIMARY KEY)";
                command.ExecuteNonQuery();
                Assert.True(File.Exists(database.DatabasePath));
                Assert.True(File.Exists(database.DatabasePath + "-wal"));
            }
            Assert.All(Directory.EnumerateFiles(sandbox, "*", SearchOption.AllDirectories), path =>
                Assert.StartsWith(directory + Path.DirectorySeparatorChar, path, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
            Directory.Delete(sandbox, recursive: true);
        }
    }
}
