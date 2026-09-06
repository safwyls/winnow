using System.Reflection;
using Avalonia;
using Avalonia.Automation;
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
    [InlineData(200)]
    public async Task Hover_reveals_fixed_compact_actions(double width)
    {
        using var fixture = await CardFixture.CreateAsync(width, reducedMotion: false);
        var actions = fixture.TileView.FindControl<Border>("TileActions")!;
        var primary = fixture.Button("Play");
        var details = fixture.Button("Details");
        var primaryOrigin = primary.TranslatePoint(default, actions)!.Value;
        var detailsOrigin = details.TranslatePoint(default, actions)!.Value;

        Assert.Equal(0, actions.Opacity);
        Assert.False(actions.IsHitTestVisible);

        fixture.Window.MouseMove(fixture.Position(fixture.TileView, new Point(width / 2, 80)));
        Flush();
        await Task.Delay(180);
        Flush();

        Assert.Equal(1, actions.Opacity);
        Assert.True(actions.IsHitTestVisible);
        Assert.Equal(new Size(32, 32), primary.Bounds.Size);
        Assert.Equal(new Size(32, 32), details.Bounds.Size);
        Assert.Equal(primaryOrigin, primary.TranslatePoint(default, actions)!.Value);
        Assert.Equal(detailsOrigin, details.TranslatePoint(default, actions)!.Value);
        Assert.Equal("Launch through Steam", ToolTip.GetTip(primary));
        Assert.Equal("Full details", ToolTip.GetTip(details));
    }

    [AvaloniaTheory]
    [InlineData(108, true, false)]
    [InlineData(108, true, true)]
    [InlineData(108, false, true)]
    [InlineData(148, true, true)]
    [InlineData(200, false, true)]
    public async Task Hover_stats_and_store_chips_do_not_overlap(double width, bool singleStore, bool played)
    {
        using var fixture = await CardFixture.CreateAsync(width, singleStore: singleStore, played: played);
        fixture.Window.MouseMove(fixture.Position(fixture.TileView, new Point(width / 2, 30)));
        await Task.Delay(200);
        Flush();
        var scrim = fixture.TileView.FindControl<Border>("Scrim")!;
        var stat = scrim.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == fixture.Tile.StatText);
        var statOrigin = stat.TranslatePoint(default, scrim)!.Value;
        var textBounds = new Rect(statOrigin, new Size(stat.TextLayout.Width, stat.TextLayout.Height));
        foreach (var chip in scrim.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("store-chip")))
        {
            var chipBounds = new Rect(chip.TranslatePoint(default, scrim)!.Value, chip.Bounds.Size);
            Assert.False(textBounds.Intersects(chipBounds),
                $"Stat {textBounds} overlaps chip {chipBounds} at width {width}");
        }
        Assert.True(scrim.Bounds.Height <= fixture.TileView.Bounds.Height);
        if (Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is { } directory)
        {
            Directory.CreateDirectory(directory);
            using var frame = fixture.Window.CaptureRenderedFrame();
            frame!.Save(Path.Combine(directory, $"card-hover-{width}-{singleStore}-{played}.png"));
        }
    }

    [AvaloniaFact]
    public async Task Keyboard_focus_reveals_the_action_dock()
    {
        using var fixture = await CardFixture.CreateAsync(148);
        fixture.Window.MouseMove(new Point(600, 400));
        Flush();
        var actions = fixture.TileView.FindControl<Border>("TileActions")!;
        var details = fixture.Button("Details");

        Assert.Equal(0, actions.Opacity);
        Assert.True(details.Focus(NavigationMethod.Tab));
        Flush();

        Assert.True(details.IsKeyboardFocusWithin);
        Assert.Equal(1, actions.Opacity);
        Assert.True(actions.IsHitTestVisible);
    }

    [AvaloniaFact]
    public async Task Off_disk_copy_uses_the_install_glyph_and_name()
    {
        using var fixture = await CardFixture.CreateAsync(148, installed: false);

        Assert.False(fixture.TileView.FindControl<Avalonia.Controls.Shapes.Path>("PlayGlyph")!.IsVisible);
        Assert.True(fixture.TileView.FindControl<Avalonia.Controls.Shapes.Path>("InstallGlyph")!.IsVisible);
        Assert.Equal("Install", AutomationProperties.GetName(fixture.Button("Install")));
    }

    [AvaloniaTheory]
    [InlineData(108)]
    [InlineData(148)]
    public async Task Details_icon_owns_its_hit_area_and_opens_after_each_reopen(double width)
    {
        using var fixture = await CardFixture.CreateAsync(width);
        fixture.Window.MouseMove(fixture.Position(fixture.TileView, new Point(width / 2, 80)));
        Flush();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var button = fixture.Button("Details");
            for (var frame = 0; frame < 100 && !button.IsEffectivelyEnabled; frame++)
            {
                await Task.Delay(10);
                Flush();
            }
            Assert.True(button.IsEffectivelyEnabled);

            for (var y = 3; y < button.Bounds.Height - 3; y += 4)
            for (var x = 3; x < button.Bounds.Width - 3; x += 4)
            {
                var point = new Point(x, y);
                var hit = fixture.Window.InputHitTest(fixture.Position(button, point)) as Control;
                Assert.True(ReferenceEquals(button, hit?.FindAncestorOfType<Button>(includeSelf: true)),
                    $"Attempt {attempt}, button point {point} hit {hit} ({hit?.Name}) instead of Details.");
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

    [AvaloniaFact]
    public async Task Repeated_primary_clicks_and_keyboard_activation_belong_to_the_button()
    {
        using var fixture = await CardFixture.CreateAsync(108);
        var presses = 0;
        fixture.Tile.PrimaryActionCommand = new RelayCommand(() => presses++);
        // Commands are assigned before a container sees its tile in the real library too.
        fixture.TileView.DataContext = null;
        fixture.TileView.DataContext = fixture.Tile;
        fixture.Window.MouseMove(fixture.Position(fixture.TileView, new Point(54, 80)));
        Flush();
        var button = fixture.Button("Play");

        fixture.Click(button);
        fixture.Click(button);
        Assert.Equal(2, presses);
        Assert.False(fixture.Library.IsDetailsOpen);

        Assert.True(button.Focus(NavigationMethod.Tab));
        fixture.Window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        fixture.Window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
        Flush();
        Assert.Equal(3, presses);
        Assert.False(fixture.Library.IsDetailsOpen);
    }

    [AvaloniaFact]
    public async Task Non_control_double_click_opens_details_but_icon_press_only_runs_its_action()
    {
        using var fixture = await CardFixture.CreateAsync(108);
        var presses = 0;
        fixture.Tile.PrimaryActionCommand = new RelayCommand(() => presses++);
        fixture.TileView.DataContext = null;
        fixture.TileView.DataContext = fixture.Tile;
        var coverPoint = fixture.Position(fixture.TileView, new Point(54, 80));

        fixture.Window.MouseMove(coverPoint);
        fixture.Window.MouseDown(coverPoint, MouseButton.Left);
        fixture.Window.MouseUp(coverPoint, MouseButton.Left);
        Flush();
        Assert.Same(fixture.Tile, fixture.Library.SelectedTile);
        Assert.False(fixture.Library.IsDetailsOpen);

        fixture.Click(fixture.Button("Play"));
        Assert.Equal(1, presses);
        Assert.False(fixture.Library.IsDetailsOpen);

        fixture.Window.MouseMove(coverPoint);
        fixture.Window.MouseDown(coverPoint, MouseButton.Left);
        fixture.Window.MouseUp(coverPoint, MouseButton.Left);
        fixture.Window.MouseDown(coverPoint, MouseButton.Left);
        fixture.Window.MouseUp(coverPoint, MouseButton.Left);
        if (fixture.Library.OpenDetailsCommand.ExecutionTask is { } opened) await opened;
        Flush();
        Assert.True(fixture.Library.IsDetailsOpen);
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

        public static async Task<CardFixture> CreateAsync(
            double width,
            bool reducedMotion = true,
            bool singleStore = false,
            bool played = false,
            bool installed = true)
        {
            var fixture = new CardFixture();
            var works = new WorkRepository(fixture._database.Factory);
            var releases = new ReleaseRepository(fixture._database.Factory);
            var ownerships = new OwnershipRepository(fixture._database.Factory);
            var work = await works.InsertAsync(new Work { Name = "A deliberately long game title" });
            var release = await releases.InsertAsync(new Release { WorkId = work, Name = "Fixture" });
            var ownership = await ownerships.InsertAsync(new Ownership { ReleaseId = release, Store = "steam", Installed = installed });
            fixture.Library = new LibraryViewModel(
                new LibraryQueryRepository(fixture._database.Factory), ownerships, releases, works,
                new UpdateEventRepository(fixture._database.Factory));
            await fixture.Library.LoadCommand.ExecuteAsync(null);
            var entries = new[]
                {
                    TileEntry.For(ownership, release, work, "steam", played ? 740700 : 0, played ? DateTime.UtcNow.AddYears(-10) : null,
                        ownership: new Ownership { ReleaseId = release, Store = "steam", Installed = installed }, steamAppId: "80"),
                    TileEntry.For(ownership + 1, release, work, "gog", 0, null),
                    TileEntry.For(ownership + 2, release, work, "epic", 0, null),
                };
            fixture.Tile = TileFixture.Tile(DateTime.UtcNow,
                singleStore ? entries[..1] : entries, work, played ? LibraryBuckets.Retired : LibraryBuckets.NeverPlayed,
                title: "A deliberately long game title occupying two lines",
                work: new Work { Name = "Fixture", FirstReleaseYear = 2006 },
                ramp: new DormancyRamp { ReducedMotion = reducedMotion });
            fixture.Tile.OpenDetailsCommand = fixture.Library.OpenDetailsCommand;
            fixture.Tile.PrimaryActionCommand = new RelayCommand(() => { });
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

        public Button Button(string name) => TileView.GetVisualDescendants().OfType<Button>()
            .Single(button => AutomationProperties.GetName(button) == name);
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
