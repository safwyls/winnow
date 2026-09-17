using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Winnow.App.Services;
using Winnow.PluginFixture;
using Winnow.Plugins;
using Winnow.PluginSdk;
using Xunit;

namespace Winnow.Tests;

public sealed class OfficialPluginInstallerTests
{
    [Theory]
    [InlineData("psn", "v1.2.3")]
    [InlineData("xbox", "v0.2.0-beta.12")]
    [InlineData("steamgriddb", "v10.20.30-rc-1")]
    public void Browser_handoff_accepts_only_named_official_plugins_and_explicit_release(string id, string release)
    {
        Assert.True(PluginInstallRequest.TryParseUri($"winnow://plugins/install?id={id}&release={release}", out var request));
        Assert.Equal(new(id, release), request);
        Assert.True(PluginInstallRequest.TryParseUri($"winnow://plugins/install?release={release}&id={id}", out request));
        Assert.Equal(new(id, release), request);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://plugins/install?id=psn&release=v1.2.3")]
    [InlineData("winnow://plugins/install?id=untrusted&release=v1.2.3")]
    [InlineData("winnow://plugins/install?id=PSN&release=v1.2.3")]
    [InlineData("winnow://plugins/install?id=psn&id=xbox")]
    [InlineData("winnow://plugins/install?id=psn&release=v1.2.3&url=https://example.com/p.zip")]
    [InlineData("winnow://plugins/install?id=psn&release=v1.2.3#fragment")]
    [InlineData("winnow://plugins/install?id=psn&release=latest")]
    [InlineData("winnow://plugins/install?id=psn&release=v1.2")]
    [InlineData("winnow://plugins/install?id=psn&release=v01.2.3")]
    [InlineData("winnow://plugins/install?id=psn&release=v1.2.3/../private")]
    [InlineData("winnow://plugins/install?id=psn&release=v1.2.3%2Fprivate")]
    [InlineData("winnow://plugins/install?id=%70sn&release=v1.2.3")]
    [InlineData("winnow://plugins/install?id=psn&release=v1.2.3\n")]
    [InlineData("winnow://plugins:443/install?id=psn&release=v1.2.3")]
    [InlineData("winnow://user@plugins/install?id=psn&release=v1.2.3")]
    [InlineData("winnow://plugins/../install?id=psn&release=v1.2.3")]
    public void Malformed_handoffs_are_rejected(string? value)
    {
        Assert.False(PluginInstallRequest.TryParseUri(value, out var request));
        Assert.Null(request);
    }

    [Fact]
    public async Task Verified_install_enables_and_loads_a_new_plugin_without_restart()
    {
        await using var host = new Host();
        await host.DiscoverAsync();
        var stages = new List<string>();

        var result = await host.Installer.InstallAsync(Host.Request, new ProgressRecorder(stages));

        Assert.Equal(PluginInstallOutcome.Installed, result.Outcome);
        var plugin = Assert.Single(host.Catalog.Plugins);
        Assert.True(plugin.Enabled);
        Assert.True(plugin.Loaded);
        Assert.False(plugin.RestartRequired);
        Assert.True(host.State.Values["psn"]);
        Assert.Equal("True", host.Context.Values["loaded-in-private-context"]);
        Assert.Single(host.Catalog.GetActive<ILibrarySourcePlugin>());
        var library = await host.Catalog.InvokeAsync<IReadOnlyList<PluginLibraryGame>>(plugin,
            (instance, ct) => ((ILibrarySourcePlugin)instance).GetLibraryAsync(ct));
        Assert.Equal("fixture-game", Assert.Single(library!).SourceId);
        Assert.Equal(3, host.Http.Requests.Count);
        Assert.Equal(4, stages.Count);
        Assert.Single(Directory.GetFiles(Path.Combine(host.User, ".archives"), "*.zip"));
        host.AssertNoPartialFiles();
    }

    [Fact]
    public async Task Official_Xbox_assembly_and_manifest_become_available_without_a_restart_or_account_network_call()
    {
        await using var host = new Host(officialXbox: true);
        await host.DiscoverAsync();

        var result = await host.Installer.InstallAsync(new("xbox", Host.Request.ReleaseTag));

        Assert.Equal(PluginInstallOutcome.Installed, result.Outcome);
        var plugin = Assert.Single(host.Catalog.GetActive<IPluginAccount>());
        Assert.Equal("xbox", plugin.Manifest.Id);
        Assert.True(plugin.Loaded);
        Assert.False(plugin.RestartRequired);
        var account = await host.Catalog.InvokeAsync<PluginAccountStatus>(plugin,
            async (instance, ct) => await ((IPluginAccount)instance).GetAccountStatusAsync(ct));
        Assert.NotNull(account);
        Assert.False(account.Connected);
        Assert.Equal(3, host.Http.Requests.Count);
    }

    [Fact]
    public async Task Concurrent_repeated_requests_install_once_without_replacing_loaded_code()
    {
        await using var host = new Host();
        await host.DiscoverAsync();
        var results = await Task.WhenAll(host.Installer.InstallAsync(Host.Request), host.Installer.InstallAsync(Host.Request));
        Assert.Single(results, result => result.Outcome == PluginInstallOutcome.Installed);
        Assert.Single(results, result => result.Outcome == PluginInstallOutcome.AlreadyInstalled);
        Assert.Single(host.Catalog.Plugins);
        Assert.Equal(3, host.Http.Requests.Count);
    }

    [Fact]
    public async Task Existing_builtin_and_disabled_user_plugins_keep_their_version_files_and_state()
    {
        await using var host = new Host();
        var directory = Path.Combine(host.Builtin, "steamgriddb");
        Directory.CreateDirectory(directory);
        var manifest = Host.Manifest("steamgriddb") with { Version = "0.1.0" };
        await File.WriteAllTextAsync(Path.Combine(directory, "plugin.json"), JsonSerializer.Serialize(manifest));
        host.State.Values["steamgriddb"] = false;
        await host.DiscoverAsync();

        var result = await host.Installer.InstallAsync(new("steamgriddb", "v1.2.3"));

        Assert.Equal(PluginInstallOutcome.AlreadyInstalled, result.Outcome);
        var plugin = Assert.Single(host.Catalog.Plugins);
        Assert.True(plugin.BuiltIn);
        Assert.False(plugin.Enabled);
        Assert.False(host.State.Values["steamgriddb"]);
        Assert.Equal("0.1.0", plugin.Manifest.Version);
        Assert.Empty(host.Http.Requests);
        Assert.False(Directory.Exists(Path.Combine(host.User, "steamgriddb")));
    }

    [Fact]
    public async Task Installation_waits_for_discovery_before_deciding_whether_to_download()
    {
        await using var host = new Host();
        var pending = host.Installer.InstallAsync(Host.Request);
        Assert.False(pending.IsCompleted);
        Assert.Empty(host.Http.Requests);
        await host.DiscoverAsync();
        Assert.Equal(PluginInstallOutcome.Installed, (await pending).Outcome);
    }

    [Fact]
    public async Task Cancellation_during_discovery_can_be_retried_and_does_not_download()
    {
        await using var host = new Host();
        using var cancel = new CancellationTokenSource();
        var pending = host.Installer.InstallAsync(Host.Request, ct: cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Empty(host.Http.Requests);
        await host.DiscoverAsync();
        Assert.Equal(PluginInstallOutcome.Installed, (await host.Installer.InstallAsync(Host.Request)).Outcome);
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("wrong-tag")]
    [InlineData("asset-url")]
    [InlineData("missing-digest")]
    [InlineData("duplicate-asset")]
    [InlineData("catalogue-hash")]
    [InlineData("catalogue-size")]
    [InlineData("catalogue-tag")]
    [InlineData("catalogue-schema")]
    [InlineData("catalogue-duplicate")]
    [InlineData("package-hash")]
    [InlineData("package-size")]
    [InlineData("package-name")]
    [InlineData("package-api")]
    [InlineData("package-sdk")]
    [InlineData("manifest-id")]
    [InlineData("manifest-version")]
    [InlineData("manifest-api")]
    [InlineData("invalid-zip")]
    [InlineData("unsafe-zip")]
    public async Task Mismatched_or_unsafe_packages_are_never_published(string failure)
    {
        await using var host = new Host(failure);
        await host.DiscoverAsync();

        var result = await host.Installer.InstallAsync(Host.Request);

        Assert.Equal(PluginInstallOutcome.Failed, result.Outcome);
        Assert.Empty(host.Catalog.Plugins);
        Assert.False(Directory.Exists(Path.Combine(host.User, "psn")));
        Assert.False(Directory.Exists(Path.Combine(host.User, "xbox")));
        Assert.Empty(host.State.Values);
        host.AssertNoPartialFiles();
    }

    [Theory]
    [InlineData("https://release-assets.githubusercontent.com/fake-asset?signature=test", true)]
    [InlineData("https://objects.githubusercontent.com/fake-asset", true)]
    [InlineData("https://untrusted.example/fake-asset", false)]
    [InlineData("http://release-assets.githubusercontent.com/fake-asset", false)]
    [InlineData("https://release-assets.githubusercontent.com:8443/fake-asset", false)]
    [InlineData("https://user@release-assets.githubusercontent.com/fake-asset", false)]
    [InlineData("https://github.com/untrusted/repo/releases/download/v1/evil.zip", false)]
    public async Task Downloads_follow_only_explicit_GitHub_release_storage_redirects(string redirect, bool permitted)
    {
        await using var host = new Host();
        host.Http.Redirect = redirect;
        await host.DiscoverAsync();
        var result = await host.Installer.InstallAsync(Host.Request);
        Assert.Equal(permitted ? PluginInstallOutcome.Installed : PluginInstallOutcome.Failed, result.Outcome);
        Assert.Equal(permitted, host.Http.Requests.Any(uri => uri.ToString() == redirect));
        host.AssertNoPartialFiles();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Download_length_and_hash_are_checked_even_without_content_length(bool extraBytes)
    {
        await using var host = new Host();
        host.Http.UnknownLength = true;
        host.Http.CorruptDownload = extraBytes ? [.. host.Http.ZipBytes, 0] : [.. host.Http.ZipBytes[..^1], (byte)(host.Http.ZipBytes[^1] ^ 255)];
        await host.DiscoverAsync();
        Assert.Equal(PluginInstallOutcome.Failed, (await host.Installer.InstallAsync(Host.Request)).Outcome);
        Assert.Empty(host.Catalog.Plugins);
        host.AssertNoPartialFiles();
    }

    [Fact]
    public async Task Cancellation_during_transfer_cleans_partial_download_and_allows_retry()
    {
        await using var host = new Host();
        await host.DiscoverAsync();
        host.Http.BlockPackage = true;
        using var cancel = new CancellationTokenSource();
        var pending = host.Installer.InstallAsync(Host.Request, ct: cancel.Token);
        await host.Http.PackageRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Empty(host.Catalog.Plugins);
        host.AssertNoPartialFiles();
        host.Http.BlockPackage = false;
        Assert.Equal(PluginInstallOutcome.Installed, (await host.Installer.InstallAsync(Host.Request)).Outcome);
    }

    [Fact]
    public async Task Application_shutdown_cancels_a_transfer_even_without_a_caller_token_and_drains_it()
    {
        using var stopping = new CancellationTokenSource();
        await using var host = new Host(applicationStopping: stopping.Token);
        await host.DiscoverAsync();
        host.Http.BlockPackage = true;
        var pending = host.Installer.InstallAsync(Host.Request);
        await host.Http.PackageRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));

        stopping.Cancel();
        await host.Installer.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Empty(host.Catalog.Plugins);
        host.AssertNoPartialFiles();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.Installer.InstallAsync(Host.Request));
    }

