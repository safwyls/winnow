using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class CardDetailsInteractionTests
{
    [AvaloniaTheory]
    [InlineData(108)]
    [InlineData(148)]
    public async Task Details_owns_its_hit_area_and_opens_after_each_reopen(double width)
    {
        using var fixture = await CardFixture.CreateAsync(width);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            fixture.Library.FlipTileCommand.Execute(fixture.Tile);
            await Task.Delay(200);
            Flush();
            var button = fixture.Button("Details");
            // AsyncRelayCommand posts its completion notification separately from ExecutionTask.
            // A reopened card is settled once that notification has re-enabled its button.
            for (var frame = 0; frame < 100 && !button.IsEffectivelyEnabled; frame++)
            {
                await Task.Delay(10);
                Flush();
            }
            Assert.True(button.IsEffectivelyEnabled);

            // At 108px the year and wrapped store chips used to cover the button.
            // Leave its rounded outer edge out of the hit-target walk.
            for (var y = 3; y < button.Bounds.Height - 3; y += 4)
            for (var x = 3; x < button.Bounds.Width - 3; x += 4)
            {
                var point = new Point(x, y);
                var hit = fixture.Window.InputHitTest(fixture.Position(button, point)) as Control;
                Assert.True(ReferenceEquals(button, hit?.FindAncestorOfType<Button>(includeSelf: true)),
                    $"Attempt {attempt}, button point {point} hit {hit} ({hit?.Name}) instead of Details.");
            }

            if (attempt == 0 && Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is { } directory)
            {
                Directory.CreateDirectory(directory);
                using var frame = fixture.Window.CaptureRenderedFrame();
                frame!.Save(Path.Combine(directory, $"card-back-{width}.png"));
            }

            fixture.Click(button, new Point(9, 9));
            if (fixture.Library.OpenDetailsCommand.ExecutionTask is { } opened)
            {
                await opened;
            }
            Flush();
            Assert.Same(fixture.Tile, fixture.Library.Details?.Tile);
            Assert.True(fixture.Details.IsEffectivelyVisible);
            fixture.Library.CloseDetailsCommand.Execute(null);
            Flush();
            Assert.False(fixture.Details.IsVisible);
        }
    }

    [AvaloniaTheory]
    [InlineData("Add to list")]
    [InlineData("Play")]
    public async Task Repeated_action_clicks_and_keyboard_activation_belong_to_the_button(string action)
    {
        using var fixture = await CardFixture.CreateAsync(108);
        var presses = 0;
        fixture.Tile.AddToListCommand = new RelayCommand(() => presses++);
        fixture.Tile.PrimaryActionCommand = new RelayCommand(() => presses++);
        // Commands are assigned before a container sees its tile in the real library too.
        fixture.TileView.DataContext = null;
        fixture.TileView.DataContext = fixture.Tile;
        fixture.Library.FlipTileCommand.Execute(fixture.Tile);
        Flush();
        var button = fixture.Button(action);

        fixture.Click(button);
        fixture.Click(button);
        Assert.Equal(2, presses);
        Assert.True(fixture.Tile.IsFlipped);
        Assert.False(fixture.Library.IsDetailsOpen);

        Assert.True(button.Focus(NavigationMethod.Tab));
        fixture.Window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        fixture.Window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
        Flush();
        Assert.Equal(3, presses);
        Assert.False(fixture.Library.IsDetailsOpen);
    }

    [AvaloniaFact]
    public async Task Scrolling_card_metadata_does_not_take_the_action_buttons_or_flip_the_card()
    {
        using var fixture = await CardFixture.CreateAsync(108);
        fixture.Library.FlipTileCommand.Execute(fixture.Tile);
        Flush();
        var scroll = Assert.Single(fixture.TileView.GetVisualDescendants().OfType<ScrollViewer>());
        Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
        var button = fixture.Button("Details");
        var position = fixture.Position(button, default);
        var point = fixture.Position(scroll, new Point(12, 12));
        fixture.Window.MouseWheel(point, new Vector(0, -3), RawInputModifiers.None);
        Flush();
        Assert.True(scroll.Offset.Y > 0);
        Assert.Equal(position, fixture.Position(button, default));

        var bar = scroll.GetVisualDescendants().OfType<ScrollBar>()
            .Single(b => b.Orientation == Avalonia.Layout.Orientation.Vertical);
        fixture.Window.MouseMove(fixture.Position(bar, new Point(bar.Bounds.Width / 2, bar.Bounds.Height / 2)));
        Flush();
        var thumb = Assert.Single(bar.GetVisualDescendants().OfType<Thumb>());
        var thumbPoint = fixture.Position(thumb, new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2));
        fixture.Window.MouseDown(thumbPoint, MouseButton.Left);
        fixture.Window.MouseUp(thumbPoint, MouseButton.Left);
        fixture.Window.MouseDown(thumbPoint, MouseButton.Left);
        fixture.Window.MouseUp(thumbPoint, MouseButton.Left);
        Flush();
        Assert.True(fixture.Tile.IsFlipped);
        Assert.False(fixture.Library.IsDetailsOpen);
    }

    private static void Flush()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class CardFixture : IDisposable
    {
        private readonly TempDatabase _database = new();
        public Window Window { get; } = new() { Width = 1200, Height = 640 };
        public GameDetailsView Details { get; } = new() { IsVisible = false };
        public LibraryViewModel Library { get; private set; } = null!;
        public GameTileViewModel Tile { get; private set; } = null!;
        public GameTileView TileView { get; private set; } = null!;

        public static async Task<CardFixture> CreateAsync(double width)
        {
            var fixture = new CardFixture();
            var works = new WorkRepository(fixture._database.Factory);
            var releases = new ReleaseRepository(fixture._database.Factory);
            var ownerships = new OwnershipRepository(fixture._database.Factory);
            var work = await works.InsertAsync(new Work { Name = "A deliberately long game title" });
            var release = await releases.InsertAsync(new Release { WorkId = work, Name = "Fixture" });
            var ownership = await ownerships.InsertAsync(new Ownership { ReleaseId = release, Store = "steam", Installed = true });
            fixture.Library = new LibraryViewModel(
                new LibraryQueryRepository(fixture._database.Factory), ownerships, releases, works,
                new UpdateEventRepository(fixture._database.Factory));
            await fixture.Library.LoadCommand.ExecuteAsync(null);
            fixture.Tile = TileFixture.Tile(DateTime.UtcNow,
                [
                    TileEntry.For(ownership, release, work, "steam", 0, null,
                        ownership: new Ownership { ReleaseId = release, Store = "steam", Installed = true }, steamAppId: "80"),
                    TileEntry.For(ownership + 1, release, work, "gog", 0, null),
                    TileEntry.For(ownership + 2, release, work, "epic", 0, null),
                ], work, LibraryBuckets.NeverPlayed,
                title: "A deliberately long game title occupying two lines",
                work: new Work { Name = "Fixture", FirstReleaseYear = 2006 },
                ramp: new DormancyRamp { ReducedMotion = true });
            fixture.Tile.OpenDetailsCommand = fixture.Library.OpenDetailsCommand;
            var wall = new CoverWall
            {
                Width = width, MinCellWidth = width, Margin = new Thickness(40),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                ItemsSource = new[] { fixture.Tile },
                ItemTemplate = new FuncDataTemplate<GameTileViewModel>((tile, _) => new GameTileView { DataContext = tile }),
            };
            // Bind the same handler the shell uses without constructing unrelated shell popups.
            var handler = typeof(MainWindow).GetMethod("HandleTilePressed", BindingFlags.NonPublic | BindingFlags.Static)!;
            var handlePress = (Action<LibraryViewModel?, PointerPressedEventArgs>)handler.CreateDelegate(
                typeof(Action<LibraryViewModel?, PointerPressedEventArgs>));
            wall.AddHandler(InputElement.PointerPressedEvent,
                (_, e) => handlePress(fixture.Library, e),
                RoutingStrategies.Tunnel);
            fixture.Library.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(LibraryViewModel.Details)) return;
                fixture.Details.DataContext = fixture.Library.Details;
                fixture.Details.IsVisible = fixture.Library.IsDetailsOpen;
            };
            fixture.Details.CloseRequested += (_, _) => fixture.Library.CloseDetailsCommand.Execute(null);
            fixture.Window.Content = new Panel { Children = { wall, fixture.Details } };
            fixture.Window.Show();
            Flush();
            fixture.TileView = Assert.Single(wall.GetVisualDescendants().OfType<GameTileView>());
            return fixture;
        }

        public Button Button(string text) => TileView.GetVisualDescendants().OfType<Button>()
            .Single(button => Equals(button.Content, text));
        public Point Position(Control control, Point point) => control.TranslatePoint(point, Window)!.Value;
        public void Click(Button button, Point? point = null)
        {
            var position = Position(button, point ?? new Point(button.Bounds.Width / 2, button.Bounds.Height / 2));
            Window.MouseMove(position);
            Window.MouseDown(position, MouseButton.Left);
            Window.MouseUp(position, MouseButton.Left);
            Flush();
        }
        public void Dispose()
        {
            Window.Close();
            _database.Dispose();
        }
    }
}
