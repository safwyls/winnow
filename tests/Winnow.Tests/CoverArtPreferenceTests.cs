using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Core.Repositories;
using Xunit;

namespace Winnow.Tests;

public sealed class CoverArtPreferenceTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("invalid", true)]
    [InlineData("fit", true)]
    [InlineData("fill", false)]
    public async Task Loads_mode_without_writing_back(string? value, bool fit)
    {
        var store = new Settings { Mode = value };
        var display = new DisplaySettingsViewModel(new DormancyRamp(), store);
        await display.LoadAsync();
        Assert.Equal(fit, display.FitCoverArt);
        Assert.Equal(fit ? 0 : 1, display.CoverArtFitIndex);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task Selector_persists_and_reloads_both_modes()
    {
        var store = new Settings();
        var display = new DisplaySettingsViewModel(new DormancyRamp(), store);
        var changes = new List<string?>();
        display.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        display.CoverArtFitIndex = 1;
        await display.PendingSave;
        Assert.Equal("fill", store.Mode);
        Assert.Contains(nameof(display.CoverArtFitIndex), changes);
        var reopened = new DisplaySettingsViewModel(new DormancyRamp(), store);
        await reopened.LoadAsync();
        Assert.False(reopened.FitCoverArt);
        reopened.CoverArtFitIndex = -1;
        Assert.False(reopened.FitCoverArt);
        reopened.CoverArtFitIndex = 0;
        await reopened.PendingSave;
        Assert.Equal("fit", store.Mode);
    }

    private sealed class Settings : ISettingsRepository
    {
        public string? Mode { get; set; }
        public int Writes { get; private set; }
        public Task<string?> GetAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(key == DisplaySettingsViewModel.CoverArtModeSettingKey ? Mode : null);
        public Task SetAsync(string key, string value, CancellationToken ct = default)
        {
            Assert.Equal(DisplaySettingsViewModel.CoverArtModeSettingKey, key);
            Mode = value;
            Writes++;
            return Task.CompletedTask;
        }
    }
}
