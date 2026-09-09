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
    public async Task Runtime_factory_isolates_library_lists_journal_and_theme_from_desktop()
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
        var context = FullscreenContext.Create(services, PreviewData.Shell);
        Assert.NotSame(PreviewData.Library, context.Library);
        Assert.NotSame(PreviewData.Library.Lists, context.Library.Lists);
        Assert.NotSame(PreviewData.Library.Journal, context.Library.Journal);
        Assert.Same(titleLookup, PreviewData.Library.Journal.TitleFor);
        await context.LoadAsync();
        var desktopQuery = PreviewData.Library.SearchText;
        context.Library.SearchText = "does not exist";
        Assert.Equal(desktopQuery, PreviewData.Library.SearchText);
        var desktopDimming = PreviewData.Library.Ramp.DimsDormantCovers;
        context.DimCovers = !desktopDimming;
        Assert.Equal(!desktopDimming, context.Library.Ramp.DimsDormantCovers);
        Assert.Equal(desktopDimming, PreviewData.Library.Ramp.DimsDormantCovers);
        var desktopTheme = PreviewData.Shell.Appearance.Service.Theme;
        using var view = new FullscreenView(context);
        var window = new Window { Width = 1920, Height = 1080, Content = view };
        window.Show();
        try
        {
            context.ThemeId = context.Themes.Last().Id;
            Dispatcher.UIThread.RunJobs();
            Assert.Same(desktopTheme, PreviewData.Shell.Appearance.Service.Theme);
            Assert.Equal(context.ThemeId, settings.Values["fullscreen.theme"]);
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
