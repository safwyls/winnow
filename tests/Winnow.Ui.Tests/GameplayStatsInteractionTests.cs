using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class GameplayStatsInteractionTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Xbox_import_updates_open_gameplay_store_choices_and_library_counts(bool fullscreen)
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 2);
        using var library = new LibraryViewModel(new LibraryQueryRepository(db.Factory), new OwnershipRepository(db.Factory),
            new ReleaseRepository(db.Factory), new WorkRepository(db.Factory), new UpdateEventRepository(db.Factory));
        await library.LoadCommand.ExecuteAsync(null);
        var repository = new GameplayStatsRepository(db.Factory);
        using var desktop = new StatsViewModel(new AccountStatsViewModel(new SpendingRepository()), new GameplayStatsViewModel(repository, library));
        using var services = new ServiceCollection().AddSingleton<IGameplayStatsRepository>(repository).BuildServiceProvider();
        using var context = new FullscreenContext(library, PreviewData.Feed, PreviewData.Shell, services);
        using var page = new FullscreenLibrarySummaryPage(context);
        var state = fullscreen ? page.Stats : desktop;
        var window = new Window { Width = fullscreen ? 1920 : 1200, Height = 1080,
            Content = fullscreen ? page : new StatsView { DataContext = desktop } };
        try
        {
            window.Show();
            if (fullscreen) await page.PendingRefresh; else await desktop.ActivateAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(state.Gameplay.StoreOptions, store => store.Key == "plugin:xbox");
            using (var connection = db.Factory.Open())
                connection.Execute("UPDATE ownerships SET store='plugin:xbox' WHERE id=2;");
            await library.LoadCommand.ExecuteAsync(null);
            await state.Gameplay.PendingRefresh; Dispatcher.UIThread.RunJobs();
            var xbox = Assert.Single(state.Gameplay.StoreOptions, store => store.Key == "plugin:xbox");
            Assert.Equal("Xbox", xbox.Label);
            Assert.Equal(1, Assert.Single(state.Gameplay.StoresChart, item => item.Label == "Xbox").Value);
            Assert.Equal(1, Assert.Single(state.Gameplay.StoresChart, item => item.Label == "Steam").Value);
            if (fullscreen)
            {
                var button = Assert.Single(page.GetVisualDescendants().OfType<Button>(), button => button.Content as string == "Xbox");
                Assert.True(button.Focus()); page.Handle(GamepadButtons.Accept);
            }
            else window.GetVisualDescendants().OfType<ComboBox>().Single(box => box.Name == "GameplayStore").SelectedItem = xbox;
            await state.Gameplay.PendingRefresh; Dispatcher.UIThread.RunJobs();
            Assert.Equal("plugin:xbox", state.Gameplay.SelectedStore.Key);
            Assert.StartsWith("Xbox ·", state.Gameplay.PeriodLabel);
            Assert.Equal(1, state.Gameplay.LibraryChart.Sum(item => item.Value));
            Assert.Equal("Xbox", Assert.Single(state.Gameplay.StoresChart).Label);
            Assert.Equal("0 h", state.Gameplay.HoursText);
            Assert.Equal("No completed sessions", state.Gameplay.MedianText);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, 1200)]
    [InlineData(false, 600)]
    [InlineData(true, 1920)]
    [InlineData(true, 1280)]
    public async Task Both_surfaces_scope_dates_switch_sections_and_render_four_charts(bool fullscreen, int width)
    {
        using var library = CreateLibrary(); await library.LoadCommand.ExecuteAsync(null);
        var repository = new RecordingRepository();
        using var desktop = new StatsViewModel(new AccountStatsViewModel(new SpendingRepository()), new GameplayStatsViewModel(repository, library));
        using var services = new ServiceCollection().AddSingleton<IGameplayStatsRepository>(repository)
            .AddSingleton<IAccountStatsRepository>(new SpendingRepository()).BuildServiceProvider();
        using var context = new FullscreenContext(library, PreviewData.Feed, PreviewData.Shell, services);
        using var page = new FullscreenLibrarySummaryPage(context);
        var state = fullscreen ? page.Stats : desktop;
        var content = fullscreen ? (Control)page : new StatsView { DataContext = desktop };
        var window = new Window { Width = width, Height = fullscreen ? 1080 : 900, Content = content,
            Background = (IBrush)content.FindResource("Ground")! };
        var typeSizes = new Dictionary<Control, double>();
        try
        {
            window.Show();
            if (fullscreen) await page.PendingRefresh; else await desktop.ActivateAsync();
            Flush();
            Assert.True(state.Gameplay.HasData); Assert.False(state.IsSpending);
            var dashboard = window.GetVisualDescendants().OfType<GameplayStatsDashboard>().Single();
            Assert.NotEmpty(state.Gameplay.HoursChart); Assert.NotEmpty(state.Gameplay.GamesChart);
            Assert.NotEmpty(state.Gameplay.LengthChart); Assert.NotEmpty(state.Gameplay.LibraryChart);
            Capture("overview");
            ScrollTo("Recorded hours over time"); Capture("hours");
            ScrollTo("Your library today"); Capture("library");
            var libraryCounts = state.Gameplay.LibraryChart.Select(x => x.Value).ToArray();
            if (fullscreen) Click("90 days");
            else window.GetVisualDescendants().OfType<ComboBox>().Single(x => x.Name == "GameplayPeriod").SelectedItem = "90 days";
            await state.Gameplay.PendingRefresh; Flush();
            Assert.Equal("90 days", state.Gameplay.SelectedPeriod);
            Assert.Equal(libraryCounts, state.Gameplay.LibraryChart.Select(x => x.Value));
            var store = state.Gameplay.StoreOptions.First(x => x.Key.Length > 0);
            if (fullscreen) Click(store.Label);
            else window.GetVisualDescendants().OfType<ComboBox>().Single(x => x.Name == "GameplayStore").SelectedItem = store;
            await state.Gameplay.PendingRefresh; Flush(); Assert.Equal(store.Key, repository.Last!.Store);
            if (fullscreen) Click("Custom");
            else window.GetVisualDescendants().OfType<ComboBox>().Single(x => x.Name == "GameplayPeriod").SelectedItem = "Custom";
            await state.Gameplay.PendingRefresh; Flush();
            var from = window.GetVisualDescendants().OfType<TextBox>().Single(x => x.Name == "GameplayFrom" || AutomationProperties.GetAutomationId(x) == nameof(GameplayStatsViewModel.CustomFrom));
            var until = window.GetVisualDescendants().OfType<TextBox>().Single(x => x.Name == "GameplayUntil" || AutomationProperties.GetAutomationId(x) == nameof(GameplayStatsViewModel.CustomUntil));
            if (fullscreen)
            {
                TextBox? requested = null; context.TextRequested += field => requested = field;
                from.Focus(); page.Handle(GamepadButtons.Accept); Assert.Same(from, requested);
            }
            from.Text = "2026-09-01"; until.Text = "2026-09-10"; Flush(); Click("Apply dates");
            await state.Gameplay.PendingRefresh; Flush();
            Assert.Contains("1 Sep 2026", state.Gameplay.PeriodLabel);
            Assert.Contains("10 Sep 2026", state.Gameplay.PeriodLabel);
            Assert.Null(state.Gameplay.Problem);
            window.FocusManager?.ClearFocus(); ActiveScroll().Offset = default; Flush();
            Capture("custom");
            if (fullscreen) page.Handle(GamepadButtons.PageNext);
            else
            {
                var button = window.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "SpendingSection");
                button.Focus(); window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null); window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            }
            await state.PendingRefresh; Flush(); Assert.True(state.IsSpending);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), x => x.IsEffectivelyVisible && x.Text?.StartsWith("Source: Steam account pages.") == true);
            Assert.Equal("$20.00", state.Spending.NetSpendValue);
            if (fullscreen) page.Handle(GamepadButtons.PagePrevious); else Click("Gameplay");
            await state.PendingRefresh; Flush(); Assert.False(state.IsSpending);
            Assert.Equal(store.Key, state.Gameplay.SelectedStore.Key);
            Assert.Equal("Custom", state.Gameplay.SelectedPeriod);
            Assert.NotSame(desktop.Gameplay, page.Stats.Gameplay);
            var scroll = ActiveScroll(); Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1);
            void Click(string label)
            {
                var button = window.GetVisualDescendants().OfType<Button>().Single(x => x.IsEffectivelyVisible && x.Content as string == label);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (button.Command?.CanExecute(button.CommandParameter) == true) button.Command.Execute(button.CommandParameter);
            }
            ScrollViewer ActiveScroll() => window.GetVisualDescendants().OfType<ScrollViewer>()
                .Where(x => x.IsEffectivelyVisible && x.Extent.Height > x.Viewport.Height).OrderByDescending(x => x.Viewport.Height).First();
            void ScrollTo(string heading)
            {
                window.FocusManager?.ClearFocus();
                var scroll = ActiveScroll();
                var text = window.GetVisualDescendants().OfType<TextBlock>().Single(x => x.Text == heading && x.IsEffectivelyVisible);
                scroll.Offset = new Vector(0, scroll.Offset.Y + text.TranslatePoint(default, scroll)!.Value.Y - 20); Flush();
            }
            void Flush()
            {
                Dispatcher.UIThread.RunJobs();
                if (fullscreen && width == 1280)
                {
                    // Match the actual shell's 140% text adjustment on this isolated page.
                    foreach (var text in window.GetVisualDescendants().OfType<TextBlock>())
                    {
                        if (!typeSizes.TryGetValue(text, out var original)) typeSizes[text] = original = text.FontSize;
                        if (original < 48) text.FontSize = original * 1.4;
                    }
                    foreach (var control in window.GetVisualDescendants().OfType<TemplatedControl>().Where(x => x.IsSet(TemplatedControl.FontSizeProperty)))
                    {
                        if (!typeSizes.TryGetValue(control, out var original)) typeSizes[control] = original = control.FontSize;
                        control.FontSize = original * 1.4;
                    }
                }
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            }
            void Capture(string section)
            {
                if (Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is not { } directory) return;
                Directory.CreateDirectory(directory);
                using var frame = window.CaptureRenderedFrame();
                frame?.Save(Path.Combine(directory, $"gameplay-{(fullscreen ? "fullscreen" : "desktop")}-{width}-{section}.png"));
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Desktop_error_retry_and_custom_date_validation_remain_actionable()
    {
        using var library = CreateLibrary(); await library.LoadCommand.ExecuteAsync(null);
        var repository = new RecordingRepository { Fail = true };
        using var state = new StatsViewModel(new AccountStatsViewModel(new SpendingRepository()), new GameplayStatsViewModel(repository, library));
        var window = new Window { Width = 800, Height = 800, Content = new StatsView { DataContext = state } };
        try
        {
            window.Show(); await state.ActivateAsync(); Dispatcher.UIThread.RunJobs();
            Assert.NotNull(state.Gameplay.Problem);
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), x => x.IsEffectivelyVisible && x.Content as string == "Try again");
            repository.Fail = false; await state.Gameplay.RefreshCommand.ExecuteAsync(null); Dispatcher.UIThread.RunJobs();
            Assert.Null(state.Gameplay.Problem); Assert.True(state.Gameplay.HasData);
            state.Gameplay.SelectedPeriod = "Custom"; await state.Gameplay.PendingRefresh;
            state.Gameplay.CustomFrom = "not a date"; await state.Gameplay.ApplyDatesCommand.ExecuteAsync(null);
            Assert.Contains("YYYY-MM-DD", state.Gameplay.Problem); Assert.False(state.Gameplay.HasData);
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public async Task Fullscreen_gameplay_retry_cancel_and_resume_are_controller_actions()
    {
        using var library = CreateLibrary(); await library.LoadCommand.ExecuteAsync(null);
        var repository = new RecordingRepository { Fail = true };
        using var services = new ServiceCollection().AddSingleton<IGameplayStatsRepository>(repository).BuildServiceProvider();
        using var context = new FullscreenContext(library, PreviewData.Feed, PreviewData.Shell, services);
        using var page = new FullscreenLibrarySummaryPage(context);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        try
        {
            window.Show(); await page.PendingRefresh; Dispatcher.UIThread.RunJobs();
            Assert.NotNull(page.Stats.Gameplay.Problem);
            repository.Fail = false;
            var held = repository.Held = new TaskCompletionSource<GameplayStats>(TaskCreationOptions.RunContinuationsAsynchronously);
            Press("Try again"); Dispatcher.UIThread.RunJobs();
            Assert.True(page.Stats.Gameplay.IsLoading);
            Press("Cancel"); Dispatcher.UIThread.RunJobs();
            Assert.False(page.Stats.Gameplay.IsLoading); Assert.Contains("Reading stopped", page.Stats.Gameplay.Problem);
            repository.Held = null;
            Press("Try again"); await page.PendingRefresh; Dispatcher.UIThread.RunJobs();
            Assert.True(page.Stats.Gameplay.HasData); Assert.Null(page.Stats.Gameplay.Problem);
            held.TrySetResult(new GameplayStats()); Dispatcher.UIThread.RunJobs();
            Assert.True(page.Stats.Gameplay.HasData);
            void Press(string label)
            {
                var button = window.GetVisualDescendants().OfType<Button>().Single(x => x.Content as string == label);
                button.Focus(); page.Handle(GamepadButtons.Accept);
            }
        }
        finally { repository.Held?.TrySetResult(new GameplayStats()); window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(1d)]
    [InlineData(1.4d)]
    public async Task Fullscreen_shell_applies_native_section_style_and_real_text_scale(double textScale)
    {
        using var library = CreateLibrary(); await library.LoadCommand.ExecuteAsync(null);
        using var services = new ServiceCollection().AddSingleton<IGameplayStatsRepository>(new RecordingRepository())
            .AddSingleton<IAccountStatsRepository>(new SpendingRepository()).BuildServiceProvider();
        using var context = new FullscreenContext(library, PreviewData.Feed, PreviewData.Shell, services) { TextScale = textScale };
        using var shell = new FullscreenView(context);
        var window = new Window { Width = 1920, Height = 1080, Content = shell };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var page = new FullscreenLibrarySummaryPage(context);
            context.Push(page); await page.PendingRefresh; Flush();
            if (page.Stats.Gameplay.IsLoading) { await page.PendingRefresh; Flush(); }
            Assert.Same(page, shell.CurrentPage); Assert.True(page.Stats.Gameplay.HasData);
            var section = page.GetVisualDescendants().OfType<Button>().Single(x => x.Content as string == "Gameplay");
            Assert.Contains("current", section.Classes);
            Assert.Equal(3, section.BorderThickness.Bottom);
            Assert.Equal(28 * textScale, section.FontSize, 3);
            var chartCaption = page.GetVisualDescendants().OfType<TextBlock>().Single(x => x.Text == "Recorded hours");
            Assert.Equal(24 * textScale, chartCaption.FontSize, 3);
            Capture("overview");
            ScrollTo("Recorded hours over time"); Capture("hours");
            ScrollTo("Your library today"); Capture("library");
            page.Stats.Gameplay.SelectedPeriod = "Custom"; await page.PendingRefresh; Flush();
            window.FocusManager?.ClearFocus(); Scroll().Offset = default; Flush(); Capture("custom");
            Assert.True(Scroll().Extent.Width <= Scroll().Viewport.Width + 1);
            ScrollViewer Scroll() => page.GetVisualDescendants().OfType<ScrollViewer>().Where(x => x.IsEffectivelyVisible)
                .OrderByDescending(x => x.Viewport.Height).First();
            void ScrollTo(string heading)
            {
                window.FocusManager?.ClearFocus();
                var scroll = Scroll();
                var text = page.GetVisualDescendants().OfType<TextBlock>().Single(x => x.Text == heading);
                scroll.Offset = new Vector(0, scroll.Offset.Y + text.TranslatePoint(default, scroll)!.Value.Y - 20); Flush();
            }
            void Flush() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
            void Capture(string sectionName)
            {
                if (Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is not { } directory) return;
                Directory.CreateDirectory(directory);
                using var frame = window.CaptureRenderedFrame();
                frame?.Save(Path.Combine(directory, $"gameplay-fullshell-{textScale * 100:0}-{sectionName}.png"));
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(1024)]
    [InlineData(600)]
    public async Task Spending_header_keeps_figures_above_the_fold(int width)
    {
        using var library = CreateLibrary();
        using var state = new StatsViewModel(new AccountStatsViewModel(new SpendingRepository()), new GameplayStatsViewModel(new RecordingRepository(), library)) { IsSpending = true };
        var view = new StatsView { DataContext = state };
        var window = new Window { Width = width, Height = 760, Content = view, Background = (IBrush)view.FindResource("Ground")! };
        try
        {
            window.Show(); await state.ActivateAsync(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var dashboard = window.GetVisualDescendants().OfType<AccountStatsDashboard>().Single();
            Assert.InRange(dashboard.TranslatePoint(default, view)!.Value.Y, 0, width == 1024 ? 170 : 220);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), x => x.IsEffectivelyVisible && x.Text == "Steam account");
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), x => x.IsEffectivelyVisible && x.Text == state.Spending.IntroMessage);
            var refresh = window.GetVisualDescendants().OfType<Button>().Single(x => x.Content as string == "Refresh Steam spending");
            Assert.True(refresh.Focus());
            if (Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is { } directory)
            {
                Directory.CreateDirectory(directory);
                using var frame = window.CaptureRenderedFrame(); frame?.Save(Path.Combine(directory, $"stats-compact-spending-{width}.png"));
            }
        }
        finally { window.Close(); }
    }

    private static LibraryViewModel CreateLibrary() => new(new PreviewLibraryQueryRepository(), new PreviewOwnershipRepository(),
        new PreviewReleaseRepository(), new PreviewWorkRepository(), new PreviewUpdateEventRepository());
    private sealed class RecordingRepository : IGameplayStatsRepository
    {
        public GameplayStatsRequest? Last { get; private set; }
        public bool Fail { get; set; }
        public TaskCompletionSource<GameplayStats>? Held { get; set; }
        public Task<GameplayStats> GetAsync(GameplayStatsRequest request, CancellationToken ct = default)
        {
            Last = request;
            if (Fail) throw new InvalidOperationException();
            if (Held is { } held) return held.Task;
            return new PreviewGameplayStatsRepository().GetAsync(request, ct);
        }
    }
    private sealed class SpendingRepository : IAccountStatsRepository
    {
        public Task<AccountStats> GetAsync(string source, CancellationToken ct = default) => Task.FromResult(new AccountStats
        {
            Source = source, TransactionCount = 1, GrossProductTransactionCount = 1, GrossProductSpendCents = 2000,
            Currencies = [new("$", 1)], Purchases = new(1, 2000), SpendByYear = [new(2026, 1, 2000)]
        });
    }
}
