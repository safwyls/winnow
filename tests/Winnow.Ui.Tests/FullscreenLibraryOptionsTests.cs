using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views.Fullscreen;
using Winnow.Data.Repositories;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class FullscreenLibraryOptionsTests
{
    [AvaloniaTheory]
    [InlineData(null)]
    [InlineData("My lists")]
    [InlineData("Filter & sort")]
    public async Task Options_and_tools_return_to_the_same_game_and_viewport_deep_in_a_large_library(string? tool)
    {
        using var fixture = new Fixture();
        await fixture.Library.LoadCommand.ExecuteAsync(null);
        Assert.Equal(900, fixture.Library.AllTiles.Count);
        using var shell = new FullscreenView(fixture.Context);
        var window = new Window { Width = 1920, Height = 1080, Content = shell };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            shell.Handle(GamepadButtons.Next); Dispatcher.UIThread.RunJobs();
            var library = Assert.IsType<FullscreenBrowsePage>(shell.CurrentPage);
            Assert.Equal("Library", library.Title);
            Assert.Contains("Y  Library options", library.Hints);
            var viewport = Viewport(library);
            Assert.True(Cards(viewport.GetRow(0))[2].Focus());
            for (var row = 0; row < 60; row++)
            {
                shell.Handle(GamepadButtons.Down);
                viewport.AdvanceAnimation(TimeSpan.FromMilliseconds(220));
            }
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            viewport = Viewport(library);
            var firstRow = viewport.FirstRow;
            Assert.True(firstRow >= 50);
            var selected = Assert.IsType<Button>(window.FocusManager!.GetFocusedElement());
            var selectedName = AutomationProperties.GetName(selected);
            var selectedColumn = Grid.GetColumn(selected);
            Assert.Equal(2, selectedColumn);

            shell.Handle(GamepadButtons.Keyboard); Dispatcher.UIThread.RunJobs();
            var options = Assert.IsType<FullscreenActionsPage>(shell.CurrentPage);
            Assert.Equal("Library options", options.Title);
            Assert.Same(viewport, Viewport(library));
            Assert.Equal(firstRow, viewport.FirstRow);
            Assert.False(library.IsEffectivelyEnabled);
            var actions = options.GetVisualDescendants().OfType<Button>().ToArray();
            Assert.Equal("My lists", AutomationProperties.GetName(actions[0]));
            Assert.Equal("Filter & sort", AutomationProperties.GetName(actions[1]));
            Assert.Contains(actions, button => AutomationProperties.GetName(button) == "Add selected game to list");
            Assert.Same(actions[0], window.FocusManager.GetFocusedElement());
            for (var repeat = 0; repeat < 4; repeat++) shell.Handle(GamepadButtons.Keyboard);
            Assert.Same(options, shell.CurrentPage);

            if (tool is not null)
            {
                if (tool == "Filter & sort") shell.Handle(GamepadButtons.Down);
                shell.Handle(GamepadButtons.Accept); Dispatcher.UIThread.RunJobs();
                Assert.Equal(tool, shell.CurrentPage.Title);
                Assert.IsNotType<FullscreenActionsPage>(shell.CurrentPage);
            }
            else
            {
                fixture.Context.ReducedMotion = true;
                Dispatcher.UIThread.RunJobs();
                Capture(window);
            }

            shell.Handle(GamepadButtons.Back); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Same(library, shell.CurrentPage);
            Assert.True(library.IsEffectivelyEnabled);
            Assert.Equal(firstRow, Viewport(library).FirstRow);
            var restored = Assert.IsType<Button>(window.FocusManager.GetFocusedElement());
            Assert.Equal(selectedName, AutomationProperties.GetName(restored));
            Assert.Equal(selectedColumn, Grid.GetColumn(restored));

            shell.Handle(GamepadButtons.Menu); Dispatcher.UIThread.RunJobs();
            Assert.Equal("Quick menu", shell.CurrentPage.Title);
            shell.Handle(GamepadButtons.Back); Dispatcher.UIThread.RunJobs();
            Assert.Same(library, shell.CurrentPage);
            Assert.Equal(firstRow, Viewport(library).FirstRow);
            Assert.Equal(selectedName, AutomationProperties.GetName(Assert.IsType<Button>(window.FocusManager.GetFocusedElement())));
        }
        finally { window.Close(); }
    }

    private static FullscreenRowViewport Viewport(Control page) => Assert.Single(page.GetVisualDescendants().OfType<FullscreenRowViewport>());
    private static Button[] Cards(Control row) => row.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("tv-cover")).ToArray();

    private static void Capture(Window window)
    {
        if (Environment.GetEnvironmentVariable("WINNOW_SCREENSHOT_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, "fullscreen-library-options.png"));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly TempDatabase _database = new();
        public LibraryViewModel Library { get; }
        public FullscreenContext Context { get; }

        public Fixture()
        {
            LibraryReadFixtures.Seed(_database, 900);
            Library = new LibraryViewModel(new LibraryQueryRepository(_database.Factory), new OwnershipRepository(_database.Factory),
                new ReleaseRepository(_database.Factory), new WorkRepository(_database.Factory), new UpdateEventRepository(_database.Factory));
            Context = new FullscreenContext(Library, new FeedViewModel(new PreviewFeedService(), Library), PreviewData.Shell);
        }

        public void Dispose()
        {
            Context.Dispose();
            _database.Dispose();
        }
    }
}
