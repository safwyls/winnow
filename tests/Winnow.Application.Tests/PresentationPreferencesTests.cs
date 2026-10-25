using Winnow.Api.Contracts.Preferences;
using Winnow.App.Services;
using Winnow.Core.Repositories;
using Winnow.Enrich.Igdb.Storage;
using Xunit;

namespace Winnow.Application.Tests;

public sealed class PresentationPreferencesTests
{
    [Theory]
    [InlineData("0.85")]
    [InlineData("1.2")]
    public async Task LegacyFullscreenScaleIsExcludedWhileNewAdjustmentsAndOtherPreferencesSurviveReload(string legacyScale)
    {
        var settings = new MemorySettings();
        settings.Values["fullscreen.ui-scale"] = legacyScale;
        settings.Values["fullscreen.text-scale"] = "1.3";
        settings.Values["fullscreen.safe-margin"] = "3";
        settings.Values["fullscreen.reduced-motion"] = "true";
        settings.Values["fullscreen.fit-ultrawide"] = "true";
        var changes = new Changes();
        var preferences = new PresentationPreferencesService(settings, changes, new ArtworkPreferences(settings));

        var initial = (await preferences.ReadAsync(default)).ToDictionary(row => row.Preference, row => row.Value);
        Assert.Null(initial[PresentationPreference.FullscreenInterfaceScale]);
        Assert.Equal("1.3", initial[PresentationPreference.FullscreenTextScale]);
        Assert.Equal("3", initial[PresentationPreference.FullscreenSafeMargin]);
        Assert.Equal("true", initial[PresentationPreference.FullscreenReducedMotion]);
        Assert.Equal("true", initial[PresentationPreference.FullscreenFitUltrawide]);
        Assert.False(settings.Values.ContainsKey("fullscreen.ui-scale-v2"));
        Assert.Empty(changes.Events);

        await preferences.SetAsync(PresentationPreference.FullscreenInterfaceScale, "1.1", default);
        Assert.Equal("1.1", settings.Values["fullscreen.ui-scale-v2"]);
        Assert.Equal(legacyScale, settings.Values["fullscreen.ui-scale"]);
        Assert.Equal(("preferences.changed", "FullscreenInterfaceScale"), Assert.Single(changes.Events));

        var reloaded = new PresentationPreferencesService(settings, new Changes(), new ArtworkPreferences(settings));
        var current = (await reloaded.ReadAsync(default)).ToDictionary(row => row.Preference, row => row.Value);
        Assert.Equal("1.1", current[PresentationPreference.FullscreenInterfaceScale]);
        foreach (var preference in initial.Keys.Where(key => key != PresentationPreference.FullscreenInterfaceScale))
            Assert.Equal(initial[preference], current[preference]);
    }

    private sealed class MemorySettings : ISettingsRepository, ISettingsStore
    {
        public Dictionary<string, string> Values { get; } = [];
        public Task<string?> GetAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(Values.GetValueOrDefault(key));
        public Task SetAsync(string key, string value, CancellationToken ct = default)
        {
            Values[key] = value;
            return Task.CompletedTask;
        }
        Task ISettingsStore.SetAsync(string key, string? value, CancellationToken ct) =>
            value is null ? RemoveAsync(key, ct) : SetAsync(key, value, ct);
        public Task RemoveAsync(string key, CancellationToken ct = default)
        {
            Values.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class Changes : IApplicationChangePublisher
    {
        public List<(string Kind, string? Resource)> Events { get; } = [];
        public void Publish(string kind, string? resource = null) => Events.Add((kind, resource));
    }
}
