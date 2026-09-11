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
    [AvaloniaFact]
    public async Task Fullscreen_keeps_open_game_and_eligible_header_actions_separate()
    {
        using var db = new TempDatabase();
        using var services = await Seed(db);
        var library = new LibraryViewModel(services.GetRequiredService<ILibraryQueryRepository>(), services.GetRequiredService<IOwnershipRepository>(),
            services.GetRequiredService<IReleaseRepository>(), services.GetRequiredService<IWorkRepository>(), new UpdateEventRepository(db.Factory));
        await library.LoadCommand.ExecuteAsync(null);
        using var context = new FullscreenContext(library, PreviewData.Feed, PreviewData.Shell, services);
        using var page = new FullscreenIdentityPage(context);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        var opened = new List<FullscreenPage>();
        context.PageRequested += sheet => { opened.Add(sheet); window.Content = sheet; };
        context.BackRequested += () => { };
        Button Find(string text) => window.GetVisualDescendants().OfType<Button>().First(button => button.Content?.ToString()?.Contains(text, StringComparison.Ordinal) == true);
        void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
        try
        {
            window.Show();
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!page.GetVisualDescendants().OfType<Button>().Any(button => button.Content?.ToString()?.Contains("entries ·", StringComparison.Ordinal) == true) && DateTime.UtcNow < deadline)
            { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Click(Find("entries ·"));
            Click(Find("· Included"));
            Assert.NotNull(Find("Open game"));
            var promote = Find("Make header");
            Assert.True(promote.Focus(NavigationMethod.Tab));
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs();
            Click(Find("· Header"));
            Assert.NotNull(Find("Open game"));
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "Make header"));
        }
        finally { window.Close(); foreach (var sheet in opened) sheet.Dispose(); }
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

    private static async Task<ServiceProvider> Seed(TempDatabase db)
    {
        var works = new WorkRepository(db.Factory);
        var releases = new ReleaseRepository(db.Factory);
        var ownership = new OwnershipRepository(db.Factory);
        var candidates = new MergeCandidateRepository(db.Factory);
        async Task<long> Add(string store)
        {
            var work = await works.InsertAsync(new Work { Name = "Bastion", FirstReleaseYear = 2011, Publisher = "Supergiant Games" });
            var release = await releases.InsertAsync(new Release { WorkId = work, Name = "Bastion", Platform = "windows" });
            await ownership.UpsertAsync(new OwnershipUpsert(release, store, null, null, null, null));
            return release;
        }
        var left = await Add("steam");
        var right = await Add("epic");
        MatchSubject Subject(long id) => new() { ReleaseId = id, Title = "Bastion", ReleaseYear = 2011, Publisher = "Supergiant Games" };
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
