using Winnow.App.Services;
using Winnow.App.ViewModels;
using Xunit;

namespace Winnow.Tests;

public sealed class PluginSettingsViewModelTests
{
    [Fact]
    public async Task Secrets_are_never_loaded_and_blank_drafts_preserve_saved_values()
    {
        var backend = new Backend();
        var settings = new PluginSettingsViewModel(backend);
        await settings.LoadAsync();
        var plugin = Assert.Single(settings.Plugins);
        var key = plugin.Fields[0];
        Assert.Empty(key.Value);
        Assert.True(key.HasStoredSecret);
        plugin.Fields[1].Value = "en";
        await plugin.SaveCommand.ExecuteAsync(null);
        Assert.False(backend.Saved!.ContainsKey("api-key"));
        Assert.Equal("en", backend.Saved["language"]);
        key.Value = "replacement-secret";
        await plugin.SaveCommand.ExecuteAsync(null);
        Assert.Equal("replacement-secret", backend.Saved["api-key"]);
        Assert.Empty(key.Value);
        Assert.Contains("Refresh queued", plugin.Status);
        await key.RemoveSecretCommand.ExecuteAsync(null);
        Assert.Equal("api-key", backend.Removed);
        Assert.False(key.HasStoredSecret);
        Assert.False(key.RemoveSecretCommand.CanExecute(null));
        Assert.DoesNotContain("replacement-secret", plugin.Status);
    }

    [Fact]
    public async Task Enablement_waits_for_restart_and_refresh_requires_an_enabled_loaded_plugin()
    {
        var backend = new Backend { Enabled = false, Loaded = false };
        var settings = new PluginSettingsViewModel(backend);
        await settings.LoadAsync();
        var plugin = Assert.Single(settings.Plugins);
        Assert.False(plugin.RefreshCommand.CanExecute(null));
        await plugin.ToggleEnabledCommand.ExecuteAsync(null);
        Assert.True(plugin.Enabled);
        Assert.False(plugin.IsLoaded);
        Assert.True(plugin.RestartRequired);
        Assert.False(plugin.RefreshCommand.CanExecute(null));
        Assert.Contains("Restart Winnow", plugin.Status);
        backend.Loaded = true;
        await settings.LoadAsync();
        Assert.Same(plugin, Assert.Single(settings.Plugins));
        Assert.True(plugin.RefreshCommand.CanExecute(null));
        await plugin.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(1, backend.Refreshes);
    }

    [Fact]
    public async Task Invalid_fields_and_storage_failures_do_not_disclose_secrets_or_leave_busy_controls()
    {
        var backend = new Backend();
        var settings = new PluginSettingsViewModel(backend);
        await settings.LoadAsync();
        var plugin = Assert.Single(settings.Plugins);
        plugin.Fields[1].Value = string.Empty;
        await plugin.SaveCommand.ExecuteAsync(null);
        Assert.Null(backend.Saved);
        Assert.Contains("Enter language", plugin.Status);
        plugin.Fields[1].Value = "en";
        plugin.Fields[0].Value = "private-value";
        backend.FailSave = true;
        await plugin.SaveCommand.ExecuteAsync(null);
        Assert.Contains("Could not save", plugin.Status);
        Assert.DoesNotContain("private-value", plugin.Status);
        Assert.False(plugin.IsBusy);
        Assert.True(plugin.Fields[0].IsEnabled);
        settings.ClearSecrets();
        Assert.Empty(plugin.Fields[0].Value);
    }

