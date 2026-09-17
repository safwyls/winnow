using System.IO.Compression;
using System.Runtime.Loader;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Winnow.App;
using Winnow.App.Services;
using Winnow.Plugin.Xbox;
using Winnow.PluginSdk;
using Winnow.Plugins;
using Xunit;

namespace Winnow.Tests;

public sealed class XboxPluginPackageTests
{
    [Fact]
    public async Task Xbox_zip_loads_only_after_opt_in_with_the_shared_sdk_and_hides_managed_credentials()
    {
        using var db = new TempDatabase();
        var root = Path.Combine(Path.GetTempPath(), "winnow-xbox-package-" + Guid.NewGuid().ToString("N"));
        try
        {
            var user = Path.Combine(root, "plugins");
            Directory.CreateDirectory(user);
            using (var archive = ZipFile.Open(Path.Combine(user, "xbox.zip"), ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(typeof(XboxPlugin).Assembly.Location, "Winnow.Plugin.Xbox.dll");
                archive.CreateEntryFromFile(Path.Combine(AppContext.BaseDirectory, "xbox.plugin.json"), "plugin.json");
            }
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            Program.ConfigureServices(services, new(root, db.DatabasePath, DataMigrationOutcome.None));
            services.AddSingleton<Winnow.Data.ISqliteConnectionFactory>(db.Factory);
            await using var provider = services.BuildServiceProvider();
            var first = provider.GetRequiredService<PluginCatalog>();
            await first.DiscoverAsync(Path.Combine(root, "none"), user);
            Assert.False(Assert.Single(first.Plugins).Loaded);
            await first.SetEnabledAsync("xbox", true);
            Assert.False(first.Plugins[0].Loaded);
            await using var restarted = new PluginCatalog(provider.GetRequiredService<PluginStorage>(),
                provider.GetRequiredService<IPluginContextFactory>());
            await restarted.DiscoverAsync(Path.Combine(root, "none"), user);
            var descriptor = Assert.Single(restarted.GetActive<ILibrarySourcePlugin>());
            Assert.Null(descriptor.Error);
            var instance = await restarted.InvokeAsync<IPlugin>(descriptor, (plugin, _) => Task.FromResult<IPlugin?>(plugin));
            Assert.NotNull(instance);
            Assert.NotSame(AssemblyLoadContext.Default, AssemblyLoadContext.GetLoadContext(instance.GetType().Assembly));
            Assert.IsAssignableFrom<IPluginAccount>(instance);
            Assert.IsAssignableFrom<IPluginGameActions>(instance);
            Assert.DoesNotContain(typeof(Program).Assembly.GetReferencedAssemblies(), a => a.Name == "Winnow.Plugin.Xbox");
            Assert.Equal(["Winnow.PluginSdk"], typeof(XboxPlugin).Assembly.GetReferencedAssemblies()
                .Where(a => a.Name!.StartsWith("Winnow.", StringComparison.Ordinal)).Select(a => a.Name));
            var backend = new PluginSettingsBackend(restarted, provider.GetRequiredService<PluginStorage>(), user);
            var card = Assert.Single(await backend.LoadAsync());
            Assert.DoesNotContain(card.Settings, setting => setting.Key == "refresh-token");
            Assert.Contains(card.Settings, setting => setting.Key == "import-history");
            var applicationOverride = Assert.Single(card.Settings, setting => setting.Key == "client-id");
            Assert.True(applicationOverride.IsAdvanced);
            Assert.False(applicationOverride.IsRequired);
            Assert.True(string.IsNullOrEmpty(applicationOverride.Value));
            var unrelated = new PluginGame("unrelated", "Steam game", new Dictionary<string, string> { ["steam"] = "220" });
            Assert.Null(await ((IMetadataProviderPlugin)instance).GetMetadataAsync(unrelated));
            Assert.Empty((await ((IArtworkProviderPlugin)instance).GetArtworkAsync(unrelated))!);
        }
        finally
        {
            var absoluteRoot = Path.GetFullPath(root);
            if (!absoluteRoot.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Temporary path escaped its root.");
            try { Directory.Delete(absoluteRoot, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
