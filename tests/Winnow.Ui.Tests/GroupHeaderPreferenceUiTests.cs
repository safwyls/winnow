using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Winnow.App.Design;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Winnow.Resolve;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class GroupHeaderPreferenceUiTests
{
    [AvaloniaFact]
    public async Task Desktop_header_selector_saves_and_automatic_restores_without_relinking()
    {
        using var fixture = await GroupHeaderFixture.CreateAsync();
        using var library = fixture.Library();
        using var queue = fixture.Queue(library);
        await queue.LoadCommand.ExecuteAsync(null);
        var card = Assert.Single(queue.Sections.SelectMany(section => section.Cards));
        var view = new MergeQueueView { DataContext = queue };
        var window = new Window { Width = 1000, Height = 850, Content = view };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var selector = Assert.Single(view.GetVisualDescendants().OfType<ComboBox>(),
                combo => AutomationProperties.GetName(combo)?.StartsWith("Header store for ", StringComparison.Ordinal) == true && combo.IsEffectivelyVisible);
            Assert.True(selector.Focus());
            Assert.True(selector.Bounds.Width >= 160);
            selector.SelectedItem = card.HeaderStoreOptions.Single(option => option.Store == "gog");
            await WaitUntil(() => card.SelectedHeaderStore?.Store == "gog" && !card.IsSavingHeader);
            Assert.Equal("GOG title", card.HeaderTitle);
            Assert.Equal("GOG title", Assert.Single(library.VisibleTiles).Title);
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), block => block.IsEffectivelyVisible && block.Text == "GOG title");
            selector.SelectedItem = card.HeaderStoreOptions.Single(option => option.Store is null);
            await WaitUntil(() => card.SelectedHeaderStore?.Store is null && !card.IsSavingHeader);
            Assert.Equal("Steam title", card.HeaderTitle);
            Assert.Single(await fixture.Links.GetHistoryAsync(), link => link.IsLive);
            Assert.Single(await fixture.Links.GetActsAsync());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Fullscreen_saved_group_offers_equivalent_store_choices_and_reset()
    {
        using var fixture = await GroupHeaderFixture.CreateAsync();
        using var library = fixture.Library();
        await library.LoadCommand.ExecuteAsync(null);
        var services = new ServiceCollection();
        services.AddSingleton<IMergeCandidateRepository>(new MergeCandidateRepository(fixture.Db.Factory));
        services.AddSingleton<IReleaseRepository>(fixture.Releases);
        services.AddSingleton<IWorkRepository>(fixture.Works);
        services.AddSingleton<IIdentityLinkRepository>(fixture.Links);
        services.AddSingleton<IOwnershipRepository>(fixture.Ownership);
        services.AddSingleton<IExpansionRefusalRepository>(new ExpansionRefusalRepository(fixture.Db.Factory));
        services.AddSingleton<ILibraryQueryRepository>(new LibraryQueryRepository(fixture.Db.Factory));
        services.AddSingleton<IGroupHeaderPreferenceRepository>(fixture.Preferences);
        services.AddSingleton(new LibraryExpansionScan(fixture.Releases, fixture.Links, new ExpansionRefusalRepository(fixture.Db.Factory)));
        services.AddSingleton(library);
        using var provider = services.BuildServiceProvider();
        using var context = new FullscreenContext(library, PreviewData.Feed, PreviewData.Shell, provider);
        using var page = new FullscreenIdentityPage(context);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        var opened = new List<FullscreenPage>();
        context.PageRequested += sheet => { opened.Add(sheet); window.Content = sheet; };
        Button Find(string text) => window.GetVisualDescendants().OfType<Button>().First(button => AutomationProperties.GetName(button)?.Contains(text, StringComparison.Ordinal) == true);
        void Click(Button button) { Assert.True(button.Focus()); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
        try
        {
            window.Show();
            await WaitUntil(() => page.GetVisualDescendants().OfType<Button>().Any(button => AutomationProperties.GetName(button)?.Contains("entries ·", StringComparison.Ordinal) == true));
            Click(Find("entries ·"));
            Click(Find("Header store · Automatic"));
            Click(Find("GOG"));
            await page.PendingAction;
            Assert.Equal("GOG title", library.VisibleTiles.Single().Title);
            Assert.Equal("gog", (await fixture.Preferences.GetAllAsync())[fixture.Steam.Work]);
            window.Content = page; Dispatcher.UIThread.RunJobs();
            Click(Find("entries ·"));
            Click(Find("Header store · GOG"));
            Click(Find("Automatic"));
            await page.PendingAction;
            Assert.Equal("Steam title", library.VisibleTiles.Single().Title);
            Assert.Null((await fixture.Preferences.GetAllAsync())[fixture.Steam.Work]);
            Assert.Single(await fixture.Links.GetActsAsync());
        }
        finally { window.Close(); foreach (var sheet in opened) sheet.Dispose(); }
    }

    private static async Task WaitUntil(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!predicate() && DateTime.UtcNow < deadline) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
        Assert.True(predicate());
    }
}
