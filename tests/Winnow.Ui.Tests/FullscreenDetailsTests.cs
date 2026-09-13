using Avalonia.Controls;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views.Fullscreen;
using Winnow.Tests;
using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Covers;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class FullscreenDetailsTests
{
    [AvaloniaTheory]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(3, true)]
    public void Detail_sections_wrap_content_and_keep_controller_actions_reachable(int section, bool populated)
    {
        var now = DateTime.UtcNow;
        using var database = new TempDatabase();
        var journal = new GameJournalViewModel(populated
            ? Enumerable.Range(1, 4).Select(index => new SessionJournalEntry { SessionId = index, OwnershipId = 1,
                SessionAt = now.AddDays(-index), Rating = index == 2 ? null : 4,
                Note = index == 3 ? null : "Follow the mountain path to the abandoned observatory, then return to the village and speak to the cartographer about the missing expedition." })
            : [], true, new Winnow.Data.Repositories.SessionRepository(database.Factory));
        var updates = populated ? Enumerable.Range(1, 4).Select(index => UpdateEventViewModel.Create(new UpdateEvent
        {
            ReleaseId = 1, Kind = UpdateEventKinds.Announcement, OccurredAt = now.AddDays(-index),
            Title = "Expedition update: new mountain regions, companion quests, controller improvements and fixes for the observatory campaign"
        }, now.AddDays(-10), 120)).ToArray() : [];
        var listEntry = new Winnow.App.ViewModels.Lists.GameListEntryViewModel(
            new Winnow.App.ViewModels.Lists.GameListViewModel(new GameList { Id = 1,
                Name = "Long adventures to revisit after finishing the mountain expedition with friends" }), [], (_, _) => Task.CompletedTask);
        var lists = new Winnow.App.ViewModels.Lists.GameListsViewModel(populated ? [listEntry] : []);
        var expansions = new GameExpansionsViewModel(populated
            ? [new ExpansionRowViewModel(1, 2, "The mountain expedition: journeys beyond the abandoned observatory", ["STEAM"], "Steam", 120, now.AddDays(-5))]
            : []);
        using var details = new GameDetailsViewModel(TileFixture.Tile(now, title: "Mountain expedition"), "Never played", updates, now,
            journal: journal, lists: lists, expansions: expansions,
            addToList: new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }));
        var library = new LibraryViewModel(new PreviewLibraryQueryRepository(), new PreviewOwnershipRepository(),
            new PreviewReleaseRepository(), new PreviewWorkRepository(), new PreviewUpdateEventRepository());
        using var context = new FullscreenContext(library, new FeedViewModel(new PreviewFeedService(), library), PreviewData.Shell);
        context.SafeMarginPercent = 3;
        using var view = new FullscreenView(context);
        context.Push(new FullscreenDetailsPage(context, details, section));
        var window = new Window { Width = 2560, Height = 1440, Content = view };
        window.Show();
        try
        {
            foreach (var enlarged in new[] { false, true })
            {
                context.TextScale = enlarged ? 1.4 : 1;
                context.UiScale = enlarged ? 1.2 : 1;
                window.Width = enlarged ? 1280 : 2560;
                window.Height = enlarged ? 720 : 1440;
                Dispatcher.UIThread.RunJobs();
                var page = Assert.IsType<FullscreenDetailsPage>(view.CurrentPage);
                var scroll = Assert.Single(page.GetVisualDescendants().OfType<ScrollViewer>());
                var body = Assert.IsAssignableFrom<Control>(scroll.Content);
                Assert.True(scroll.Bounds.Height > 100);
                foreach (var block in body.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible))
                {
                    var point = block.TranslatePoint(default, body)!.Value;
                    Assert.True(point.X >= -1 && point.X + block.Bounds.Width <= body.Bounds.Width + 1,
                        $"Section {section}: '{block.Text}' overflows horizontally ({point.X} + {block.Bounds.Width} > {body.Bounds.Width}).");
                }
                var actions = body.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.IsEffectivelyVisible && button.IsEffectivelyEnabled).ToArray();
                var visited = new HashSet<Button>();
                // Start at the first section tab, then traverse the same explicit action rows as a controller.
                var tabs = Assert.IsType<Grid>(page.Content).Children[1].GetVisualDescendants().OfType<Button>().ToArray();
                tabs[0].Focus();
                for (var index = 0; index <= actions.Length; index++)
                {
                    page.Handle(GamepadButtons.Down);
                    Dispatcher.UIThread.RunJobs();
                    if (window.FocusManager!.GetFocusedElement() is not Button focused || !actions.Contains(focused)) continue;
                    visited.Add(focused);
                    var point = focused.TranslatePoint(default, scroll)!.Value;
                    Assert.True(point.Y < scroll.Bounds.Height && point.Y + focused.Bounds.Height > 0,
                        $"Section {section}: focused action is outside the viewport.");
                    if (focused.Bounds.Height <= scroll.Bounds.Height)
                    {
                        Assert.True(point.Y >= -1 && point.Y + focused.Bounds.Height <= scroll.Bounds.Height + 1,
                            $"Section {section}: focused action clips at {point.Y} with height {focused.Bounds.Height}, viewport {scroll.Bounds.Height}.");
                    }
                }
                Assert.All(actions, action => Assert.Contains(action, visited));
                if (section == 3 && populated)
                {
                    var membership = Link(page, listEntry.AutomationName);
                    foreach (var selected in new[] { true, false })
                    {
                        membership.Focus();
                        page.Handle(GamepadButtons.Accept);
                        Dispatcher.UIThread.RunJobs();
                        Assert.Equal(selected, listEntry.IsMember);
                        Assert.Same(membership, Link(page, listEntry.AutomationName));
                        Assert.Contains(membership.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == listEntry.SelectionLabel);
                        Assert.Same(membership, window.FocusManager!.GetFocusedElement());
                    }
                }
                if (!populated && section == 2)
                    Assert.Contains(body.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == journal.EmptyText);
                if (populated)
                    Assert.Contains(body.GetVisualDescendants().OfType<Border>(), border => border.Name == "FullscreenDetailsHorizontalRule");
                if (Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is { } directory)
                {
                    Directory.CreateDirectory(directory);
                    scroll.Offset = default;
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var frame = window.CaptureRenderedFrame();
                    frame!.Save(Path.Combine(directory, $"fullscreen-details-tab-{section}-{(populated ? "populated" : "empty")}-{(enlarged ? "140" : "100")}.png"));
                }
            }
        }
        finally { window.Close(); }
    }

    private static Button Link(Control root, string name) => Assert.Single(root.GetVisualDescendants().OfType<Button>(),
        button => Avalonia.Automation.AutomationProperties.GetName(button) == name);

    private static void AssertCompleteScreenshotEdges(Image image)
    {
        var width = (int)Math.Ceiling(image.Bounds.Width);
        var height = (int)Math.Ceiling(image.Bounds.Height);
        using var rendered = new RenderTargetBitmap(new PixelSize(width, height));
        rendered.Render(image);
        var pixels = new byte[width * height * 4];
        var pin = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
        try { rendered.CopyPixels(new PixelRect(rendered.PixelSize), pin.AddrOfPinnedObject(), pixels.Length, width * 4); }
        finally { pin.Free(); }
        AssertPixel(width / 2, 2, [0, 0, 255, 255]);
        AssertPixel(width / 2, height - 3, [0, 255, 0, 255]);
        AssertPixel(2, height / 2, [255, 0, 0, 255]);
        AssertPixel(width - 3, height / 2, [0, 255, 255, 255]);

        void AssertPixel(int x, int y, byte[] expected)
        {
            var offset = (y * width + x) * 4;
            Assert.Equal(expected, pixels[offset..(offset + 4)]);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Overview_previews_only_the_latest_saved_note_and_omits_missing_content(bool hasNotes)
    {
        var now = DateTime.UtcNow;
        using var database = new TempDatabase();
        var journal = new GameJournalViewModel(hasNotes
            ? [new SessionJournalEntry { SessionId = 1, OwnershipId = 1, SessionAt = now.AddDays(-2), Note = "An older saved note." },
                new SessionJournalEntry { SessionId = 2, OwnershipId = 1, SessionAt = now.AddDays(-1), Note = "Return to the mountain camp." }]
            : [], true, new Winnow.Data.Repositories.SessionRepository(database.Factory));
        using var details = new GameDetailsViewModel(TileFixture.Tile(now), "Never played", [], now, journal: journal);
        using var page = new FullscreenDetailsPage(null!, details);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var text = page.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text).ToArray();
            Assert.Equal(hasNotes, text.Any(value => value?.Contains("Return to the mountain camp.") == true));
            Assert.DoesNotContain(text, value => value?.Contains("An older saved note.") == true);
            Assert.DoesNotContain(text, value => value == journal.EmptyText);
            Assert.DoesNotContain(page.GetVisualDescendants().OfType<Grid>(), grid => grid.Name == "FullscreenOverviewScreenshots");
            Assert.DoesNotContain(text, value => value == "RECEPTION");
            Assert.Contains(details.EmptyBodyText, text);
            var openJournal = Link(page, "Open journal");
            Assert.Contains(hasNotes ? "LATEST NOTE" : "JOURNAL", text);
            openJournal.Focus();
            page.Handle(GamepadButtons.Accept);
            Assert.Equal(2, page.SelectedSection);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Overview_controller_moves_between_actions_and_the_screenshot_row(int screenshotCount)
    {
        var now = DateTime.UtcNow;
        using var details = new GameDetailsViewModel(TileFixture.Tile(now, title: "Across the room"), "Started", [], now,
            images: [new WorkImages { WorkId = 1, Source = ImageSources.Igdb, Kind = ImageKinds.Screenshot,
                ImageIds = string.Join(",", Enumerable.Range(1, screenshotCount).Select(index => $"shot{index}")), ObservedAt = now }]);
        var page = new FullscreenDetailsPage(null!, details);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var buttons = page.GetVisualDescendants().OfType<Button>().ToArray();
            var history = Link(page, "Play history");
            var about = Link(page, "Read more");
            var journal = Link(page, "Open journal");
            var screenshots = buttons.Where(button => button.Content is FullscreenScreenshotPreview).ToArray();
            Assert.Equal(screenshotCount, screenshots.Length);
            page.FocusInitial();
            var primary = window.FocusManager!.GetFocusedElement();
            Assert.IsType<Button>(primary);
            page.Handle(GamepadButtons.Down);
            var tab = window.FocusManager.GetFocusedElement();
            page.Handle(GamepadButtons.Right);
            var focusedTab = Assert.IsType<Button>(window.FocusManager.GetFocusedElement());
            Assert.Equal(0, page.SelectedSection);
            Assert.DoesNotContain("current", focusedTab.Classes);
            Assert.Equal(0, Assert.IsAssignableFrom<ISolidColorBrush>(focusedTab.BorderBrush).Color.A);
            Assert.True(Assert.IsAssignableFrom<ISolidColorBrush>(Assert.IsType<Button>(tab).BorderBrush).Color.A > 0);
            page.Handle(GamepadButtons.Left);
            page.Handle(GamepadButtons.Down);
            Assert.Same(history, window.FocusManager.GetFocusedElement());
            page.Handle(GamepadButtons.Right);
            Assert.Same(about, window.FocusManager.GetFocusedElement());
            if (screenshots.Length > 0)
            {
                page.Handle(GamepadButtons.Down);
                var gallery = Link(page, "View gallery");
                Assert.Contains(window.FocusManager.GetFocusedElement(), screenshots);
                screenshots[0].Focus();
                foreach (var screenshot in screenshots.Skip(1))
                {
                    page.Handle(GamepadButtons.Right);
                    Assert.Same(screenshot, window.FocusManager.GetFocusedElement());
                }
                page.Handle(GamepadButtons.Right);
                Assert.Same(screenshots[^1], window.FocusManager.GetFocusedElement());
                page.Handle(GamepadButtons.Down);
                Assert.Same(gallery, window.FocusManager.GetFocusedElement());
                page.Handle(GamepadButtons.Up);
                Assert.Contains(window.FocusManager.GetFocusedElement(), screenshots.Cast<Control>().Prepend(journal));
                page.Handle(GamepadButtons.Up);
                Assert.Contains(window.FocusManager.GetFocusedElement(), new[] { history, about });
            }
            else
            {
                page.Handle(GamepadButtons.Down);
                Assert.Same(journal, window.FocusManager.GetFocusedElement());
            }
            history.Focus();
            Assert.Same(history, window.FocusManager.GetFocusedElement());
            page.Handle(GamepadButtons.Up);
            Assert.Same(tab, window.FocusManager.GetFocusedElement());
            page.Handle(GamepadButtons.Up);
            Assert.Same(primary, window.FocusManager.GetFocusedElement());
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(true, false, false, 1)]
    [InlineData(false, false, false, 1)]
    [InlineData(true, true, false, 1)]
    [InlineData(true, false, true, 1)]
    [InlineData(true, false, false, .8)]
    [InlineData(true, false, true, .8)]
    public void Cinematic_details_lease_saved_landscape_across_the_canvas_and_release_on_close(bool userBackground, bool longTitle, bool hasJournal, double uiScale)
    {
        // An original geometric landscape exercises real bitmap decoding/display without live library art.
        using var pixels = new RenderTargetBitmap(new PixelSize(1600, 900));
        using (var draw = pixels.CreateDrawingContext())
        {
            draw.DrawRectangle(new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = [new GradientStop(Color.Parse("#203E60"), 0), new GradientStop(Color.Parse("#D7A37C"), .55),
                    new GradientStop(Color.Parse("#17434B"), 1)]
            }, null, new Rect(0, 0, 1600, 900));
            draw.DrawEllipse(Brushes.Wheat, null, new Point(1200, 210), 70, 70);
            draw.DrawGeometry(new SolidColorBrush(Color.Parse("#244954")), null,
                Geometry.Parse("M 0,650 L 350,290 L 610,570 L 990,180 L 1420,560 L 1600,390 L 1600,900 L 0,900 Z"));
            draw.DrawGeometry(new SolidColorBrush(Color.Parse("#102D32")), null,
                Geometry.Parse("M 0,760 L 260,550 L 600,760 L 1050,510 L 1330,700 L 1600,580 L 1600,900 L 0,900 Z"));
            // All four source edges must survive preview rendering; panoramic cropping loses these bands.
            draw.DrawRectangle(Brushes.Red, null, new Rect(0, 0, 1600, 24));
            draw.DrawRectangle(Brushes.Lime, null, new Rect(0, 876, 1600, 24));
            draw.DrawRectangle(Brushes.Blue, null, new Rect(0, 24, 24, 852));
            draw.DrawRectangle(Brushes.Yellow, null, new Rect(1576, 24, 24, 852));
        }
        var leases = new DetailLeases(new CoverArt(pixels, pixels));
        var work = new Work { Id = 1, Name = longTitle ? "A distant shore: the journey beyond the mountains and the forgotten coast" : "A distant shore",
            Summary = string.Join(" ", Enumerable.Repeat("Explore the mountain coast and find your way home.", 12)),
            BackgroundUrl = userBackground ? UserArtRef.Format("landscape") : null };
        var images = new DetailImages();
        using var services = new ServiceCollection().AddSingleton<ICoverLeases>(leases)
            .AddSingleton<IWorkRepository>(new DetailWorks(work)).AddSingleton<IWorkImageRepository>(images).BuildServiceProvider();
        var library = new LibraryViewModel(new PreviewLibraryQueryRepository(), new PreviewOwnershipRepository(),
            new PreviewReleaseRepository(), new PreviewWorkRepository(), new PreviewUpdateEventRepository());
        using var context = new FullscreenContext(library, new FeedViewModel(new PreviewFeedService(), library), PreviewData.Shell, services);
        context.UiScale = uiScale;
        context.SafeMarginPercent = 3;
        var now = DateTime.UtcNow;
        using var database = new TempDatabase();
        var journal = hasJournal ? new GameJournalViewModel(
            [new SessionJournalEntry { SessionId = 1, OwnershipId = 1, SessionAt = now.AddDays(-5),
                Note = "Found the mountain camp. Next time, follow the coast toward the lighthouse." }],
            true, new Winnow.Data.Repositories.SessionRepository(database.Factory)) : null;
        using var details = new GameDetailsViewModel(TileFixture.Tile(now, title: work.Name, work: work, steamAppId: "42",
            bucket: hasJournal ? LibraryBuckets.Bounced : LibraryBuckets.NeverPlayed,
            playtimeMinutes: hasJournal ? 120 : 0, lastPlayedUtc: hasJournal ? now.AddDays(-5) : null,
            ownership: new Ownership { ReleaseId = 1, Store = "steam", Installed = true }),
            hasJournal ? "Started" : "Never played", [], now, covers: leases, images: images.Rows, journal: journal);
        using var view = new FullscreenView(context);
        context.Push(new FullscreenDetailsPage(context, details));
        var window = new Window { Width = 2560, Height = 1440, Content = view };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var backdrop = Assert.IsType<FullscreenBackdrop>(view.CurrentPage.Backdrop);
            Assert.Equal(1920 / uiScale, backdrop.Bounds.Width, 1);
            Assert.Equal(1080 / uiScale, backdrop.Bounds.Height, 1);
            Assert.Same(pixels, Assert.Single(backdrop.GetVisualDescendants().OfType<Image>(), image => image.Source is not null).Source);
            Assert.Contains(userBackground ? CoverKey.User("landscape") : CoverKey.SteamHero("42"), leases.Keys);
            Assert.Equal(1, images.Reads);
            var hero = Assert.IsType<Grid>(Assert.IsType<Grid>(view.CurrentPage.Content).Children[0]);
            Assert.Empty(hero.GetVisualDescendants().OfType<Image>());
            view.CurrentPage.FocusInitial();
            var primary = Assert.IsType<Button>(window.FocusManager!.GetFocusedElement());
            Assert.Equal("details-primary-action", Avalonia.Automation.AutomationProperties.GetAutomationId(primary));
            Assert.True(Assert.IsAssignableFrom<ISolidColorBrush>(primary.Background).Color.A > 0);
            Assert.NotEqual(Assert.IsAssignableFrom<ISolidColorBrush>(primary.Background).Color,
                Assert.IsAssignableFrom<ISolidColorBrush>(primary.Foreground).Color);
            var overview = Assert.Single(view.CurrentPage.GetVisualDescendants().OfType<Grid>(), grid => grid.Name == "FullscreenDetailsOverview");
            Assert.True(overview.Children[1].Bounds.Width > overview.Children[0].Bounds.Width * 2);
            var sections = Assert.IsType<Border>(Assert.IsType<Grid>(view.CurrentPage.Content).Children[1]);
            Assert.Equal(1, sections.BorderThickness.Bottom);
            var aboutRegion = Assert.IsType<Border>(overview.Children[1]);
            Assert.Equal(1, aboutRegion.BorderThickness.Left);
            var journalRule = Assert.Single(overview.GetVisualDescendants().OfType<Border>(), border => border.Name == "FullscreenDetailsHorizontalRule");
            Assert.Equal(1, journalRule.Bounds.Height);
            Assert.True(journalRule.Bounds.Width > 250);
            var initialScroll = Assert.Single(view.CurrentPage.GetVisualDescendants().OfType<ScrollViewer>());
            var initialStrip = Assert.Single(view.CurrentPage.GetVisualDescendants().OfType<Grid>(), grid => grid.Name == "FullscreenOverviewScreenshots");
            var gallery = Link(view.CurrentPage, "View gallery");
            var journalLink = Link(view.CurrentPage, "Open journal");
            var historyLink = Link(view.CurrentPage, "Play history");
            Assert.True(journalLink.TranslatePoint(default, overview)!.Value.Y > historyLink.TranslatePoint(default, overview)!.Value.Y);
            var previews = initialStrip.GetVisualDescendants().OfType<FullscreenScreenshotPreview>().ToArray();
            Assert.Equal(2, previews.Length);
            foreach (var preview in previews)
            {
                var image = Assert.IsType<Image>(preview.Child);
                Assert.Same(pixels, image.Source);
                Assert.Equal(16d / 9, image.Bounds.Width / image.Bounds.Height, 2);
                Assert.Equal(Stretch.Uniform, image.Stretch);
                Assert.True(preview.Bounds.Width > (longTitle ? 300 : 400));
                AssertCompleteScreenshotEdges(image);
                var screenWidth = preview.Bounds.Width * Math.Abs(preview.TransformToVisual(window)!.Value.M11);
                Assert.Contains(leases.Requests, request => request.Key == CoverKey.IgdbScreenshot("detailshot") && request.Width >= screenWidth - 1);
            }
            if (!longTitle)
            {
                foreach (var control in initialStrip.Children.Append(gallery))
                {
                    var position = control.TranslatePoint(default, initialScroll)!.Value;
                    Assert.True(position.Y >= 0);
                    Assert.True(position.Y + control.Bounds.Height <= initialScroll.Bounds.Height + 1,
                        $"{control}: bottom {position.Y + control.Bounds.Height} exceeds viewport {initialScroll.Bounds.Height}.");
                }
            }
            if (hasJournal)
            {
                var text = overview.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text).ToArray();
                Assert.Contains(details.PlaytimeText, text);
                Assert.Contains(details.IdleText, text);
                Assert.Contains(journal!.Entries[0].Note, text);
                var metricRule = Assert.Single(overview.GetVisualDescendants().OfType<Border>(), border => border.Name == "FullscreenDetailsMetricRule");
                Assert.Equal(1, metricRule.Bounds.Width);
                Assert.True(metricRule.Bounds.Height > 60);
                foreach (var value in new[] { details.PlaytimeText, details.IdleText })
                {
                    var metric = Assert.Single(overview.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == value);
                    Assert.Equal(60, metric.FontSize);
                    Assert.Equal(FontWeight.Bold, metric.FontWeight);
                }
            }
            if (userBackground && Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is { } directory)
            {
                Directory.CreateDirectory(directory);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame();
                var name = hasJournal ? "fullscreen-details-journal" : longTitle ? "fullscreen-details-long-title" : "fullscreen-details-landscape";
                frame!.Save(Path.Combine(directory, $"{name}-{uiScale:0.0}.png"));
            }
            window.Width = 3840;
            window.Height = 2160;
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(leases.Requests, request => request.Key == (userBackground ? CoverKey.User("landscape") : CoverKey.SteamHero("42")) && request.Width >= 3840);
            foreach (var preview in previews)
            {
                var screenWidth = preview.Bounds.Width * Math.Abs(preview.TransformToVisual(window)!.Value.M11);
                Assert.Contains(leases.Requests, request => request.Key == CoverKey.IgdbScreenshot("detailshot") && request.Width >= screenWidth - 1);
            }
            context.TextScale = 1.4;
            context.UiScale = 1.2;
            window.Width = 1280;
            window.Height = 720;
            Dispatcher.UIThread.RunJobs();
            var layout = Assert.IsType<Grid>(view.CurrentPage.Content);
            Assert.True(hero.Bounds.Bottom <= layout.Children[1].Bounds.Top);
            Assert.True(layout.Children[2].Bounds.Height > 100);
            var scroll = Assert.Single(view.CurrentPage.GetVisualDescendants().OfType<ScrollViewer>());
            var strip = Assert.Single(view.CurrentPage.GetVisualDescendants().OfType<Grid>(), grid => grid.Name == "FullscreenOverviewScreenshots");
            var historyRegion = overview.Children[0];
            foreach (var block in historyRegion.GetVisualDescendants().OfType<TextBlock>())
            {
                var point = block.TranslatePoint(default, historyRegion)!.Value;
                Assert.True(point.X >= -1 && point.X + block.Bounds.Width <= historyRegion.Bounds.Width + 1,
                    $"History text '{block.Text}' extends beyond its column: {point.X} + {block.Bounds.Width} > {historyRegion.Bounds.Width}.");
            }
            Assert.Equal(2, strip.Children.Count);
            foreach (var button in strip.Children.OfType<Button>())
            {
                Assert.True(button.Bounds.Height >= 100);
                button.Focus();
                view.CurrentPage.FocusInitial();
                Dispatcher.UIThread.RunJobs();
                var position = button.TranslatePoint(default, scroll)!.Value;
                var geometry = $"Shot top={position.Y}, height={button.Bounds.Height}; viewport={scroll.Viewport.Height}, bounds={scroll.Bounds.Height}, offset={scroll.Offset.Y}, extent={scroll.Extent.Height}; row={strip.Bounds}, maxHeight={strip.MaxHeight}.";
                Assert.True(position.Y >= -1, geometry);
                Assert.True(position.Y + button.Bounds.Height <= scroll.Bounds.Height + 1, geometry);
                Assert.Same(button, window.FocusManager!.GetFocusedElement());
            }
            if (longTitle && Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is { } scaledDirectory)
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame();
                frame!.Save(Path.Combine(scaledDirectory, "fullscreen-details-long-title-140.png"));
            }
            var about = Link(view.CurrentPage, "Read more");
            about.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.IsType<FullscreenDetailsReadingPage>(view.CurrentPage);
            Assert.Contains(view.CurrentPage.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == work.Summary);
            view.Back();

            window.Content = null;
            details.Dispose();
            Assert.Equal(0, leases.Active);
            Assert.All(backdrop.GetVisualDescendants().OfType<Image>(), image => Assert.Null(image.Source));
        }
        finally { window.Close(); }
    }

    private sealed class DetailImages : IWorkImageRepository
    {
        public int Reads { get; private set; }
        public IReadOnlyList<WorkImages> Rows { get; } = [new() { WorkId = 1, Source = ImageSources.Igdb,
            Kind = ImageKinds.Screenshot, ImageIds = "detailshot,detailshot2", ObservedAt = DateTime.UtcNow }];
        public Task<IReadOnlyList<WorkImages>> GetForWorkAsync(long workId, CancellationToken ct = default) { Reads++; return Task.FromResult(Rows); }
        public Task UpsertAsync(WorkImages images, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(long workId, string source, string kind, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class DetailWorks(Work work) : IWorkRepository
    {
        public Task<Work?> GetAsync(long id, CancellationToken ct = default) => Task.FromResult<Work?>(work);
        public Task<long> InsertAsync(Work value, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateNameAsync(long id, string name, bool provisional, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Work?> GetByIgdbIdAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Work>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProvisionalNameTarget>> GetProvisionalNameTargetsAsync(string provider, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<EnrichmentTarget>> GetEnrichmentTargetsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> ApplyEnrichmentAsync(WorkEnrichment enrichment, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class DetailLeases(CoverArt art) : ICoverLeases
    {
        public int Active { get; private set; }
        public List<CoverKey> Keys { get; } = [];
        public List<(CoverKey Key, double Width)> Requests { get; } = [];
        public ICoverLease Acquire(CoverKey key, double width, CoverLayers layers = CoverLayers.VividAndFloor)
        { Active++; Keys.Add(key); Requests.Add((key, width)); return new DetailLease(this, art, key, CoverImaging.SnapWidth(width), layers); }
        private sealed class DetailLease(DetailLeases owner, CoverArt art, CoverKey key, int width, CoverLayers layers) : ICoverLease
        {
            private bool _disposed;
            public CoverKey Key => key;
            public int Width => width;
            public CoverLayers Layers => layers;
            public bool TryGetArt(out CoverArt value) { value = art; return true; }
            public Task<CoverArt?> GetAsync(CancellationToken ct = default) => Task.FromResult<CoverArt?>(art);
            public void Dispose() { if (_disposed) return; _disposed = true; owner.Active--; }
        }
    }

    [AvaloniaFact]
    public void Single_copy_achievements_remain_visible_without_an_identity_link()
    {
        var now = DateTime.UtcNow;
        var entry = new CoverageEntry { OwnershipId = 1, ReleaseId = 1, WorkId = 1, Title = "One copy", Store = "steam", PlaytimeMinutes = 60 };
        var coverage = new GameCoverageViewModel(IdentityCoverage.For(1, SameGameResolution.Empty, [entry]),
            new Dictionary<long, string> { [1] = "One copy" }, new Dictionary<long, ReleaseAchievementSummary>
            { [1] = new() { ReleaseId = 1, Total = 20, Unlocked = 5 } });
        using var details = new GameDetailsViewModel(TileFixture.Tile(now, title: "One copy"), "Started", [], now, coverage: coverage);
        Assert.False(coverage.HasCoverage);
        var page = new FullscreenDetailsPage(null!, details, 3);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "Achievements: 5/20 · 25%");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Per_game_history_switches_from_lifetime_to_observed_sessions_with_controller()
    {
        var now = DateTime.UtcNow;
        var tracker = new ActivityTrackerViewModel([], [new Session { OwnershipId = 1, StartedAt = now.AddHours(-2),
            EndedAt = now.AddHours(-1), DurationSeconds = 3600, DetectionMethod = "process" }], null, now.AddHours(-1), 60, now);
        var page = new FullscreenDetailsHistoryPage(null!, tracker);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            page.FocusInitial();
            page.Handle(GamepadButtons.Right);
            page.Handle(GamepadButtons.Accept);
            Dispatcher.UIThread.RunJobs();
            Assert.True(tracker.IsTrackedSessions);
            Assert.Single(tracker.Series.Bars);
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "Only sessions observed by Winnow are shown.");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Long_game_title_and_actions_fit_the_hero_without_overlapping_section_navigation()
    {
        var now = DateTime.UtcNow;
        using var details = new GameDetailsViewModel(TileFixture.Tile(now, title: string.Join(" ", Enumerable.Repeat("A very long game title", 10))), "Never played", [], now);
        var page = new FullscreenDetailsPage(null!, details);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var layout = Assert.IsType<Grid>(page.Content);
            var hero = layout.Children[0];
            var sections = layout.Children[1];
            foreach (var child in hero.GetVisualDescendants().OfType<Button>())
            {
                var position = child.TranslatePoint(default, hero)!.Value;
                Assert.True(position.Y >= 0);
                Assert.True(position.Y + child.Bounds.Height <= hero.Bounds.Height + 1);
            }
            var title = Assert.Single(hero.GetVisualDescendants().OfType<TextBlock>(), block => block.Classes.Contains("tv-title"));
            Assert.True(title.Bounds.Height > title.FontSize);
            Assert.True(hero.Bounds.Bottom <= sections.Bounds.Top);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Version_chooser_does_not_launch_until_selected_and_uses_the_selected_copy_uri()
    {
        var dispatcher = new RecordingDispatcher();
        var library = new LibraryViewModel(new PreviewLibraryQueryRepository(), new PreviewOwnershipRepository(),
            new PreviewReleaseRepository(), new PreviewWorkRepository(), new PreviewUpdateEventRepository(),
            launcher: new GameLaunchService(dispatcher));
        using var context = new FullscreenContext(library, new FeedViewModel(new PreviewFeedService(), library), PreviewData.Shell);
        var now = DateTime.UtcNow;
        var first = TileFixture.Tile(now).Primary with { OwnershipId = 1, SteamAppId = "10", Installed = true };
        var second = first with { OwnershipId = 2, SteamAppId = "20" };
        var tile = new GameTileViewModel([first, second], TileFixture.Tile(now).Game, "Two copies", now);
        FullscreenPage? choice = null;
        context.PageRequested += page => choice = page;
        context.ChooseVersion(tile);
        Assert.Empty(dispatcher.Uris);
        Assert.NotNull(choice);
        var window = new Window { Width = 1920, Height = 1080, Content = choice };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            choice.FocusInitial();
            choice.Handle(GamepadButtons.Down);
            choice.Handle(GamepadButtons.Accept);
            Assert.Contains("20", Assert.Single(dispatcher.Uris).AbsoluteUri);
            context.Dispose();
            context.Play(tile);
            Assert.Single(dispatcher.Uris);
        }
        finally { window.Close(); }
    }

    private sealed class RecordingDispatcher : IUriDispatcher
    {
        public List<Uri> Uris { get; } = [];
        public Task<bool> OpenAsync(Uri uri) { Uris.Add(uri); return Task.FromResult(true); }
    }

    [AvaloniaFact]
    public void Reload_replaces_the_underlying_detail_and_preserves_local_section_and_open_editor()
    {
        var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell);
        using var view = new FullscreenView(context);
        var window = new Window { Width = 1920, Height = 1080, Content = view };
        var now = DateTime.UtcNow;
        context.Library.Details = new GameDetailsViewModel(TileFixture.Tile(now, title: "Before"), "Started", [], now);
        window.Show();
        try
        {
            context.Push(new FullscreenDetailsPage(context, context.Library.Details, 2));
            var editor = new FullscreenDetailsReadingPage(context, "Open draft", "Keep this page in place.");
            context.Push(editor);
            context.Library.Details = new GameDetailsViewModel(TileFixture.Tile(now, title: "After"), "Started", [], now);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(editor, view.CurrentPage);
            view.Back();
            var refreshed = Assert.IsType<FullscreenDetailsPage>(view.CurrentPage);
            Assert.Equal("After", refreshed.Title);
            Assert.Equal(2, refreshed.SelectedSection);
            view.Back();
            Assert.IsType<FullscreenBrowsePage>(view.CurrentPage);
            Assert.Null(context.Library.Details);
        }
        finally { window.Close(); context.Library.CloseDetailsCommand.Execute(null); }
    }

    [AvaloniaFact]
    public void Pending_session_prompt_opens_on_attach_and_dismisses_through_the_shared_model()
    {
        var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell);
        using var view = new FullscreenView(context);
        var prompt = context.Shared.Library.Journal;
        prompt.DismissCommand.Execute(null);
        prompt.Open(new EndedSession(781, 1, 600), "A finished game");
        Assert.IsType<FullscreenBrowsePage>(view.CurrentPage);
        var window = new Window { Width = 1920, Height = 1080, Content = view };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.IsType<FullscreenSessionJournalPage>(view.CurrentPage);
            view.Handle(GamepadButtons.Back);
            Assert.False(prompt.IsOpen);
            Assert.IsType<FullscreenBrowsePage>(view.CurrentPage);
        }
        finally { window.Close(); prompt.DismissCommand.Execute(null); }
    }

    [AvaloniaFact]
    public async Task Ungroup_from_the_expansion_end_requires_confirmation_and_uses_child_identity()
    {
        var changed = new List<long>();
        var row = new ExpansionRowViewModel(10, 99, "Base game", ["STEAM"], "Steam", 30, null);
        var expansions = new GameExpansionsViewModel([], row, child => { changed.Add(child); return Task.CompletedTask; });
        var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell);
        FullscreenPage? confirmation = null;
        context.PageRequested += page => confirmation = page;
        var now = DateTime.UtcNow;
        using var details = new GameDetailsViewModel(TileFixture.Tile(now, title: "Expansion"), "Started", [], now, expansions: expansions);
        var page = new FullscreenDetailsPage(context, details);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            page.Handle(GamepadButtons.PagePrevious);
            Dispatcher.UIThread.RunJobs();
            var ungroup = Assert.Single(page.GetVisualDescendants().OfType<Button>(), button =>
                Avalonia.Automation.AutomationProperties.GetName(button) == row.UngroupAutomationName);
            ungroup.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Empty(changed);
            Assert.NotNull(confirmation);
            window.Content = confirmation;
            Dispatcher.UIThread.RunJobs();
            confirmation.FocusInitial();
            confirmation.Handle(GamepadButtons.Down);
            confirmation.Handle(GamepadButtons.Accept);
            if (expansions.UngroupCommand.ExecutionTask is { } task) await task;
            Assert.Equal([99L], changed);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Opening_details_focuses_play_without_launching_and_triggers_own_local_sections()
    {
        var now = DateTime.UtcNow;
        using var details = new GameDetailsViewModel(TileFixture.Tile(now, title: "Across the room"), "Never played", [], now);
        var page = new FullscreenDetailsPage(null!, details);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            page.FocusInitial();
            Assert.Equal(0, page.SelectedSection);
            Assert.IsType<Button>(window.FocusManager!.GetFocusedElement());
            var first = (Button)window.FocusManager.GetFocusedElement()!;
            if (details.HasPrimaryAction)
                Assert.Equal("details-primary-action", Avalonia.Automation.AutomationProperties.GetAutomationId(first));
            else
                Assert.Equal("More", first.Content);
            Assert.True(page.Handle(GamepadButtons.PageNext));
            Assert.Equal(1, page.SelectedSection);
            Assert.Equal(0, details.SelectedTabIndex);
            Assert.True(page.Handle(GamepadButtons.PagePrevious));
            Assert.Equal(0, page.SelectedSection);
            Assert.True(page.Handle(GamepadButtons.PagePrevious));
            Assert.Equal(3, page.SelectedSection);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Long_description_has_an_explicit_controller_reading_region()
    {
        var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell);
        var page = new FullscreenDetailsReadingPage(context, "Long description", string.Join("\n", Enumerable.Repeat("A readable line", 100)));
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var scroll = Assert.Single(page.GetVisualDescendants().OfType<ScrollViewer>());
            Assert.True(page.Handle(GamepadButtons.Down));
            Assert.True(scroll.Offset.Y > 0);
            Assert.True(page.Handle(GamepadButtons.Up));
            Assert.Equal(0, scroll.Offset.Y);
        }
        finally { window.Close(); }
    }
}
