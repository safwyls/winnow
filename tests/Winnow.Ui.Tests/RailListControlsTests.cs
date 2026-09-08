using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.ViewModels.Lists;
using Winnow.App.Views;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class RailListControlsTests
{
    [AvaloniaFact]
    public void Sections_collapse_independently_and_footer_creates_either_kind()
    {
        var shell = PreviewData.Shell;
        shell.Library.Lightbox.CloseCommand.Execute(null);
        shell.Library.CloseDetailsCommand.Execute(null);
        shell.Library.Prompt?.CancelCommand.Execute(null);
        shell.ShowLibraryCommand.Execute(null);
        var lists = shell.Library.Lists;
        var manual = new GameListViewModel(GameList.Manual("Rail test static") with { Id = -101 });
        var live = new GameListViewModel(GameList.Live("Rail test live", LibraryFilter.Empty) with { Id = -102 });
        lists.Lists.Add(manual);
        lists.LiveLists.Add(live);
        lists.AreListsExpanded = lists.AreLiveListsExpanded = true;
        lists.IsCreateMenuOpen = false;
        var window = new MainWindow();
        try
        {
            window.Show();
            window.DataContext = shell;
            Flush();
            var controls = window.GetVisualDescendants().OfType<Button>().ToArray();
            var staticHeader = controls.Single(button => AutomationProperties.GetName(button) == "LISTS");
            var liveHeader = controls.Single(button => AutomationProperties.GetName(button) == "LIVE LISTS");
            var staticRows = window.GetVisualDescendants().OfType<ItemsControl>()
                .Single(control => ReferenceEquals(control.ItemsSource, lists.Lists));
            var liveRows = window.GetVisualDescendants().OfType<ItemsControl>()
                .Single(control => ReferenceEquals(control.ItemsSource, lists.LiveLists));
            Activate(window, staticHeader);
            Assert.False(staticRows.IsVisible);
            Assert.True(liveRows.IsVisible);
            Assert.Equal("Collapsed", AutomationProperties.GetItemStatus(staticHeader));
            Activate(window, liveHeader);
            Assert.False(liveRows.IsVisible);
            Activate(window, staticHeader);
            Assert.True(staticRows.IsVisible);
            Assert.False(liveRows.IsVisible);

            var create = controls.Single(button => AutomationProperties.GetName(button) == "New list");
            var settings = window.FindControl<Button>("SettingsButton")!;
            var createBounds = new Rect(create.TranslatePoint(default, window)!.Value, create.Bounds.Size);
            var settingsBounds = new Rect(settings.TranslatePoint(default, window)!.Value, settings.Bounds.Size);
            Assert.True(createBounds.Right < settingsBounds.Left);
            Assert.Equal(createBounds.Center.Y, settingsBounds.Center.Y);
            Assert.True(settingsBounds.Bottom <= window.ClientSize.Height);

            foreach (var (label, question) in new[] { ("Static list", "Name this list"), ("Live list", "Name this live list") })
            {
                Activate(window, create);
                var choice = controls.Single(button => Equals(button.Content, label));
                Assert.True(choice.IsEffectivelyVisible);
                Assert.False(string.IsNullOrWhiteSpace(Assert.IsType<string>(ToolTip.GetTip(choice))));
                Activate(window, choice);
                Assert.False(lists.IsCreateMenuOpen);
                Assert.Equal(question, shell.Library.Prompt!.Question);
                shell.Library.Prompt.CancelCommand.Execute(null);
                Flush();
                Assert.Same(create, window.FocusManager!.GetFocusedElement());
                Flush();
            }
        }
        finally
        {
            shell.Library.Prompt?.CancelCommand.Execute(null);
            lists.Lists.Remove(manual);
            lists.LiveLists.Remove(live);
            lists.AreListsExpanded = lists.AreLiveListsExpanded = true;
            lists.IsCreateMenuOpen = false;
            window.Close();
        }
    }

    private static void Activate(Window window, Button button)
    {
        Assert.True(button.Focus(NavigationMethod.Tab));
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Flush();
    }

    private static void Flush() => Dispatcher.UIThread.RunJobs();
}