    [Fact]
    public async Task Busy_save_disables_other_operations_and_clear_during_save_does_not_restore_secret()
    {
        var backend = new Backend { PendingSave = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var settings = new PluginSettingsViewModel(backend);
        await settings.LoadAsync();
        var plugin = Assert.Single(settings.Plugins);
        plugin.Fields[0].Value = "private-value";
        var save = plugin.SaveCommand.ExecuteAsync(null);
        Assert.True(plugin.IsBusy);
        Assert.False(plugin.ToggleEnabledCommand.CanExecute(null));
        Assert.False(plugin.RefreshCommand.CanExecute(null));
        Assert.False(plugin.Fields[0].RemoveSecretCommand.CanExecute(null));
        Assert.False(plugin.Fields[0].IsEnabled);
        settings.ClearSecrets();
        backend.PendingSave.SetResult();
        await save;
        Assert.Empty(plugin.Fields[0].Value);
        Assert.False(plugin.IsBusy);
    }

    [Theory]
    [InlineData("https://example.com/api", true)]
    [InlineData("file:///C:/example.exe", false)]
    [InlineData("https://user:password@example.com/api", false)]
    [InlineData("javascript:alert(1)", false)]
    public async Task Manifest_links_only_offer_plain_https_web_destinations(string url, bool allowed)
    {
        var backend = new Backend { SetupUrl = url };
        var uris = new Uris();
        var settings = new PluginSettingsViewModel(backend, uris);
        await settings.LoadAsync();
        var field = Assert.Single(settings.Plugins).Fields[0];
        Assert.Equal(allowed, field.HasSetup);
        await field.OpenSetupCommand.ExecuteAsync(null);
        Assert.Equal(allowed, uris.Opened is not null);
    }

    [Fact]
    public async Task A_slow_status_reload_does_not_overwrite_newly_typed_drafts()
    {
        var backend = new Backend();
        var settings = new PluginSettingsViewModel(backend);
        await settings.LoadAsync();
        var plugin = Assert.Single(settings.Plugins);
        backend.PendingLoad = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var load = settings.LoadAsync();
        plugin.Fields[0].Value = "just-entered-secret";
        plugin.Fields[1].Value = "fr";
        backend.PendingLoad.SetResult();
        await load;
        Assert.Equal("just-entered-secret", plugin.Fields[0].Value);
        Assert.Equal("fr", plugin.Fields[1].Value);
        settings.ClearSecrets();
        Assert.Empty(plugin.Fields[0].Value);
    }

    private sealed class Uris : IUriDispatcher
    {
        public Uri? Opened { get; private set; }
        public Task<bool> OpenAsync(Uri uri) { Opened = uri; return Task.FromResult(true); }
    }

    private sealed class Backend : IPluginSettingsBackend
    {
        public string UserPluginDirectory => "plugins";
        public bool Enabled { get; set; } = true;
        public bool Loaded { get; set; } = true;
        public bool Stored { get; set; } = true;
        public bool FailSave { get; set; }
        public string SetupUrl { get; init; } = "https://example.com/api";
        public IReadOnlyDictionary<string, string>? Saved { get; private set; }
        public string? Removed { get; private set; }
        public int Refreshes { get; private set; }
        public TaskCompletionSource? PendingSave { get; init; }
        public TaskCompletionSource? PendingLoad { get; set; }
        public async Task<IReadOnlyList<PluginSettingsSnapshot>> LoadAsync(CancellationToken ct = default)
        {
            if (PendingLoad is not null) await PendingLoad.Task;
            return [new("community-art", "Community artwork", "Artwork from a community source.",
                "1.0.0", "Artwork", Enabled, Loaded, Enabled != Loaded, "Ready.", [
                    new("api-key", "API key", null, true, true, "must-never-be-shown", Stored, SetupUrl),
                    new("language", "Language", null, false, true, "en", false)])];
        }
        public async Task SaveAsync(string pluginId, IReadOnlyDictionary<string, string> values, CancellationToken ct = default)
        {
            if (FailSave) throw new IOException("private-value");
            Saved = values;
            if (PendingSave is not null) await PendingSave.Task;
        }
        public Task RemoveSecretAsync(string pluginId, string key, CancellationToken ct = default)
        { Removed = key; Stored = false; return Task.CompletedTask; }
        public Task SetEnabledAsync(string pluginId, bool enabled, CancellationToken ct = default)
        { Enabled = enabled; return Task.CompletedTask; }
        public Task RefreshAsync(string pluginId, CancellationToken ct = default)
        { Refreshes++; return Task.CompletedTask; }
    }
}
