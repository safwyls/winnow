using Winnow.PluginSdk;

namespace Winnow.Plugins;

/// <summary>Discovers manifests before loading trusted code. Activation changes take effect on the next launch.</summary>
public sealed class PluginCatalog(IPluginStateStore state, IPluginContextFactory contexts) : IAsyncDisposable
{
    private readonly List<PluginDescriptor> _plugins = [];
    private readonly List<PluginDiscoveryIssue> _issues = [];
    private readonly object _registry = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private bool _discovered;
    private volatile bool _disposed;

    public IReadOnlyList<PluginDescriptor> Plugins { get { lock (_registry) return _plugins.ToArray(); } }
    public IReadOnlyList<PluginDiscoveryIssue> Issues { get { lock (_registry) return _issues.ToArray(); } }
    public TimeSpan InvocationTimeout { get; init; } = TimeSpan.FromSeconds(120);
    public TimeSpan InitializationTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public void ReportInvalidResult(PluginDescriptor descriptor)
    {
        if (Plugins.Contains(descriptor)) descriptor.Error = "The plugin returned data that could not be applied.";
    }

    public async Task DiscoverAsync(string builtinRoot, string userRoot, CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_discovered) return;
            _discovered = true;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            await DiscoverRootAsync(builtinRoot, true, ids, cancellationToken).ConfigureAwait(false);
            await DiscoverRootAsync(userRoot, false, ids, cancellationToken).ConfigureAwait(false);
        }
        finally { _lifecycle.Release(); }
    }

    private async Task DiscoverRootAsync(string root, bool builtin, HashSet<string> ids, CancellationToken cancellationToken)
    {
        string[] directories;
        try
        {
            if (!builtin) Directory.CreateDirectory(root);
            else if (!Directory.Exists(root)) return;
            directories = Directory.GetDirectories(root).Order(StringComparer.Ordinal).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddIssue(root, "The plugin directory could not be created or read.");
            return;
        }
        foreach (var directory in directories)
        {
            var name = Path.GetFileName(directory);
            if (!builtin && (name.Equals(".archives", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith(".unpack-", StringComparison.OrdinalIgnoreCase))) continue;
            await DiscoverDirectoryAsync(directory, builtin, ids, cancellationToken).ConfigureAwait(false);
        }
        if (!builtin) await InstallArchivesAsync(root, ids, cancellationToken).ConfigureAwait(false);
    }

    private async Task InstallArchivesAsync(string root, HashSet<string> ids, CancellationToken cancellationToken)
    {
        string[] archives;
        try
        {
            archives = Directory.GetFiles(root).Where(path => Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddIssue(root, "Plugin ZIP files could not be read. Check that the plugins folder is available, then restart Winnow.");
            return;
        }
        foreach (var archive in archives)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = await PluginArchiveInstaller.TryInstallAsync(archive, root, ids, AddIssue, cancellationToken).ConfigureAwait(false);
            if (directory is not null)
                await DiscoverDirectoryAsync(directory, false, ids, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task DiscoverDirectoryAsync(string directory, bool builtin, HashSet<string> ids, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var manifestPath = Path.Combine(directory, "plugin.json");
        if (!File.Exists(manifestPath)) return;
        PluginManifest manifest;
        try { manifest = await PluginManifestReader.ReadAsync(manifestPath, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (InvalidDataException ex)
        {
            AddIssue(directory, ex.Message);
            return;
        }
        catch (Exception)
        {
            AddIssue(directory, "The plugin manifest could not be read.");
            return;
        }
        if (!ids.Add(manifest.Id))
        {
            AddIssue(directory, "Another installed plugin already uses this ID.");
            return;
        }
        var descriptor = new PluginDescriptor
        {
            Manifest = manifest, DirectoryPath = Path.GetFullPath(directory), BuiltIn = builtin,
        };
        lock (_registry) _plugins.Add(descriptor);
        try { descriptor.Enabled = await state.GetEnabledAsync(manifest.Id, cancellationToken).ConfigureAwait(false) ?? builtin; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            descriptor.Error = "The plugin activation setting could not be read.";
            return;
        }
        if (descriptor.Enabled) await LoadAsync(descriptor, cancellationToken).ConfigureAwait(false);
    }

    private void AddIssue(string directory, string message)
    {
        lock (_registry) _issues.Add(new(directory, message));
    }

    private async Task LoadAsync(PluginDescriptor descriptor, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(InitializationTimeout);
        Task? initialization = null;
        try
        {
            // Constructors and synchronous plugin code must not occupy the UI/startup thread.
            initialization = Task.Run(async () =>
            {
                var path = Path.Combine(descriptor.DirectoryPath, descriptor.Manifest.EntryAssembly);
                var loader = new PluginLoadContext(path);
                descriptor.LoadContext = loader;
                var assembly = loader.LoadFromAssemblyPath(path);
                var type = assembly.GetType(descriptor.Manifest.EntryType, throwOnError: true)!;
                if (type.IsAbstract || !type.IsPublic || !typeof(IPlugin).IsAssignableFrom(type))
                    throw new InvalidDataException("The plugin entry type does not implement the plugin contract.");
                var plugin = Activator.CreateInstance(type) as IPlugin
                    ?? throw new InvalidDataException("The plugin could not be constructed.");
                descriptor.Instance = plugin;
                ValidateCapabilities(descriptor.Manifest, plugin);
                await plugin.InitializeAsync(contexts.Create(descriptor.Manifest), deadline.Token).ConfigureAwait(false);
            }, deadline.Token);
            await initialization.WaitAsync(deadline.Token).ConfigureAwait(false);
            descriptor.Loaded = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ObserveLateFailure(initialization);
            throw;
        }
        catch (OperationCanceledException)
        {
            ObserveLateFailure(initialization);
            descriptor.Error = "The plugin did not finish starting in time. Restart Winnow to try again.";
        }
        catch (Exception)
        {
            descriptor.Error = "The plugin could not start. Check its package and API compatibility.";
        }
    }

    public async Task SetEnabledAsync(string pluginId, bool enabled, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var descriptor = Plugins.SingleOrDefault(x => x.Manifest.Id == pluginId)
            ?? throw new KeyNotFoundException("The plugin is not installed.");
        await state.SetEnabledAsync(pluginId, enabled, cancellationToken).ConfigureAwait(false);
        descriptor.Enabled = enabled;
    }

    public IReadOnlyList<PluginDescriptor> GetActive<TPlugin>() where TPlugin : class, IPlugin =>
        Plugins.Where(x => x.Loaded && x.Instance is TPlugin && Declares<TPlugin>(x.Manifest)).ToArray();

    private static bool Declares<TPlugin>(PluginManifest manifest) => typeof(TPlugin) == typeof(IPlugin)
        || (typeof(TPlugin) == typeof(ILibrarySourcePlugin) && manifest.Capabilities.Contains(PluginCapabilities.Library))
        || (typeof(TPlugin) == typeof(IMetadataProviderPlugin) && manifest.Capabilities.Contains(PluginCapabilities.Metadata))
        || (typeof(TPlugin) == typeof(IArtworkProviderPlugin) && manifest.Capabilities.Contains(PluginCapabilities.Artwork))
        || (typeof(TPlugin) == typeof(IRecommendationFeedPlugin) && manifest.Capabilities.Contains(PluginCapabilities.Recommendations));

    /// <summary>Provider exceptions never expose their text, which might contain credentials or response data.</summary>
    public async Task<T?> InvokeAsync<T>(PluginDescriptor descriptor,
        Func<IPlugin, CancellationToken, Task<T?>> operation, CancellationToken cancellationToken = default) where T : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Plugins.Contains(descriptor) || !descriptor.Loaded || descriptor.Instance is null) return null;
        await descriptor.InvocationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!descriptor.Loaded || descriptor.Instance is null) return null;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(InvocationTimeout);
            var token = deadline.Token;
            var plugin = descriptor.Instance;
            var pending = Task.Run(() => operation(plugin, token), token);
            try
            {
                var result = await pending.WaitAsync(token).ConfigureAwait(false);
                descriptor.Error = null;
                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                ObserveLateFailure(pending);
                // A noncooperative provider may still be running after cancellation.
                if (!pending.IsCompleted) descriptor.Loaded = false;
                throw;
            }
            catch (OperationCanceledException)
            {
                ObserveLateFailure(pending);
                descriptor.Loaded = false;
                descriptor.Error = "The plugin did not respond in time and was disabled for this session.";
                return null;
            }
            catch (Exception)
            {
                descriptor.Error = "The plugin could not complete this operation.";
                return null;
            }
        }
        finally { descriptor.InvocationGate.Release(); }
    }

    private static void ValidateCapabilities(PluginManifest manifest, IPlugin plugin)
    {
        foreach (var capability in manifest.Capabilities)
        {
            var implemented = capability switch
            {
                PluginCapabilities.Library => plugin is ILibrarySourcePlugin,
                PluginCapabilities.Metadata => plugin is IMetadataProviderPlugin,
                PluginCapabilities.Artwork => plugin is IArtworkProviderPlugin,
                PluginCapabilities.Recommendations => plugin is IRecommendationFeedPlugin,
                _ => false,
            };
            if (!implemented) throw new InvalidDataException("A declared plugin capability is not implemented.");
        }
    }

    private static void ObserveLateFailure(Task? task)
    {
        if (task is not null)
            _ = task.ContinueWith(completed => _ = completed.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var descriptor in Plugins)
        {
            descriptor.Loaded = false;
            try
            {
                if (descriptor.Instance is IAsyncDisposable asyncDisposable)
                    await Task.Run(async () => await asyncDisposable.DisposeAsync().ConfigureAwait(false))
                        .WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                else if (descriptor.Instance is IDisposable disposable)
                    await Task.Run(disposable.Dispose).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch (Exception) { /* Plugin cleanup must not prevent host shutdown. */ }
            descriptor.Instance = null;
            descriptor.LoadContext?.Unload();
            descriptor.LoadContext = null;
        }
    }
}
