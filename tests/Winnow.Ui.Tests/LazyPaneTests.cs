using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Views;
using Xunit;

namespace Winnow.Ui.Tests;

/// <summary>
/// The shell's hidden screens are built on first show rather than at startup
/// (<see cref="LazyPane"/>, TASK-152.3). These tests hold that line: the heap
/// saving in <c>docs/spikes/memory-footprint.md</c> is only there while a pane
/// the user has not opened has no controls in the window's tree, and a pane
/// that is declared eagerly again would still look and behave correctly on
/// screen — which is exactly why nothing else would catch it.
/// </summary>
public sealed class LazyPaneTests
{
    [AvaloniaFact]
    public void Panes_are_absent_from_the_tree_until_they_are_first_shown()
    {
        var shell = PreviewData.Shell;

        // PreviewData's instances are process-wide, so put the shell back on
        // the library screen with nothing open before reading the tree.
        shell.Library.Lightbox.CloseCommand.Execute(null);
        shell.Library.CloseDetailsCommand.Execute(null);
        shell.ShowLibraryCommand.Execute(null);

        var window = new MainWindow { DataContext = shell };
        try
        {
            window.Show();

            Assert.Empty(window.GetVisualDescendants().OfType<GameDetailsView>());
            Assert.Empty(window.GetVisualDescendants().OfType<ScreenshotLightboxView>());
            Assert.Empty(window.GetVisualDescendants().OfType<MergeQueueView>());
            Assert.Empty(window.GetVisualDescendants().OfType<AccountStatsView>());
            Assert.Empty(window.GetVisualDescendants().OfType<StoresView>());
            Assert.Empty(window.GetVisualDescendants().OfType<LibrarySettingsView>());
            Assert.Empty(window.GetVisualDescendants().OfType<AppearanceView>());

            // Opening one screen builds that screen and no other.
            shell.ToggleMergeQueueCommand.Execute(null);

            Assert.Single(window.GetVisualDescendants().OfType<MergeQueueView>());
            Assert.Empty(window.GetVisualDescendants().OfType<AccountStatsView>());

            // The container stretches exactly as the Border it replaced did.
            // A pane that shrink-wrapped instead would still be in the tree and
            // still be bound, so nothing else here would notice.
            Dispatcher.UIThread.RunJobs();
            var queue = window.GetVisualDescendants().OfType<MergeQueueView>().Single();
            Assert.True(
                queue.Bounds is { Width: > 900, Height: > 600 },
                $"The merge queue was arranged at {queue.Bounds}, not across the pane.");

            shell.ShowAppearanceCommand.Execute(null);

            Assert.Single(window.GetVisualDescendants().OfType<AppearanceView>());
            Assert.Empty(window.GetVisualDescendants().OfType<StoresView>());
            Assert.Empty(window.GetVisualDescendants().OfType<LibrarySettingsView>());

            // Built once: a second visit reuses the pane it already has, and
            // its own IsVisible follows the container both ways. Two panes read
            // that property as their open and close signal (the lightbox's
            // focus trap, the modal's refusal to restore focus while closing),
            // so a pane left permanently visible inside a hidden container
            // would be a silent regression.
            var appearance = window.GetVisualDescendants().OfType<AppearanceView>().Single();
            Assert.True(appearance.IsVisible);

            shell.ShowLibraryCommand.Execute(null);
            Assert.False(appearance.IsVisible);

            shell.ShowAppearanceCommand.Execute(null);

            Assert.Same(appearance, window.GetVisualDescendants().OfType<AppearanceView>().Single());
            Assert.True(appearance.IsVisible);
        }
        finally
        {
            shell.ShowLibraryCommand.Execute(null);
            window.Close();
        }
    }

    /// <summary>
    /// A pane's own <c>IsVisible</c> follows its container, and a pane is born
    /// visible: it is built at the moment it is first shown, so there is no
    /// first change to see. <c>ScreenshotLightboxView</c> reads that property —
    /// it arms its focus trap when it turns on and announces its close when it
    /// turns off — so both halves are load-bearing. A false fed in at birth
    /// would make it announce a close it never had; leaving it stuck at true
    /// would swallow every real close.
    /// </summary>
    [AvaloniaFact]
    public void A_panes_visibility_follows_its_container_without_a_change_at_birth()
    {
        var window = new Window { Width = 400, Height = 300 };
        var pane = new LazyPane
        {
            PaneTemplate = new FuncDataTemplate<object?>(
                (_, _) => new VisibilitySpy(),
                supportsRecycling: false),
        };

        window.Content = pane;
        try
        {
            window.Show();
            Assert.Null(pane.Pane);

            pane.IsVisible = true;
            var spy = Assert.IsType<VisibilitySpy>(pane.Pane);
            Assert.True(spy.IsVisible);
            Assert.True(spy.WasAttachedVisible);
            Assert.Empty(spy.Changes);

            pane.IsVisible = false;
            pane.IsVisible = true;
            Assert.Equal([false, true], spy.Changes);
        }
        finally
        {
            window.Close();
        }
    }

    private sealed class VisibilitySpy : UserControl
    {
        public List<bool> Changes { get; } = [];

        /// <summary>What the lightbox's own attach hook reads.</summary>
        public bool WasAttachedVisible { get; private set; }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            WasAttachedVisible = IsVisible;
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == IsVisibleProperty)
            {
                Changes.Add(change.GetNewValue<bool>());
            }
        }
    }

    /// <summary>
    /// The modal is the pane with a code-behind seam: the window reaches it
    /// through <c>DetailsPanel.Pane</c> and wires its close request when it
    /// appears, rather than holding a generated field. It also carries its own
    /// DataContext binding, which a pane built from a template has to keep.
    /// </summary>
    [AvaloniaFact]
    public async Task The_detail_modal_is_built_with_its_data_when_a_game_is_opened()
    {
        await PreviewData.LoadShellAsync();

        var shell = PreviewData.Shell;
        shell.Library.CloseDetailsCommand.Execute(null);
        shell.ShowLibraryCommand.Execute(null);

        var window = new MainWindow { DataContext = shell };
        try
        {
            window.Show();
            Assert.Empty(window.GetVisualDescendants().OfType<GameDetailsView>());

            var tile = shell.Library.VisibleTiles.Single(t => t.Title == "Stardew Valley");
            await shell.Library.OpenDetailsCommand.ExecuteAsync(tile);

            var modal = Assert.Single(window.GetVisualDescendants().OfType<GameDetailsView>());
            Assert.Same(shell.Library.Details, modal.DataContext);
        }
        finally
        {
            shell.Library.CloseDetailsCommand.Execute(null);
            window.Close();
        }
    }
}
