using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Winnow.Core.Repositories;
using Winnow.Core.Domain;
using Winnow.Data.Repositories;
using Winnow.Resolve;
using Winnow.Resolve.Matching;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class MergeRowActionsTests
{
    private const string LongTitle = "Metal Gear Solid V: The Phantom Pain — The Definitive Experience Collector's Edition";

    [AvaloniaTheory]
    [InlineData(1920, "Bastion")]
    [InlineData(1280, LongTitle)]
    public async Task Fullscreen_keeps_open_game_and_eligible_header_actions_separate(int width, string title)
    {
        using var db = new TempDatabase();
        using var services = await Seed(db, title);
        var library = new LibraryViewModel(services.GetRequiredService<ILibraryQueryRepository>(), services.GetRequiredService<IOwnershipRepository>(),
            services.GetRequiredService<IReleaseRepository>(), services.GetRequiredService<IWorkRepository>(), new UpdateEventRepository(db.Factory));
        await library.LoadCommand.ExecuteAsync(null);
        using var context = new FullscreenContext(library, PreviewData.Feed, PreviewData.Shell, services);
        using var television = new FullscreenView(context);
        var page = new FullscreenIdentityPage(context);
        var window = new Window { Width = width, Height = width * 9 / 16, Content = television };
        Button Find(string text) => window.GetVisualDescendants().OfType<Button>().First(button => button.Content?.ToString()?.Contains(text, StringComparison.Ordinal) == true);
        void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
        try
        {
            window.Show();
            context.Push(page);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!page.GetVisualDescendants().OfType<Button>().Any(button => button.Content?.ToString()?.Contains("entries ·", StringComparison.Ordinal) == true) && DateTime.UtcNow < deadline)
            { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            var proposal = Find("entries ·");
            AssertFitsHorizontally(proposal, window);
            Capture(window, $"merges-fullscreen-{width}");
            Click(proposal);
            var member = Find("· Included");
            AssertFitsHorizontally(member, window);
            Click(member);
            Assert.NotNull(Find("Open game"));
            var promote = Find("Make header");
            AssertFitsHorizontally(promote, window);
            Capture(window, $"merges-fullscreen-member-{width}");
            Assert.True(promote.Focus(NavigationMethod.Tab));
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs();
            Click(Find("· Header"));
            Assert.NotNull(Find("Open game"));
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "Make header"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Long_titles_keep_cards_and_trailing_actions_inside_the_desktop_pane_when_resized()
    {
        using var db = new TempDatabase();
        using var services = await Seed(db, LongTitle, multipleStores: true);
        using var queue = ActivatorUtilities.CreateInstance<MergeQueueViewModel>(services);
        await queue.LoadCommand.ExecuteAsync(null);
        var card = queue.Sections.SelectMany(section => section.Cards).Single();
        var member = card.Rows.Single(row => !row.IsHeader);
        var view = new MergeQueueView { DataContext = queue };
        var window = new Window { Width = 1670, Height = 700, Content = view };
        MergeRowViewModel? opened = null;
        queue.DetailsRequested += row => opened = row;
        try
        {
            window.Show();
            // Pane widths reserve the desktop rail and shell margins, including the 1200px minimum window.
            foreach (var width in new[] { 1670, 950, 1030, 950 })
            {
                window.Width = width;
                Dispatcher.UIThread.RunJobs();
                var cardBody = view.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("card"));
                cardBody.BringIntoView();
                Dispatcher.UIThread.RunJobs();
                Capture(window, $"merges-desktop-pane-{width}");
                var scroll = view.GetVisualDescendants().OfType<ScrollViewer>().First();
                AssertFitsHorizontally(cardBody, scroll);
                Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1);
                foreach (var control in cardBody.GetVisualDescendants().OfType<Control>()
                             .Where(control => control.IsEffectivelyVisible && (control is Button or CheckBox || control is TextBlock)))
                    AssertFitsHorizontally(control, cardBody);
                var headline = cardBody.GetVisualDescendants().OfType<TextBlock>()
                    .Single(text => text.IsEffectivelyVisible && text.Classes.Contains("headline"));
                var badge = cardBody.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("badge"));
                var answer = cardBody.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Same game"));
                AssertBefore(headline, badge, cardBody);
                AssertBefore(badge, answer, cardBody);

                var rowBody = cardBody.GetVisualDescendants().OfType<Border>()
                    .Single(border => border.Classes.Contains("row") && ReferenceEquals(border.DataContext, member));
                var title = rowBody.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Classes.Contains("rowtitle"));
                var mark = rowBody.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Classes.Contains("mark"));
                var stores = rowBody.GetVisualDescendants().OfType<ItemsControl>().Single();
                AssertBefore(title, mark, rowBody);
                AssertBefore(mark, stores, rowBody);
                var details = rowBody.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Details"));
                opened = null;
                Click(details);
                Assert.Same(member, opened);
                Assert.False(member.IsHeader);
                var include = rowBody.GetVisualDescendants().OfType<CheckBox>().Single();
                Click(include);
                Assert.False(member.IsIncluded);
                Click(include);
                Assert.True(member.IsIncluded);
            }
            card.MarkResolved(123);
            Dispatcher.UIThread.RunJobs();
            Capture(window, "merges-desktop-resolved-pane-950");
            var resolved = view.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("card"));
            AssertFitsHorizontally(resolved, view);
            foreach (var control in resolved.GetVisualDescendants().OfType<Control>()
                         .Where(control => control.IsEffectivelyVisible && control is Button or ComboBox or TextBlock))
                AssertFitsHorizontally(control, resolved);
        }
        finally { window.Close(); }

        void Click(Control control)
        {
            var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void AssertFitsHorizontally(Control child, Control container)
    {
        var left = child.TranslatePoint(default, container)!.Value.X;
        var right = child.TranslatePoint(new Point(child.Bounds.Width, 0), container)!.Value.X;
        Assert.True(left >= -1 && right <= container.Bounds.Width + 1,
            $"{child.GetType().Name} spans {left}..{right} in {container.Bounds.Width}px {container.GetType().Name}.");
    }

    private static void AssertBefore(Control left, Control right, Control container) =>
        Assert.True(left.TranslatePoint(new Point(left.Bounds.Width, 0), container)!.Value.X <=
                    right.TranslatePoint(default, container)!.Value.X);

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name + ".png"));
    }

    [AvaloniaFact]
    public async Task Row_body_promotes_while_details_and_radio_have_independent_pointer_and_keyboard_actions()
    {
        using var db = new TempDatabase();
        using var services = await Seed(db);
        using var queue = ActivatorUtilities.CreateInstance<MergeQueueViewModel>(services);
        await queue.LoadCommand.ExecuteAsync(null);
        var card = queue.Sections.SelectMany(section => section.Cards).First(card => card.Rows.All(row => row.CanPromote));
        var first = card.Rows[0];
        var second = card.Rows[1];
        card.Promote(first);
        var view = new MergeQueueView { DataContext = queue };
        var window = new Window { Width = 1200, Height = 900, Content = view };
        MergeRowViewModel? opened = null;
        void Open(MergeRowViewModel row) => opened = row;
        queue.DetailsRequested += Open;
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var rowBody = view.GetVisualDescendants().OfType<Border>()
                .Single(border => border.Classes.Contains("row") && ReferenceEquals(border.DataContext, second));
            rowBody.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            var point = rowBody.TranslatePoint(new Point(95, 30), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.True(second.IsHeader);
            Assert.False(first.IsHeader);
            Assert.Null(opened);
            Assert.Equal(second.HeaderMark, AutomationProperties.GetItemStatus(rowBody));

            var details = rowBody.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Details"));
            Assert.Equal(second.DetailsAutomationName, AutomationProperties.GetName(details));
            Assert.True(details.Focus(NavigationMethod.Tab));
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.Same(second, opened);
            Assert.True(second.IsHeader);

            var firstBody = view.GetVisualDescendants().OfType<Border>()
                .Single(border => border.Classes.Contains("row") && ReferenceEquals(border.DataContext, first));
            var radio = firstBody.GetVisualDescendants().OfType<RadioButton>().Single();
            Assert.True(radio.Focus(NavigationMethod.Tab));
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            Assert.True(first.IsHeader);
            Assert.False(second.IsHeader);
            opened = null;
            point = details.TranslatePoint(new Point(details.Bounds.Width / 2, details.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Assert.Same(second, opened);
            Assert.True(first.IsHeader);
            card.MarkResolved(123);
            Dispatcher.UIThread.RunJobs();
            Assert.False(rowBody.IsEffectivelyVisible);
            card.Promote(second);
            Assert.True(first.IsHeader);
        }
        finally { queue.DetailsRequested -= Open; window.Close(); }
    }

    private static async Task<ServiceProvider> Seed(TempDatabase db, string title = "Bastion", bool multipleStores = false)
    {
        var works = new WorkRepository(db.Factory);
        var releases = new ReleaseRepository(db.Factory);
        var ownership = new OwnershipRepository(db.Factory);
        var candidates = new MergeCandidateRepository(db.Factory);
        async Task<long> Add(string store)
        {
            var work = await works.InsertAsync(new Work { Name = title, FirstReleaseYear = 2011, Publisher = "Supergiant Games" });
            var release = await releases.InsertAsync(new Release { WorkId = work, Name = title, Platform = "windows" });
            await ownership.UpsertAsync(new OwnershipUpsert(release, store, null, null, null, null));
            return release;
        }
        var left = await Add("steam");
        if (multipleStores)
            await ownership.UpsertAsync(new OwnershipUpsert(left, "gog", null, null, null, null));
        var right = await Add("epic");
        MatchSubject Subject(long id) => new() { ReleaseId = id, Title = title, ReleaseYear = 2011, Publisher = "Supergiant Games" };
        var score = new SoftMatcher().Score(Subject(left), Subject(right));
        await candidates.InsertAsync(new MergeCandidate { LeftReleaseId = left, RightReleaseId = right, Score = score.Score,
            SignalsJson = SoftMatchSignalsJson.Serialize(score), Status = MergeCandidateStatuses.Pending });
        return new ServiceCollection().AddSingleton<IMergeCandidateRepository>(candidates)
            .AddSingleton<IReleaseRepository>(releases).AddSingleton<IWorkRepository>(works)
            .AddSingleton<IIdentityLinkRepository>(new IdentityLinkRepository(db.Factory))
            .AddSingleton<IOwnershipRepository>(ownership)
            .AddSingleton<IExpansionRefusalRepository>(new ExpansionRefusalRepository(db.Factory))
            .AddSingleton<ILibraryQueryRepository>(new LibraryQueryRepository(db.Factory))
            .AddSingleton<LibraryExpansionScan>().BuildServiceProvider();
    }
}
