using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Enrich.SteamGridDb;
using Xunit;

namespace Winnow.Tests;

public sealed class SteamGridDbSettingsTests
{
    [Fact]
    public async Task Save_activates_storage_without_reloading_the_key_and_remove_keeps_configuration_fallback()
    {
        var store = new Store();
        var service = new SteamGridDbSettingsService(store);
        var changes = 0;
        service.CredentialsChanged += () => changes++;
        var model = new SteamGridDbSettingsViewModel(service) { ApiKey = " user-key " };
        await model.SaveCommand.ExecuteAsync(null);
        Assert.Equal("user-key", store.Saved);
        Assert.True(model.HasSavedKey);
        Assert.Empty(model.ApiKey);
        Assert.Contains("Artwork refresh queued", model.Status);
        Assert.Equal(1, changes);
        model.ApiKey = "unsaved";
        await model.LoadAsync();
        Assert.Empty(model.ApiKey);
        Assert.Contains("saved on this device", model.Status);
        store.ConfigurationFallback = true;
        await model.RemoveCommand.ExecuteAsync(null);
        Assert.Null(store.Saved);
        Assert.False(model.HasSavedKey);
        Assert.Contains("configuration remains available", model.Status);
        Assert.Equal(2, changes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Refused_save_preserves_existing_key_and_does_not_announce_credentials_changed(bool canSave)
    {
        var store = new Store { Saved = "old-key", CanSave = canSave, Refuse = true };
        var service = new SteamGridDbSettingsService(store);
        var changes = 0;
        service.CredentialsChanged += () => changes++;
        var model = new SteamGridDbSettingsViewModel(service) { ApiKey = "new-key" };
        await model.SaveCommand.ExecuteAsync(null);
        Assert.Equal("old-key", store.Saved);
        Assert.Equal(0, changes);
        Assert.False(model.IsBusy);
        Assert.DoesNotContain("new-key", model.Status);
        Assert.Contains(canSave ? "Check the API key" : "nothing was saved", model.Status);
    }

    [Fact]
    public async Task Blank_key_and_storage_exceptions_are_reported_without_secret_details()
    {
        var store = new Store();
        var model = new SteamGridDbSettingsViewModel(new SteamGridDbSettingsService(store));
        await model.SaveCommand.ExecuteAsync(null);
        Assert.Contains("Enter your API key", model.Status);
        Assert.Null(store.Saved);
        store.Throw = true;
        model.ApiKey = "secret-value";
        await model.SaveCommand.ExecuteAsync(null);
        Assert.Contains("Could not save", model.Status);
        Assert.DoesNotContain("secret-value", model.Status);
        await model.LoadAsync();
        Assert.Empty(model.ApiKey);
        Assert.Contains("Could not read", model.Status);
        await model.RemoveCommand.ExecuteAsync(null);
        Assert.Contains("Could not remove", model.Status);
        Assert.DoesNotContain("secret-value", model.Status);
        Assert.False(model.IsBusy);
    }

    [Fact]
    public async Task Setup_opens_the_API_key_page_and_handles_browser_refusal()
    {
        var uris = new Uris();
        var model = new SteamGridDbSettingsViewModel(uris: uris);
        await model.OpenSetupCommand.ExecuteAsync(null);
        Assert.Equal("https://www.steamgriddb.com/profile/preferences/api", uris.Opened?.AbsoluteUri);
        Assert.Contains("Could not open the browser", model.Status);
    }

    private sealed class Uris : IUriDispatcher
    {
        public Uri? Opened { get; private set; }
        public Task<bool> OpenAsync(Uri uri) { Opened = uri; return Task.FromResult(false); }
    }

    private sealed class Store : ISteamGridDbSettingsStore
    {
        public string? Saved { get; set; }
        public bool ConfigurationFallback { get; set; }
        public bool CanSave { get; init; } = true;
        public bool Refuse { get; init; }
        public bool Throw { get; set; }
        public Task<SteamGridDbCredentialStatus> GetStatusAsync(CancellationToken ct = default)
        {
            if (Throw) throw new IOException("secret-value");
            return Task.FromResult(new SteamGridDbCredentialStatus(Saved is not null, Saved is not null || ConfigurationFallback,
                CanSave, Saved is not null ? "saved" : ConfigurationFallback ? "environment or configuration" : null));
        }
        public Task<bool> SaveAsync(string apiKey, CancellationToken ct = default)
        {
            if (Throw) throw new IOException("secret-value");
            if (Refuse || !CanSave) return Task.FromResult(false);
            Saved = apiKey;
            return Task.FromResult(true);
        }
        public Task RemoveAsync(CancellationToken ct = default)
        {
            if (Throw) throw new IOException("secret-value");
            Saved = null;
            return Task.CompletedTask;
        }
    }
}
