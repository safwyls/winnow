using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views.Fullscreen;
using Winnow.Core.Repositories;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class FullscreenContextTests
{
    [AvaloniaFact]
    public async Task Runtime_factory_isolates_library_state_but_shares_the_persisted_theme()
    {
        var settings = new MemorySettings();
        var registrations = new ServiceCollection();
        registrations.AddSingleton<ILibraryQueryRepository, PreviewLibraryQueryRepository>();
        registrations.AddSingleton<IOwnershipRepository, PreviewOwnershipRepository>();
        registrations.AddSingleton<IReleaseRepository, PreviewReleaseRepository>();
        registrations.AddSingleton<IWorkRepository, PreviewWorkRepository>();
        registrations.AddSingleton<IUpdateEventRepository, PreviewUpdateEventRepository>();
        registrations.AddSingleton<IFeedService, PreviewFeedService>();
        registrations.AddSingleton<ISettingsRepository>(settings);
        registrations.AddSingleton(PreviewData.Library);
        registrations.AddSingleton(PreviewData.Library.Journal);
        registrations.AddSingleton(PreviewData.Library.Ramp);
        using var services = registrations.BuildServiceProvider();
        var titleLookup = PreviewData.Library.Journal.TitleFor;
        var original = PreviewData.Shell;
        var appearance = new AppearanceViewModel(new ThemeService(settings));
        var shell = new MainWindowViewModel(original.Library, original.MergeQueue, original.Stores, appearance,
            original.Feed, original.AccountStats, original.LibrarySettings, settings: settings);
        var context = FullscreenContext.Create(services, shell);
        Assert.NotSame(PreviewData.Library, context.Library);
        Assert.NotSame(PreviewData.Library.Lists, context.Library.Lists);
        Assert.NotSame(PreviewData.Library.Journal, context.Library.Journal);
        Assert.Same(titleLookup, PreviewData.Library.Journal.TitleFor);
        await context.LoadAsync();
        context.SetFitUltrawide(true);
        Assert.Equal("True", settings.Values["fullscreen.fit-ultrawide"]);
        await context.LoadAsync();
        Assert.True(context.FitUltrawide);
        var desktopQuery = PreviewData.Library.SearchText;
        context.Library.SearchText = "does not exist";
        Assert.Equal(desktopQuery, PreviewData.Library.SearchText);
        var desktopDimming = PreviewData.Library.Ramp.DimsDormantCovers;
        context.DimCovers = !desktopDimming;
        Assert.Equal(!desktopDimming, context.Library.Ramp.DimsDormantCovers);
        Assert.Equal(!desktopDimming, PreviewData.Library.Ramp.DimsDormantCovers);
        await shell.Display.PendingSave;
        Assert.Equal((!desktopDimming).ToString().ToLowerInvariant(), settings.Values[DormancyRamp.DimCoversSettingKey]);
        settings.Values["fullscreen.dim-covers"] = desktopDimming.ToString();
        context.ReducedMotion = true;
        await context.LoadAsync();
        Assert.Equal(!desktopDimming, context.DimCovers);
        Assert.True(context.Library.Ramp.ReducedMotion);
        var reloadedRamp = new DormancyRamp();
        var reloadedDisplay = new DisplaySettingsViewModel(reloadedRamp, settings);
        await reloadedDisplay.LoadAsync();
        Assert.Equal(!desktopDimming, reloadedRamp.DimsDormantCovers);
        context.SetActive(false);
        shell.Display.DimDormantCovers = desktopDimming;
        context.SetActive(true);
        Assert.Equal(desktopDimming, context.Library.Ramp.DimsDormantCovers);
        Assert.True(context.Library.Ramp.ReducedMotion);
        settings.Values["fullscreen.theme"] = context.Themes.Last().Id;
        await context.LoadAsync();
        Assert.Equal(appearance.Service.Theme.Id, context.ThemeId);
        using var view = new FullscreenView(context);
        var backdrop = new FullscreenBackdrop(context, PreviewData.Tile, cinematic: true);
        var window = new Window { Width = 1920, Height = 1080, Content = new Grid { Children = { view, backdrop } } };
        window.Show();
        try
        {
            context.ThemeId = context.Themes.Last().Id;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(context.ThemeId, appearance.Service.Theme.Id);
            Assert.True(appearance.Themes.Single(t => t.Theme.Id == context.ThemeId).IsSelected);
            await appearance.Service.PendingSave;
            Assert.Equal(context.ThemeId, settings.Values[ThemeService.ThemeSettingKey]);
            Assert.Equal(appearance.Service.Theme.Ground, Assert.IsType<Avalonia.Media.SolidColorBrush>(view.Resources["Ground"]).Color);
            appearance.Service.SelectTheme(context.Themes.First());
            Assert.Equal(appearance.Service.Theme.Id, context.ThemeId);
            Assert.Equal(appearance.Service.Theme.Ground, Assert.IsType<Avalonia.Media.SolidColorBrush>(view.Resources["Ground"]).Color);
            var gradients = backdrop.Children.OfType<Border>().Select(b => b.Background).OfType<Avalonia.Media.LinearGradientBrush>().ToArray();
            Assert.Equal(3, gradients.Length);
            foreach (var gradient in gradients)
                foreach (var stop in gradient.GradientStops)
                {
                    Assert.Equal(appearance.Service.Theme.Ground.R, stop.Color.R);
                    Assert.Equal(appearance.Service.Theme.Ground.G, stop.Color.G);
                    Assert.Equal(appearance.Service.Theme.Ground.B, stop.Color.B);
                }
            await appearance.Service.PendingSave;
            var reloaded = new ThemeService(settings);
            await reloaded.LoadAsync();
            Assert.Equal(context.ThemeId, reloaded.Theme.Id);
            context.TextScale = .7;
            await context.LoadAsync();
            Assert.Equal(.7, context.TextScale);
        }
        finally { window.Close(); }
    }

    private sealed class MemorySettings : ISettingsRepository
    {
        public Dictionary<string, string> Values { get; } = [];
        public Task<string?> GetAsync(string key, CancellationToken ct = default) => Task.FromResult(Values.GetValueOrDefault(key));
        public Task SetAsync(string key, string value, CancellationToken ct = default) { Values[key] = value; return Task.CompletedTask; }
    }
}