    [Fact]
    public async Task Disposing_during_archive_validation_waits_for_cleanup_and_prevents_publication()
    {
        await using var host = new Host();
        await host.DiscoverAsync();
        var archive = Path.Combine(host.User, "verified.zip");
        await File.WriteAllBytesAsync(archive, host.Http.ZipBytes);
        var validating = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishValidation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = Task.Run(() => host.Catalog.InstallVerifiedArchiveAsync(archive, host.User, _ =>
        {
            validating.TrySetResult();
            finishValidation.Task.GetAwaiter().GetResult();
        }));
        await validating.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var disposal = host.Catalog.DisposeAsync().AsTask();
        Assert.False(disposal.IsCompleted);
        finishValidation.TrySetResult();
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Empty(host.Catalog.Plugins);
        Assert.False(Directory.Exists(Path.Combine(host.User, "psn")));
        Assert.Empty(host.State.Values);
        host.AssertNoPartialFiles();
    }

    [Fact]
    public async Task Shutdown_after_publication_cancels_registration_before_loading_and_retains_the_package()
    {
        await using var host = new Host();
        await host.DiscoverAsync();
        host.State.BlockWrites = true;
        var pending = host.Installer.InstallAsync(Host.Request);
        await host.State.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(Directory.Exists(Path.Combine(host.User, "psn")));

        await host.Installer.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await host.Catalog.DisposeAsync();

        Assert.False(Assert.Single(host.Catalog.Plugins).Loaded);
        Assert.Empty(host.Context.Values);
        Assert.Empty(host.State.Values);
        Assert.True(File.Exists(Path.Combine(host.User, "psn", "plugin.json")));
        host.AssertNoPartialFiles();
    }

