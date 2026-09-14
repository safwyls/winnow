using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.Core.Domain;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class FeedCardActionTests
{
    [AvaloniaFact]
    public async Task Built_in_shelf_headers_explain_their_membership_in_tooltips()
    {
        await PreviewData.LoadShellAsync();
        var model = PreviewData.Feed;
        await model.LoadCommand.ExecuteAsync(null);
        var view = new FeedView { DataContext = model };
        var window = new Window { Width = 1000, Height = 700, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var explanations = view.GetVisualDescendants().OfType<StackPanel>()
                .Select(ToolTip.GetTip).OfType<string>().ToArray();
            Assert.All(model.Shelves, shelf => Assert.Contains(shelf.Blurb, explanations));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(180)]
    [InlineData(240)]
    public void Portrait_card_actions_are_accessible_without_moving_the_card(double width)
    {
        var tile = TileFixture.Tile(DateTime.UtcNow, title: "A long game title that needs installing",
            steamAppId: "123", ownership: new Ownership { ReleaseId = 1, Store = "steam" });
        using var model = new FeedCardViewModel(tile, "You have never opened this game.",
            new FeedbackService(), _ => { });
        var view = new FeedCardView { DataContext = model, Width = width,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var window = new Window { Width = 800, Height = 700, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var before = view.Bounds.Size;
            var card = view.FindControl<Button>("Card")!;
            Assert.True(card.Focus(NavigationMethod.Tab));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(before, view.Bounds.Size);
            var names = new[] { "AddToList", "NotNow", "NotInterested" };
            var labels = new[] { "Add to list", "Not now", "Not interested" };
            var inks = new[] { "Azure", "Amber", "TextDim" };
            Rect? previous = null;
            for (var i = 0; i < names.Length; i++)
            {
                var button = view.FindControl<Button>(names[i])!;
                Assert.True(button.IsEffectivelyVisible);
                Assert.Equal(labels[i], AutomationProperties.GetName(button));
                Assert.Equal(labels[i], ToolTip.GetTip(button));
                var icon = Assert.IsType<Avalonia.Controls.Shapes.Path>(button.Content);
                Assert.Equal(Assert.IsAssignableFrom<ISolidColorBrush>(view.FindResource(inks[i])).Color,
                    Assert.IsAssignableFrom<ISolidColorBrush>(icon.Stroke).Color);
                var bounds = new Rect(button.TranslatePoint(default, view)!.Value, button.Bounds.Size);
                Assert.True(bounds.Width >= 32 && bounds.Height >= 32);
                Assert.InRange(bounds.Left, 0, view.Bounds.Width);
                Assert.True(bounds.Right <= view.Bounds.Width);
                if (previous is { } earlier)
                {
                    Assert.Equal(earlier.Top, bounds.Top);
                    Assert.True(bounds.Left >= earlier.Right);
                }
                previous = bounds;
                Assert.True(button.Focus(NavigationMethod.Tab));
            }
            Assert.Equal(before, view.Bounds.Size);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("AddToList")]
    [InlineData("NotNow")]
    [InlineData("NotInterested")]
    public void Pointer_actions_do_not_open_details_and_feedback_keeps_undo(string action)
    {
        var added = 0;
        var opened = 0;
        var tile = TileFixture.Tile(DateTime.UtcNow, title: "Aloft", steamAppId: "123");
        tile.OpenDetailsCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => opened++);
        using var model = new FeedCardViewModel(tile, "Last played on 7 Sep 2026.",
            new FeedbackService(), _ => added++);
        var view = new FeedCardView { DataContext = model, Width = 220,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var window = new Window { Width = 800, Height = 700, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var card = view.FindControl<Button>("Card")!;
            var point = card.TranslatePoint(new Point(50, 50), window)!.Value;
            window.MouseMove(point);
            Dispatcher.UIThread.RunJobs();
            var flyout = Assert.IsType<Flyout>(FlyoutBase.GetAttachedFlyout(card));
            Assert.True(flyout.IsOpen);
            var before = view.Bounds.Size;
            var button = view.FindControl<Button>(action)!;
            Assert.True(button.IsEffectivelyVisible);
            Click(window, button);
            Assert.Equal(0, opened);
            Assert.Equal(before, view.Bounds.Size);
            if (action == "AddToList")
            {
                Assert.Equal(1, added);
                Assert.False(model.IsSetAside);
            }
            else
            {
                Assert.True(model.IsSetAside);
                var undo = view.GetVisualDescendants().OfType<Button>()
                    .Single(b => ReferenceEquals(b.Command, model.UndoCommand));
                Assert.True(undo.IsEffectivelyVisible);
                Click(window, undo);
                Assert.False(model.IsSetAside);
                Assert.Equal(0, opened);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(null)]
    [InlineData(PhysicalKey.Enter)]
    [InlineData(PhysicalKey.Space)]
    public void Card_art_activation_opens_full_details_directly(PhysicalKey? key)
    {
        var opened = 0;
        var tile = TileFixture.Tile(DateTime.UtcNow, title: "Aloft", steamAppId: "123");
        tile.OpenDetailsCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => opened++);
        using var model = new FeedCardViewModel(tile, "A reason", new FeedbackService());
        var view = new FeedCardView { DataContext = model, Width = 220 };
        var window = new Window { Width = 1000, Height = 800, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var card = view.FindControl<Button>("Card")!;
            var flyout = Assert.IsType<Flyout>(FlyoutBase.GetAttachedFlyout(card));
            if (key is { } physicalKey)
            {
                Assert.True(card.Focus(NavigationMethod.Tab));
                window.KeyPressQwerty(physicalKey, RawInputModifiers.None);
                window.KeyReleaseQwerty(physicalKey, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
            }
            else Click(window, card, new Point(40, 40));
            Assert.Equal(1, opened);
            Assert.False(flyout.IsOpen);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Quick_details_closes_when_card_is_rebound_or_detached(bool rebind)
    {
        using var first = new FeedCardViewModel(TileFixture.Tile(DateTime.UtcNow), "First reason", new FeedbackService());
        using var second = new FeedCardViewModel(TileFixture.Tile(DateTime.UtcNow), "Second reason", new FeedbackService());
        var view = new FeedCardView { DataContext = first, Width = 220 };
        var window = new Window { Width = 1000, Height = 800, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var card = view.FindControl<Button>("Card")!;
            window.MouseMove(card.TranslatePoint(new Point(40, 40), window)!.Value);
            Dispatcher.UIThread.RunJobs();
            var flyout = Assert.IsType<Flyout>(FlyoutBase.GetAttachedFlyout(card));
            Assert.True(flyout.IsOpen);
            if (rebind) view.DataContext = second;
            else window.Content = null;
            Dispatcher.UIThread.RunJobs();
            Assert.False(flyout.IsOpen);
            Assert.False(first.IsFocusWithin);
            Assert.False(first.IsPointerOver);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Escape_closes_quick_details_and_returns_focus_to_the_card()
    {
        using var model = new FeedCardViewModel(TileFixture.Tile(DateTime.UtcNow), "A reason", new FeedbackService());
        var view = new FeedCardView { DataContext = model, Width = 220 };
        var elsewhere = new Button { Content = "Elsewhere" };
        var window = new Window { Width = 1000, Height = 800,
            Content = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Children = { view, elsewhere } } };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var card = view.FindControl<Button>("Card")!;
            Assert.True(card.Focus(NavigationMethod.Tab));
            window.MouseMove(card.TranslatePoint(new Point(40, 40), window)!.Value);
            Dispatcher.UIThread.RunJobs();
            var flyout = Assert.IsType<Flyout>(FlyoutBase.GetAttachedFlyout(card));
            Assert.True(flyout.IsOpen);
            Assert.True(model.IsFocusWithin);
            Assert.True(model.IsCountdownHeld);

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.False(flyout.IsOpen);
            Assert.True(card.IsFocused);
            Assert.True(elsewhere.Focus(NavigationMethod.Tab));
            Dispatcher.UIThread.RunJobs();
            Assert.False(model.IsFocusWithin);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Hover_preview_opens_immediately_and_closes_on_exit_even_over_the_preview(bool overPreview)
    {
        var opened = 0;
        var metadataRequests = 0;
        CancellationToken metadataCancellation = default;
        var source = TileFixture.Tile(DateTime.UtcNow, steamAppId: "123");
        var tile = new GameTileViewModel(source.Entries, source.Game, source.Title, DateTime.UtcNow)
        {
            LoadBackdropImages = token =>
            {
                metadataRequests++;
                metadataCancellation = token;
                return new TaskCompletionSource<IReadOnlyList<WorkImages>>().Task;
            },
            OpenDetailsCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => opened++),
        };
        using var model = new FeedCardViewModel(tile, "A reason kept beneath the cover", new FeedbackService());
        var view = new FeedCardView { DataContext = model, Width = 220,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var window = new Window { Width = 1100, Height = 800, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, metadataRequests);
            var card = view.FindControl<Button>("Card")!;
            var flyout = Assert.IsType<Flyout>(FlyoutBase.GetAttachedFlyout(card));
            window.MouseMove(card.TranslatePoint(new Point(40, 40), window)!.Value);
            Dispatcher.UIThread.RunJobs();
            Assert.True(flyout.IsOpen);
            Assert.Equal(1, metadataRequests);
            Assert.False(card.IsFocused);
            var content = Assert.IsAssignableFrom<Control>(flyout.Content);
            var popup = TopLevel.GetTopLevel(content)!;
            Assert.Empty(content.GetVisualDescendants().OfType<Button>());
            Assert.DoesNotContain(content.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "WHY THIS GAME" || text.Text == model.Reason);
            Assert.True(model.IsCountdownHeld);
            if (overPreview)
                popup.MouseMove(content.TranslatePoint(new Point(40, 40), popup)!.Value);
            else
                window.MouseMove(new Point(1000, 700));
            Dispatcher.UIThread.RunJobs();
            Assert.False(flyout.IsOpen);
            Assert.Equal(0, opened);
            Assert.True(metadataCancellation.IsCancellationRequested);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Hovering_another_card_replaces_the_preview_without_a_click()
    {
        using var first = new FeedCardViewModel(TileFixture.Tile(DateTime.UtcNow), "First", new FeedbackService());
        using var second = new FeedCardViewModel(TileFixture.Tile(DateTime.UtcNow), "Second", new FeedbackService());
        var firstView = new FeedCardView { DataContext = first, Width = 220 };
        var secondView = new FeedCardView { DataContext = second, Width = 220 };
        var window = new Window { Width = 1400, Height = 800,
            Content = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 440, Children = { firstView, secondView } } };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var firstCard = firstView.FindControl<Button>("Card")!;
            var secondCard = secondView.FindControl<Button>("Card")!;
            var firstFlyout = Assert.IsType<Flyout>(FlyoutBase.GetAttachedFlyout(firstCard));
            var secondFlyout = Assert.IsType<Flyout>(FlyoutBase.GetAttachedFlyout(secondCard));
            window.MouseMove(firstCard.TranslatePoint(new Point(40, 40), window)!.Value);
            Dispatcher.UIThread.RunJobs();
            Assert.True(firstFlyout.IsOpen);
            window.MouseMove(secondCard.TranslatePoint(new Point(40, 40), window)!.Value);
            Dispatcher.UIThread.RunJobs();
            Assert.True(secondFlyout.IsOpen);
            Assert.False(firstFlyout.IsOpen);
        }
        finally { window.Close(); }
    }

    private static void Click(TopLevel window, Button button, Point? localPoint = null)
    {
        var point = button.TranslatePoint(localPoint ?? new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class FeedbackService : IFeedService
    {
        public Task<FeedSnapshot> GetShelvesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task RecordSurfacedAsync(long releaseId, string shelfId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<FeedVerdictOutcome> RecordVerdictAsync(long releaseId, FeedVerdictKind kind, CancellationToken ct = default)
            => Task.FromResult(new FeedVerdictOutcome(true, null));
        public Task<bool> RevokeVerdictAsync(long releaseId, FeedVerdictKind kind, CancellationToken ct = default)
            => Task.FromResult(true);
        public Task<IReadOnlyList<FeedVerdictRecord>> GetHistoryAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<FeedVerdictRecord>>([]);
    }
}
