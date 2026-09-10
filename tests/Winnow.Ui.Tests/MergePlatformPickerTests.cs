using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Winnow.Resolve;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class MergePlatformPickerTests
{
    [AvaloniaFact]
    public async Task Fullscreen_controller_selects_platform_and_remembers_it_on_reopen()
    {
        using var db = new TempDatabase();
        var settings = new SettingsRepository(db.Factory);
        using var services = new ServiceCollection()
            .AddSingleton<IMergeCandidateRepository>(new MergeCandidateRepository(db.Factory))
            .AddSingleton<IReleaseRepository>(new ReleaseRepository(db.Factory))
            .AddSingleton<IWorkRepository>(new WorkRepository(db.Factory))
            .AddSingleton<IIdentityLinkRepository>(new IdentityLinkRepository(db.Factory))
            .AddSingleton<IOwnershipRepository>(new OwnershipRepository(db.Factory))
            .AddSingleton<IExpansionRefusalRepository>(new ExpansionRefusalRepository(db.Factory))
            .AddSingleton<ILibraryQueryRepository>(new LibraryQueryRepository(db.Factory))
            .AddSingleton<ISettingsRepository>(settings)
            .AddSingleton<LibraryExpansionScan>()
            .BuildServiceProvider();
        using var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell, services);
        using var page = new FullscreenIdentityPage(context);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        FullscreenPage? sheet = null;
        context.PageRequested += opened => { sheet = opened; window.Content = opened; };
        context.BackRequested += () => window.Content = page;
        try
        {
            window.Show();
            await Until(() => page.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "No possible matches to review."));
            page.FocusInitial();
            page.Handle(GamepadButtons.Down);
            page.Handle(GamepadButtons.Down);
            page.Handle(GamepadButtons.Accept);
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(sheet);
            sheet.FocusInitial();
            sheet.Handle(GamepadButtons.Down);
            sheet.Handle(GamepadButtons.Accept);
            await Until(() => page.GetVisualDescendants().OfType<Button>().Any(b => Equals(b.Content, "Prefer · Steam")));
            Assert.Equal("steam", await settings.GetAsync(MergeQueueViewModel.PreferredPlatformSettingKey));

            using var reopened = new FullscreenIdentityPage(context);
            window.Content = reopened;
            await Until(() => reopened.GetVisualDescendants().OfType<Button>().Any(b => Equals(b.Content, "Prefer · Steam")));
        }
        finally { window.Close(); sheet?.Dispose(); }
    }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(condition());
    }

    [AvaloniaFact]
    public void Desktop_picker_applies_option_closes_and_updates_accessible_status()
    {
        var model = PreviewData.MergeQueue;
        var original = model.PlatformOptions.Single(option => option.IsSelected);
        var view = new MergeQueueView { DataContext = model };
        var window = new Window { Width = 980, Height = 640, Content = view };
        window.Show();
        var trigger = view.FindControl<Button>("PreferredPlatformButton")!;
        var flyout = Assert.IsType<Flyout>(trigger.Flyout);
        try
        {
            flyout.ShowAt(trigger);
            Dispatcher.UIThread.RunJobs();
            var content = Assert.IsType<ItemsControl>(flyout.Content);
            var steam = content.GetVisualDescendants().OfType<Button>()
                .Single(button => button.DataContext is MergePlatformOptionViewModel { Store: "steam" });
            Assert.True(steam.Focus(NavigationMethod.Tab));
            steam.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.False(flyout.IsOpen);
            Assert.Equal("Prefer · Steam", model.PreferredPlatformLabel);
            Assert.Equal(model.PreferredPlatformLabel, AutomationProperties.GetName(trigger));
            Assert.Equal(model.PreferredPlatformLabel, AutomationProperties.GetItemStatus(trigger));
            var bar = Assert.IsType<Grid>(trigger.Parent);
            Assert.True(trigger.Bounds.Left >= 0);
            Assert.True(trigger.Bounds.Right <= bar.Bounds.Width);
            var count = bar.Children.OfType<TextBlock>().Single();
            Assert.True(trigger.Bounds.Right <= count.Bounds.Left);
        }
        finally
        {
            model.SelectPlatformCommand.Execute(original);
            flyout.Hide();
            window.Close();
        }
    }
}