    [Fact]
    public async Task An_initializer_that_finishes_after_disposal_cannot_become_active()
    {
        await using var host = new Host();
        await host.DiscoverAsync();
        host.Context.BlockInitialization = true;
        var pending = host.Installer.InstallAsync(Host.Request);
        await host.Context.InitializationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await host.Installer.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await host.Catalog.DisposeAsync();
        host.Context.FinishInitialization.TrySetResult();
        await host.Context.InitializationReturned.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(Assert.Single(host.Catalog.Plugins).Loaded);
        Assert.Empty(host.Catalog.GetActive<ILibrarySourcePlugin>());
        host.AssertNoPartialFiles();
    }

    [Fact]
    public async Task Provider_startup_failure_is_reported_without_exposing_exception_text()
    {
        await using var host = new Host("plugin-startup");
        await host.DiscoverAsync();
        var result = await host.Installer.InstallAsync(Host.Request);
        Assert.Equal(PluginInstallOutcome.Installed, result.Outcome);
        Assert.Contains("could not start", result.Message);
        Assert.DoesNotContain("private-api-key", result.Message);
        Assert.False(Assert.Single(host.Catalog.Plugins).Loaded);
    }

    private sealed class ProgressRecorder(List<string> messages) : IProgress<PluginInstallProgress>
    {
        public void Report(PluginInstallProgress value) => messages.Add(value.Message);
    }

    private sealed class Host : IAsyncDisposable
    {
        public static readonly PluginInstallRequest Request = new("psn", "v1.2.3-beta.1");
        private readonly string _root = Path.Combine(Path.GetTempPath(), "winnow-official-plugin-tests-" + Guid.NewGuid().ToString("N"));
        private readonly HttpClient _client;
        public string User => Path.Combine(_root, "user");
        public string Builtin => Path.Combine(_root, "builtin");
        public StateStore State { get; } = new();
        public ContextFactory Context { get; } = new();
        public PluginCatalog Catalog { get; }
        public ReleaseHandler Http { get; }
        public OfficialPluginInstaller Installer { get; }
        public Host(string failure = "", bool officialXbox = false, CancellationToken applicationStopping = default)
        {
            Catalog = new(State, Context);
            Http = new(failure, officialXbox);
            _client = new(Http);
            Installer = new(_client, Catalog, new Backend(User), applicationStopping: applicationStopping);
        }
        public Task DiscoverAsync() => Catalog.DiscoverAsync(Builtin, User);
        public void AssertNoPartialFiles()
        {
            Assert.Empty(Directory.GetFiles(User, "*.partial"));
            Assert.Empty(Directory.GetDirectories(User, ".unpack-*"));
        }
        public static PluginManifest Manifest(string id = "psn") => new()
        {
            Id = id, Name = "Fixture official package", Version = "1.0.0", EntryAssembly = "Winnow.PluginFixture.dll",
            EntryType = typeof(FixturePlugin).FullName!, Capabilities = [PluginCapabilities.Library],
            Settings = [new() { Key = "loaded-in-private-context", Label = "Fixture context" }],
        };
        public async ValueTask DisposeAsync()
        {
            await Installer.StopAsync();
            await Catalog.DisposeAsync();
            Installer.Dispose();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private sealed class ReleaseHandler : HttpMessageHandler
    {
        private const string CatalogueName = "winnow-plugins.json";
        private readonly string _assetName;
        private static string Url(string name) => $"https://github.com/safwyls/winnow/releases/download/{Host.Request.ReleaseTag}/{name}";
        private readonly byte[] _release;
        private readonly byte[] _catalogue;
        public byte[] ZipBytes { get; }
        public List<Uri> Requests { get; } = [];
        public string? Redirect { get; set; }
        public bool UnknownLength { get; set; }
        public byte[]? CorruptDownload { get; set; }
        public bool BlockPackage { get; set; }
        public TaskCompletionSource PackageRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ReleaseHandler(string failure, bool officialXbox)
        {
            _assetName = officialXbox ? "Winnow.Plugin.Xbox-1.0.0.zip" : "Winnow.Plugin.Psn-1.0.0.zip";
            var manifest = officialXbox ? JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "xbox.plugin.json")), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })! : Host.Manifest();
            if (failure == "manifest-id") manifest = manifest with { Id = "xbox" };
            if (failure == "manifest-version") manifest = manifest with { Version = "2.0.0" };
            if (failure == "manifest-api") manifest = manifest with { ApiVersion = 99 };
            if (failure == "plugin-startup") manifest = manifest with { EntryType = typeof(ThrowingPlugin).FullName!, Capabilities = [PluginCapabilities.Artwork] };
            using (var output = new MemoryStream())
            {
                using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
                {
                    using (var entry = zip.CreateEntry("plugin.json").Open()) JsonSerializer.Serialize(entry, manifest);
                    using (var entry = zip.CreateEntry(manifest.EntryAssembly).Open())
                    using (var input = File.OpenRead(officialXbox ? typeof(Plugin.Xbox.XboxPlugin).Assembly.Location : typeof(FixturePlugin).Assembly.Location)) input.CopyTo(entry);
                    if (failure == "unsafe-zip") zip.CreateEntry("../outside.txt");
                }
                ZipBytes = failure == "invalid-zip" ? "not a zip"u8.ToArray() : output.ToArray();
            }
            var plugin = new JsonObject
            {
                ["id"] = officialXbox ? "xbox" : "psn", ["name"] = officialXbox ? "Xbox" : "PlayStation", ["version"] = "1.0.0", ["apiVersion"] = 1,
                ["minimumSdkVersion"] = "1.1.0", ["assetName"] = _assetName, ["size"] = ZipBytes.Length, ["sha256"] = Hash(ZipBytes),
            };
            if (failure == "package-hash") plugin["sha256"] = new string('0', 64);
            if (failure == "package-size") plugin["size"] = ZipBytes.Length + 1;
            if (failure == "package-name") plugin["assetName"] = "some-other-plugin.zip";
            if (failure == "package-api") plugin["apiVersion"] = 2;
            if (failure == "package-sdk") plugin["minimumSdkVersion"] = "99.0.0";
            var plugins = new JsonArray(plugin);
            if (failure == "catalogue-duplicate") plugins.Add(plugin.DeepClone());
            var catalogue = new JsonObject
            {
                ["schemaVersion"] = failure == "catalogue-schema" ? 2 : 1,
                ["releaseTag"] = failure == "catalogue-tag" ? "v9.9.9" : Host.Request.ReleaseTag,
                ["appVersion"] = Host.Request.ReleaseTag[1..], ["plugins"] = plugins,
            };
            _catalogue = Encoding.UTF8.GetBytes(catalogue.ToJsonString());
            var catalogueAsset = Asset(CatalogueName, _catalogue);
            if (failure == "catalogue-hash") catalogueAsset["digest"] = "sha256:" + new string('0', 64);
            if (failure == "catalogue-size") catalogueAsset["size"] = _catalogue.Length + 1;
            if (failure == "asset-url") catalogueAsset["browser_download_url"] = "https://evil.example/catalogue.json";
            if (failure == "missing-digest") catalogueAsset.Remove("digest");
            var assets = new JsonArray(catalogueAsset, Asset(_assetName, ZipBytes));
            if (failure == "duplicate-asset") assets.Add(catalogueAsset.DeepClone());
            _release = Encoding.UTF8.GetBytes(new JsonObject
            {
                ["tag_name"] = failure == "wrong-tag" ? "v9.9.9" : Host.Request.ReleaseTag,
                ["draft"] = failure == "draft", ["prerelease"] = true, ["assets"] = assets,
            }.ToJsonString());
        }
        private static JsonObject Asset(string name, byte[] content) => new()
        {
            ["name"] = name, ["browser_download_url"] = Url(name), ["size"] = content.Length,
            ["state"] = "uploaded", ["digest"] = "sha256:" + Hash(content),
        };
        private static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            Requests.Add(uri);
            byte[] bytes;
            if (uri.Host == "api.github.com")
            {
                Assert.Equal($"https://api.github.com/repos/safwyls/winnow/releases/tags/{Host.Request.ReleaseTag}", uri.ToString());
                bytes = _release;
            }
            else if (uri.ToString() == Url(CatalogueName)) bytes = _catalogue;
            else if (uri.ToString() == Url(_assetName))
            {
                PackageRequested.TrySetResult();
                if (BlockPackage) await Task.Delay(Timeout.Infinite, ct);
                if (Redirect is not null) return new(HttpStatusCode.Found) { Headers = { Location = new Uri(Redirect) } };
                bytes = CorruptDownload ?? ZipBytes;
            }
            else if (uri.ToString() == Redirect) bytes = ZipBytes;
            else throw new InvalidOperationException("Unexpected test request.");
            return new(HttpStatusCode.OK)
            {
                Content = UnknownLength ? new UnknownLengthContent(bytes) : new ByteArrayContent(bytes),
            };
        }
    }

    private sealed class UnknownLengthContent(byte[] content) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(content).AsTask();
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }

    private sealed class StateStore : IPluginStateStore
    {
        public Dictionary<string, bool> Values { get; } = [];
        public bool BlockWrites { get; set; }
        public TaskCompletionSource WriteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<bool?> GetEnabledAsync(string pluginId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Values.TryGetValue(pluginId, out var enabled) ? (bool?)enabled : null);
        public async ValueTask SetEnabledAsync(string pluginId, bool enabled, CancellationToken cancellationToken = default)
        {
            WriteStarted.TrySetResult();
            if (BlockWrites) await Task.Delay(Timeout.Infinite, cancellationToken);
            Values[pluginId] = enabled;
        }
    }

    private sealed class ContextFactory : IPluginContextFactory, IPluginContext, IPluginSettings, IPluginSecrets, IPluginCache, IPluginHttp
    {
        public Dictionary<string, string?> Values { get; } = [];
        public bool BlockInitialization { get; set; }
        public TaskCompletionSource InitializationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FinishInitialization { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource InitializationReturned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string PluginId { get; private set; } = "";
        public IPluginSettings Settings => this;
        public IPluginSecrets Secrets => this;
        public IPluginCache Cache => this;
        public IPluginHttp Http => this;
        public IPluginContext Create(PluginManifest manifest) { PluginId = manifest.Id; return this; }
        public ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default) => ValueTask.FromResult(Values.GetValueOrDefault(key));
        public async ValueTask SetAsync(string key, string? value, CancellationToken cancellationToken = default)
        {
            InitializationStarted.TrySetResult();
            if (BlockInitialization) await FinishInitialization.Task; // Deliberately noncooperative fixture initialization.
            Values[key] = value;
            InitializationReturned.TrySetResult();
        }
        ValueTask<PluginCacheEntry?> IPluginCache.GetAsync(string key, CancellationToken cancellationToken) => ValueTask.FromResult<PluginCacheEntry?>(null);
        public ValueTask SetAsync(string key, PluginCacheEntry entry, CancellationToken cancellationToken = default) => default;
        public Task<PluginHttpResponse> SendAsync(PluginHttpRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Backend(string userRoot) : IPluginSettingsBackend
    {
        public string UserPluginDirectory => userRoot;
        public Task<IReadOnlyList<PluginSettingsSnapshot>> LoadAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PluginSettingsSnapshot>>([]);
        public Task SaveAsync(string pluginId, IReadOnlyDictionary<string, string> values, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RemoveSecretAsync(string pluginId, string key, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SetEnabledAsync(string pluginId, bool enabled, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RefreshAsync(string pluginId, CancellationToken ct = default) => Task.CompletedTask;
    }
}
