using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Reactive;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Services;
using Winnow.App.Themes;
using Winnow.App.ViewModels;

namespace Winnow.App.Views;

/// <summary>
/// The library shell's behaviour: the §8 keyboard floor (arrows across the
/// grid, <c>/</c> to search, <c>Enter</c> to open the selection, <c>Escape</c>
/// to close), tile selection, and list-view selection. The cover wall's own
/// geometry lives in <see cref="CoverWall"/>, which divides the width it is
/// measured with — the view no longer computes a cell size and pushes it into a
/// layout object that is measured separately.
/// </summary>
public partial class MainWindow : Window
{
    private LibraryViewModel? _library;
    private MainWindowViewModel? _shell;
    private ThemeService? _theme;
    private bool _backdropSubscribed;
    private bool _chromeReady;
    private DateTime _lastTitleBarPress = DateTime.MinValue;
    private PixelPoint _lastTitleBarPoint;

    public MainWindow()
    {
        InitializeComponent();

        DetailsPanel.CloseRequested += (_, _) => _library?.CloseDetailsCommand.Execute(null);

        // Tunnel, not bubble: this handler must see a press before the buttons
        // on a turned card's back face do. See OnTilePressed.
        TileWall.AddHandler(PointerPressedEvent, OnTilePressed, RoutingStrategies.Tunnel);

        RequestBackdrop();

        // WindowState can be written before the caption's controls exist, so
        // the state handler stays inert until the tree is up.
        _chromeReady = true;
        UpdateWindowStateChrome();
    }

    // ══ Window chrome ═══════════════════════════════════════════════════════
    // The client area is extended over the decorations, so everything Windows
    // used to do for the caption is done here. Each of these is load-bearing:
    // drop one and the window reads as broken rather than as styled.

    /// <summary>
    /// Asks Windows for the backdrop the user's preference needs, and reports
    /// back what it actually got.
    /// </summary>
    private void RequestBackdrop()
    {
        TransparencyLevelHint = _theme?.TransparencyRequested != true
            ? [WindowTransparencyLevel.None]
            : _theme.Backdrop == WinnowBackdrop.Mica
                ?
                [
                    WindowTransparencyLevel.Mica,
                    WindowTransparencyLevel.AcrylicBlur,
                    WindowTransparencyLevel.None,
                ]
                :
                [
                    WindowTransparencyLevel.AcrylicBlur,
                    WindowTransparencyLevel.Mica,
                    WindowTransparencyLevel.None,
                ];

        ApplyBackdrop();

        if (_backdropSubscribed)
        {
            return;
        }

        _backdropSubscribed = true;
        this.GetObservable(ActualTransparencyLevelProperty)
            .Subscribe(new AnonymousObserver<WindowTransparencyLevel>(_ => ApplyBackdrop()));
    }

    /// <summary>
    /// Paints the window's background and tells the theme service which backdrop
    /// the platform actually granted.
    /// </summary>
    private void ApplyBackdrop()
    {
        var active =
            ActualTransparencyLevel == WindowTransparencyLevel.Mica ? WinnowBackdrop.Mica
            : ActualTransparencyLevel == WindowTransparencyLevel.AcrylicBlur ? WinnowBackdrop.Acrylic
            : ActualTransparencyLevel == WindowTransparencyLevel.Blur ? WinnowBackdrop.Acrylic
            : WinnowBackdrop.None;

        _theme?.SetActiveBackdrop(active);

        Background = active is WinnowBackdrop.Acrylic or WinnowBackdrop.Mica
            && _theme?.TransparencyRequested == true
            ? Brushes.Transparent
            : Token("ShellGround", Brushes.Black);
    }

    /// <summary>
    /// The theme service repainted the resource dictionary; re-request the
    /// backdrop and invalidate the visual tree.
    /// </summary>
    private void OnThemeApplied(object? sender, EventArgs e)
    {
        RequestBackdrop();
        InvalidateTree(this);
    }

    private static void InvalidateTree(Visual visual)
    {
        visual.InvalidateVisual();
        foreach (var child in visual.GetVisualChildren())
        {
            InvalidateTree(child);
        }
    }

    /// <summary>
    /// A brush out of <c>tokens.axaml</c> by key. The fallback is never expected
    /// and never silently pretty: a missing token should look wrong here rather
    /// than resolve to something plausible and hide the mistake.
    /// </summary>
    private IBrush Token(string key, IBrush fallback)
        => this.TryFindResource(key, ActualThemeVariant, out var found) && found is IBrush brush
            ? brush
            : fallback;

    /// <summary>
    /// Drag moves the window; a double press maximises or restores it.
    /// </summary>
    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var at = this.PointToScreen(e.GetPosition(this));
        var now = DateTime.UtcNow;

        // Screen coordinates, not window coordinates: after a drag the pointer
        // sits at the same place *in the title bar* it started from, and a
        // window-relative test would read the next click as a double.
        var repeat = now - _lastTitleBarPress < TimeSpan.FromMilliseconds(500)
            && Math.Abs(at.X - _lastTitleBarPoint.X) <= 8
            && Math.Abs(at.Y - _lastTitleBarPoint.Y) <= 8;

        if (e.ClickCount >= 2 || repeat)
        {
            _lastTitleBarPress = DateTime.MinValue;
            ToggleMaximised();
            e.Handled = true;
            return;
        }

        _lastTitleBarPress = now;
        _lastTitleBarPoint = at;

        BeginMoveDrag(e);
    }

    private void OnMinimisePressed(object? sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void OnMaximisePressed(object? sender, RoutedEventArgs e)
        => ToggleMaximised();

    private void OnClosePressed(object? sender, RoutedEventArgs e) => Close();

    private void ToggleMaximised()
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (_chromeReady && change.Property == WindowStateProperty)
        {
            UpdateWindowStateChrome();
        }
    }

    /// <summary>
    /// The middle caption button says what it will do, not what state the
    /// window is in: a maximised window offers "Restore down" and draws the
    /// two-square glyph, exactly as the system chrome it replaced did.
    /// </summary>
    private void UpdateWindowStateChrome()
    {
        var maximised = WindowState == WindowState.Maximized;

        MaximiseGlyph.IsVisible = !maximised;
        RestoreGlyph.IsVisible = maximised;
        ToolTip.SetTip(MaximiseButton, maximised ? "Restore down" : "Maximise");
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_library is not null)
        {
            _library.PropertyChanged -= OnLibraryPropertyChanged;
        }

        if (_theme is not null)
        {
            _theme.Applied -= OnThemeApplied;
        }

        _shell = DataContext as MainWindowViewModel;
        _library = _shell?.Library;

        // The theme service reaches the window through the shell rather than
        // through the container, so the window keeps one source of state and a
        // test that builds a MainWindowViewModel by hand gets a working one.
        _theme = _shell?.Appearance.Service;
        if (_theme is not null)
        {
            _theme.Applied += OnThemeApplied;
            RequestBackdrop();
        }

        if (_library is not null)
        {
            _library.PropertyChanged += OnLibraryPropertyChanged;
        }
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (_library is { } library)
        {
            await library.LoadCommand.ExecuteAsync(null);
        }

        // The rail's REVIEW count has to be right before the user looks at it,
        // so the queue loads with the window rather than on first visit.
        if (_shell?.MergeQueue is { } queue)
        {
            await queue.LoadCommand.ExecuteAsync(null);
        }

        // §8's dimming preference. After the library, deliberately: it is one
        // row out of a settings table and the wall is already built, so reading
        // it late costs a repaint of the visible tiles rather than delaying the
        // first paint of the grid behind an IO round trip.
        if (_shell?.Display is { } display)
        {
            await display.LoadAsync();
        }

        // M8, and LAST on purpose. The scoring pass is ~60 ms over a thousand
        // games (Winnow.App.Services.IFeedService carries the measurement), and
        // it needs the library's tiles to exist before it can build a card —
        // the feed renders the library's own tiles rather than a second
        // projection of them (IGameTileSource).
        //
        // Nothing above it waits on it: the window is up, the wall is built and
        // the rail is populated by the time this runs, and the feed says what it
        // is doing until it answers. The service moves the work off this thread;
        // awaiting here only sequences it after the reads it depends on.
        if (_shell?.Feed is { } feed)
        {
            await feed.LoadCommand.ExecuteAsync(null);
        }

#if DEBUG
        // The window now OPENS on the feed (M8), so every capture flag that
        // wants a library state has to say so — otherwise the shell is showing
        // two panes at once, which is a state the rail cannot produce and the
        // screenshot cannot be read. Through the command rather than the flag,
        // exactly as --open-stores has always done, so each screen reads its
        // state on the way in like a real click.
        if (_shell is not null && Environment.GetCommandLineArgs()
                .Any(a => a is "--open-library" or "--open-list" or "--open-filters"
                    or "--open-first-list" or "--open-live-list" or "--grid-probe"
                    || a.StartsWith("--check=", StringComparison.Ordinal)
                    || a.StartsWith("--filters-scroll=", StringComparison.Ordinal)))
        {
            _shell.ShowLibraryCommand.Execute(null);
        }

        // --open-queue lands on the merge confirm queue instead of the library,
        // so the screen can be captured and reviewed without driving the rail.
        // Debug-only, same convention as --seed-sample.
        if (_shell is not null && Environment.GetCommandLineArgs().Contains("--open-queue"))
        {
            _shell.ToggleMergeQueueCommand.Execute(null);
        }

        // Same convention and same reason as --open-queue: the Stores panel has
        // to be reviewable in a screenshot, and driving the rail by an injected
        // click is not reliable enough to trust (SetForegroundWindow fails
        // silently). Goes through the command rather than the flag so the panel
        // reads its state on the way in, exactly as a real click would.
        if (_shell is not null && Environment.GetCommandLineArgs().Contains("--open-stores"))
        {
            await _shell.ShowStoresCommand.ExecuteAsync(null);
        }

        // §5.1's ramp has to be reviewable in a screenshot on a machine whose
        // owner has turned dimming off. Written onto the RAMP rather than
        // through the Display view model, because that view model's setter is
        // also the thing that persists the preference — and a capture must
        // never leave a setting behind in somebody's real library.
        if (_library is not null && Environment.GetCommandLineArgs().Contains("--dim-covers"))
        {
            _library.Ramp.DimsDormantCovers = true;
        }

        // Same convention: the Appearance screen has to be reviewable without
        // driving the rail.
        if (_shell is not null && Environment.GetCommandLineArgs().Contains("--open-appearance"))
        {
            _shell.ShowAppearanceCommand.Execute(null);
        }

        if (_library is not null && Environment.GetCommandLineArgs().Contains("--open-list"))
        {
            _library.ShowListViewCommand.Execute(null);
        }

        // --open-filters and --open-first-list land the window on the two
        // screens the filter and list work has to be reviewed on, without
        // driving the rail by injected clicks. Same convention and same reason
        // as --open-queue: SetForegroundWindow is not reliable enough here to
        // trust a synthetic click, and a screenshot of the wrong screen is worse
        // than no screenshot.
        if (_library is not null && Environment.GetCommandLineArgs().Contains("--open-filters"))
        {
            _library.Filters.IsOpen = true;
        }

        if (_library is not null && Environment.GetCommandLineArgs().Contains("--open-first-list"))
        {
            _library.OpenListCommand.Execute(_library.Lists.Lists.FirstOrDefault());
        }

        if (_library is not null && Environment.GetCommandLineArgs().Contains("--open-live-list"))
        {
            _library.OpenListCommand.Execute(_library.Lists.LiveLists.FirstOrDefault());
        }

        // --check=<group>:<label> ticks one facet option through the same
        // property the checkbox writes, so a screenshot can show a rule the
        // USER set sitting beside one an open live list contributed — which is
        // the only way to review whether the two are told apart on screen.
        foreach (var arg in Environment.GetCommandLineArgs()
            .Where(a => a.StartsWith("--check=", StringComparison.Ordinal)))
        {
            var spec = arg["--check=".Length..].Split(':', 2);
            if (spec.Length != 2 || _library is null)
            {
                continue;
            }

            var option = _library.Filters.Groups
                .FirstOrDefault(g => g.Key == spec[0])?
                .AllOptions.FirstOrDefault(o => o.Label == spec[1]);

            if (option is not null)
            {
                option.IsChecked = true;
            }
        }

        // --filters-scroll=N puts the panel's own scroll at N px, because
        // FEATURES and CONTROLLER sit below the fold on an 820px window and a
        // screenshot cannot show them working otherwise. Posted at Background
        // priority: the panel has to have been measured before its extent
        // exists, and setting Offset on a ScrollViewer with a zero extent is
        // silently a no-op.
        if (Environment.GetCommandLineArgs()
                .FirstOrDefault(a => a.StartsWith("--filters-scroll=", StringComparison.Ordinal))
            is { } scrollArg
            && double.TryParse(
                scrollArg["--filters-scroll=".Length..],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var offset))
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(
                () => FilterPanel.ScrollTo(offset),
                Avalonia.Threading.DispatcherPriority.Background);
        }

        // --appearance-scroll=N, the same device for the same reason one level
        // over: YOUR THEMES sits under a row of theme cards, so the folder, the
        // contrast report and the validation output are all below the fold on
        // an 820px window and cannot be captured without a scroll. Background
        // priority for the same reason too — the screen has to have been
        // measured before its extent exists.
        if (Environment.GetCommandLineArgs()
                .FirstOrDefault(a => a.StartsWith("--appearance-scroll=", StringComparison.Ordinal))
            is { } appearanceScroll
            && double.TryParse(
                appearanceScroll["--appearance-scroll=".Length..],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var appearanceOffset))
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(
                () => AppearancePanel.ScrollTo(appearanceOffset),
                Avalonia.Threading.DispatcherPriority.Background);
        }
#endif
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Handled)
        {
            return;
        }

        // The cut bar's prompt is the shallowest layer that can still swallow
        // Escape: it is asking a question, and backing out of a question must
        // work from wherever the caret happens to be. It sits ABOVE the detail
        // modal in this chain only because the two are never up together.
        if (_library?.Prompt is { } prompt && e.Key == Key.Escape)
        {
            prompt.CancelCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // The detail modal is modal: it answers first, and it answers Escape
        // from anywhere — including from inside the search box (§8).
        if (_library is { IsDetailsOpen: true })
        {
            if (e.Key == Key.Escape)
            {
                _library.CloseDetailsCommand.Execute(null);
                e.Handled = true;
            }

            return;
        }

        if (_shell is { IsMergeQueueVisible: true })
        {
            OnMergeQueueKeyDown(e);
            return;
        }

        // The settings surface answers Escape the same way the merge queue
        // does: give the library back. It answers nothing else. It has no
        // selection to walk and no one-key shortcuts, so every other key
        // belongs to whatever control has focus, which on Stores is a
        // selectable command line.
        if (_shell is { IsSettingsVisible: true })
        {
            if (e.Key == Key.Escape)
            {
                _shell.ShowLibraryCommand.Execute(null);
                e.Handled = true;
            }

            return;
        }

        // The Feed answers the arrow keys and nothing else. Its cards are real
        // buttons, so Tab, Enter and Space are already the framework's — what
        // the window supplies is the SHAPE (left/right along the reading order,
        // up/down by one row of whatever the width fits, §8's keyboard floor on
        // a surface the wall's walk knows nothing about).
        //
        // The early return is the load-bearing half: without it every arrow key
        // pressed on the feed would ALSO walk the library's selection behind it,
        // moving a highlight the user cannot see on a screen they are not
        // looking at — and Enter would then open the modal for whichever tile
        // that walk had landed on.
        if (_shell is { IsFeedVisible: true })
        {
#if DEBUG
            // The wall's probe, pointed at the Feed, and it needs a flag of its
            // own: --grid-probe is in the list above that forces the LIBRARY up,
            // because the wall cannot be probed from a screen it is not on. The
            // Feed has the opposite requirement, which is the whole reason the
            // two flags are two flags.
            if (e.Key == Key.F9 && Environment.GetCommandLineArgs().Contains("--feed-probe"))
            {
                FeedPanel.DumpDiagnostics();
                e.Handled = true;
                return;
            }
#endif

            // The inspection surface is the shallowest layer on this screen, so
            // it answers Escape first — and it is the only thing Escape does
            // here. Closing it puts the shelves back; the Feed itself is left
            // up, because the user asked to leave a list and not to leave the
            // screen the list belongs to.
            if (_shell.Feed.IsHistoryOpen)
            {
                if (e.Key == Key.Escape)
                {
                    _shell.Feed.CloseHistoryCommand.Execute(null);
                    e.Handled = true;
                }

                // No arrow walk over it: its rows are ordinary controls in the
                // window's tree, so Tab already reaches every title and every
                // Undo in reading order, and a second walk laid over that would
                // move focus twice per press.
                return;
            }

            if (FeedPanel.HandleNavigationKey(e))
            {
                e.Handled = true;
            }

            return;
        }

        if (_library is null)
        {
            return;
        }

        // ── While the caret is in a field, the keyboard belongs to the field ──
        // This used to test SearchBox alone, which was correct while it was the
        // only text box on the screen. It no longer is: the filter panel has a
        // find field per long group and two year fields, and every one of them
        // would otherwise have its letters eaten as shortcuts — typing "f" into
        // "Find a tag" would close the panel the user was typing into.
        //
        // Escape still means "give me the library back", so it is answered even
        // from inside a field; the search box additionally clears itself first,
        // because that is the field whose content IS a filter.
        if (FocusedTextBox() is { } focused)
        {
            if (e.Key != Key.Escape)
            {
                return;
            }

            if (ReferenceEquals(focused, SearchBox) && SearchBox.Text is { Length: > 0 })
            {
                SearchBox.Text = string.Empty;
            }
            else
            {
                UnwindCut();
            }

            e.Handled = true;
            return;
        }

        // Up/down walks rows in list view and whole rows of tiles in the grid;
        // the two views are the same sequence read at different widths.
        var verticalStep = _library.IsGridView ? TileWall.Columns : 1;

        switch (e.Key)
        {
            case Key.Oem2 or Key.Divide:
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
                break;

            // The filter panel opens and closes on the same key, like the rail
            // rows it sits beside. F rather than Ctrl+F, which every application
            // on the machine has already spent on find-in-page — and "/" is
            // already the search box here.
            case Key.F:
                _library.Filters.ToggleCommand.Execute(null);
                e.Handled = true;
                break;

            // Escape unwinds the cut one layer at a time, outermost first: the
            // panel, then the filters in it, then the list, then the bucket.
            // One key, and every press visibly does something.
            case Key.Escape:
                UnwindCut();
                e.Handled = true;
                break;

            // Reordering a hand-built list. Alt+arrows rather than drag and
            // drop: the rows are virtualized, a drag across four hundred of them
            // is a scroll fight, and §8 asks for the whole interface to be
            // reachable without a pointer regardless.
            case Key.Up when e.KeyModifiers.HasFlag(KeyModifiers.Alt):
                _ = _library.MoveInListAsync(-1);
                e.Handled = true;
                break;

            case Key.Down when e.KeyModifiers.HasFlag(KeyModifiers.Alt):
                _ = _library.MoveInListAsync(1);
                e.Handled = true;
                break;

            case Key.Left:
                MoveSelection(-1);
                e.Handled = true;
                break;

            case Key.Right:
                MoveSelection(1);
                e.Handled = true;
                break;

            case Key.Up:
                MoveSelection(-verticalStep);
                e.Handled = true;
                break;

            case Key.Down:
                MoveSelection(verticalStep);
                e.Handled = true;
                break;

            // §8: the flip has to be reachable without a pointer, or the actions
            // on the back of the card are mouse-only. Space is the key every
            // toolkit already spends on "act on the selected thing"; a focused
            // button answers it first and marks it handled, so pressing Space on
            // Play launches rather than turning the card back.
            case Key.Space:
                FlipSelectedTile();
                e.Handled = true;
                break;

            case Key.Enter:
                // §5.3 caps the tile at four facts; Enter is how you get the
                // rest. It stays the keyboard route to the modal even though the
                // back face now carries a Details button too — §10 names Enter
                // and a double click as the two ways in, and the flip took the
                // pointer one.
                _library.OpenDetailsCommand.Execute(_library.SelectedTile);
                e.Handled = true;
                break;

#if DEBUG
            // --grid-probe writes the wall's realized cell rects to
            // %TEMP%\winnow-grid-debug.txt. The dead-space bug was invisible in
            // a screenshot until you could read the anchor's cell position, so
            // the probe that found it stays reachable.
            case Key.F9 when Environment.GetCommandLineArgs().Contains("--grid-probe"):
                DumpGridDiagnostics();
                e.Handled = true;
                break;
#endif
        }
    }

#if DEBUG
    private void DumpGridDiagnostics()
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine($"scroll offset={GridScroll.Offset} viewport={GridScroll.Viewport} extent={GridScroll.Extent}");
        text.AppendLine($"wall bounds={TileWall.Bounds} desired={TileWall.DesiredSize} columns={TileWall.Columns} tiles={_library?.VisibleTiles.Count}");

        foreach (var child in TileWall.Children)
        {
            if (child.IsVisible && child.DataContext is GameTileViewModel tile)
            {
                text.AppendLine($"  {child.Bounds} {tile.Title}");
            }
        }

        File.AppendAllText(
            Path.Combine(Path.GetTempPath(), "winnow-grid-debug.txt"),
            $"=== {DateTime.Now:HH:mm:ss.fff} ===\n{text}\n");
    }
#endif

    /// <summary>
    /// The §8 keyboard floor for the merge confirm queue: arrows walk the
    /// pairs, <c>S</c>/<c>Enter</c> answers "Same game", <c>D</c> answers
    /// "Different games", <c>Escape</c> goes back to the library. Both answers
    /// are one key because the queue's whole job is to be cleared — but they
    /// are different keys, never one key with a modifier, because "different
    /// games" is permanent.
    /// </summary>
    private void OnMergeQueueKeyDown(KeyEventArgs e)
    {
        if (_shell?.MergeQueue is not { } queue)
        {
            return;
        }

        // Escape leaves from either surface; everything else acts on a card,
        // and a card is only on screen while the review segment is up.
        // Answering now merges the pair, so an S pressed over the history
        // list would write to the library on a card the user cannot see.
        if (e.Key == Key.Escape)
        {
            _shell.ShowLibraryCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (!queue.IsReviewVisible)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Up:
                MergeQueue.ScrollIntoView(queue.MoveSelection(-1));
                e.Handled = true;
                break;

            case Key.Down:
                MergeQueue.ScrollIntoView(queue.MoveSelection(1));
                e.Handled = true;
                break;

            case Key.S or Key.Enter:
                queue.SameGameCommand.Execute(queue.SelectedCandidate);
                e.Handled = true;
                break;

            case Key.D:
                queue.DifferentGamesCommand.Execute(queue.SelectedCandidate);
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Escape's one job on the library screen: give the user back the library,
    /// one visible step per press. The order is outside-in — the panel is
    /// chrome, an unsaved edit to a live list is the newest thing on top of it,
    /// the filters are rules, and the list and the bucket are where you are
    /// standing — so no press is ever a no-op while anything is still cutting
    /// the grid.
    /// </summary>
    /// <summary>The text box holding focus, or null. Nothing else may claim a letter key.</summary>
    private TextBox? FocusedTextBox()
        => TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as TextBox;

    private void UnwindCut()
    {
        if (_library is null)
        {
            return;
        }

        // A turned card is the newest and shallowest thing on the screen — it is
        // one click old and it is not a cut of the library at all — so it is the
        // first thing Escape gives back. Focus comes with it, or the next press
        // would be answered by a button that is no longer showing.
        if (_library.FlippedTile is not null)
        {
            _library.ClearFlip();
            TakeGridFocus();
            return;
        }

        if (_library.Filters.IsOpen)
        {
            _library.Filters.IsOpen = false;
            return;
        }

        // Inside a live list, an unsaved edit is the outermost thing the user
        // added, so it unwinds first — and it unwinds by NAME, to the rules the
        // list was saved with, rather than by clearing the panel. Clearing the
        // panel there would not be a step back out: it would be a fourth,
        // emptier version of the list, still labelled as the list.
        if (_library.IsLiveListEdited)
        {
            _library.RevertLiveListCommand.Execute(null);
            return;
        }

        // A manual list's panel rules are the user's own (§12.2), so they clear
        // before the list does. A LIVE list's are the list's, and leaving takes
        // them — so this layer is skipped and the next one does both at once.
        if (_library.Filters.HasSelection && _library.Lists.Open is not { IsLive: true })
        {
            _library.Filters.ClearCommand.Execute(null);
            return;
        }

        if (_library.Lists.Open is not null)
        {
            _library.CloseListCommand.Execute(null);
            return;
        }

        if (_library.SelectedBucket is not null)
        {
            _shell?.SelectBucketCommand.Execute(null);
        }
    }

    /// <summary>
    /// A click turns the card over; a double click opens the detail modal.
    /// Registered on the tunnel route so the double click is caught before the
    /// back face's buttons.
    /// </summary>
    private void OnTilePressed(object? sender, PointerPressedEventArgs e)
    {
        if (_library is null
            || e.Source is not Control source
            || source.FindAncestorOfType<GameTileView>(includeSelf: true) is not
                { DataContext: GameTileViewModel tile })
        {
            return;
        }

        if (e.ClickCount >= 2)
        {
            // Takes the press away from the back face before it is offered one.
            _library.OpenDetailsCommand.Execute(tile);
            e.Handled = true;
            return;
        }

        if (!e.GetCurrentPoint(source).Properties.IsLeftButtonPressed)
        {
            _library.SelectTile(tile);
            return;
        }

        // A press on one of the back's own controls belongs to that control.
        if (source.FindAncestorOfType<Button>(includeSelf: true) is not null)
        {
            _library.SelectTile(tile);
            return;
        }

        _library.FlipTileCommand.Execute(tile);
    }

    /// <summary>
    /// The journal card's note field. Enter saves, Escape dismisses.
    /// </summary>
    private void OnJournalNoteKeyDown(object? sender, KeyEventArgs e)
    {
        if (_library?.Journal is not { IsOpen: true } journal)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                journal.SaveCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Escape:
                journal.DismissCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void OnDisplayFlyoutOpened(object? sender, EventArgs e)
    {
        if (sender is not Flyout { Content: Control content })
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                var toggle = content as CheckBox
                    ?? content.GetVisualDescendants().OfType<CheckBox>().FirstOrDefault();
                toggle?.Focus(NavigationMethod.Tab);
            },
            DispatcherPriority.Input);
    }

    /// <summary>Sort menu row. Sets the shared order, then closes the flyout.</summary>
    private void OnSortItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: SortOptionViewModel option })
        {
            _library?.SelectSortCommand.Execute(option);
        }

        SortButton.Flyout?.Hide();
    }

    /// <summary>
    /// The list is the one view that can hold more than one selection (§6), so
    /// the per-item flag the Volt edge reads has to follow the whole set rather
    /// than only the anchor the view model tracks. This runs after the anchor
    /// has been written, so it is the last word on which rows are marked.
    /// </summary>
    private void OnListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        foreach (var removed in e.RemovedItems)
        {
            if (removed is GameTileViewModel tile)
            {
                tile.IsSelected = false;
            }
        }

        foreach (var added in e.AddedItems)
        {
            if (added is GameTileViewModel tile)
            {
                tile.IsSelected = true;
            }
        }

        if (_library is not null && sender is ListBox list)
        {
            _library.SelectedCount = list.SelectedItems?.Count ?? 0;

            // The whole picked set, not just the anchor: "Add to list" and
            // "Remove from list" both act on every marked row, and the view
            // model has no other way to see them.
            _library.SelectedTiles = list.SelectedItems is null
                ? []
                : [.. list.SelectedItems.OfType<GameTileViewModel>()];
        }
    }

    /// <summary>
    /// The context menu opens only when there is a selection; cancels otherwise.
    /// </summary>
    private void OnLibraryContextMenuOpening(object? sender, CancelEventArgs e)
        => e.Cancel = _library is not { HasSelection: true };

    private void OnListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_library is not null && e.Source is Control { DataContext: GameTileViewModel tile })
        {
            _library.OpenDetailsCommand.Execute(tile);
            e.Handled = true;
        }
    }

    private void OnLibraryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            // The visible set changed under a viewport that is still scrolled
            // to wherever the previous, longer set had been left. Nothing
            // re-anchors it, so a filter down to three tiles would leave the
            // user looking at empty space below the content.
            case nameof(LibraryViewModel.VisibleTiles):
            case nameof(LibraryViewModel.IsGridView):
                ResetScroll();
                break;

            case nameof(LibraryViewModel.Details):
                // Focus follows the modal, so Escape and Tab reach it wherever
                // the user's focus happened to be (§8).
                if (_library is { IsDetailsOpen: true })
                {
                    Dispatcher.UIThread.Post(() => DetailsPanel.Focus(), DispatcherPriority.Input);
                }

                break;
        }
    }

    /// <summary>Sends both views back to the top after a set or view change.</summary>
    private void ResetScroll()
    {
        GridScroll.Offset = new Vector(0, 0);

        if (ListRows.Scroll is { } listScroll)
        {
            listScroll.Offset = new Vector(0, 0);
        }
    }

    private void MoveSelection(int delta)
    {
        var index = _library?.MoveSelection(delta) ?? -1;
        if (index < 0)
        {
            return;
        }

        // Keep the newly selected item on screen (§8: full keyboard navigation).
        if (_library is { IsGridView: false })
        {
            ListRows.ScrollIntoView(index);
            return;
        }

        // Moving selection turned any face-down card back over (the library
        // keeps the two together), which may have just removed the control that
        // had focus. Take it back to the window so the next arrow key still
        // reaches this handler rather than falling into nothing.
        TakeGridFocus();

        // The target is usually not realized — selection can jump a hundred
        // rows — so the wall scrolls to the cell, and the scroll is what
        // realizes the container.
        TileWall.ScrollIntoView(index);
    }

    // ══ The card flip, from the keyboard ════════════════════════════════════
    // §8 asks for the whole interface to be reachable without a pointer, and a
    // flip that only answers a click would put Play, Add to list and Details
    // behind a mouse. Space turns the selected card over — the key every
    // toolkit already spends on "act on the thing that is selected" — and focus
    // follows it in, so Tab walks the three buttons and §8's focus ring shows
    // where it is. Escape turns it back (see UnwindCut) and returns focus here,
    // as do the arrow keys by way of moving the selection.

    /// <summary>Turns the selected card over, or back, and takes focus with it.</summary>
    private void FlipSelectedTile()
    {
        if (_library is not { IsGridView: true, SelectedTile: { } tile })
        {
            return;
        }

        _library.FlipTileCommand.Execute(tile);

        if (_library.FlippedTile is null)
        {
            TakeGridFocus();
            return;
        }

        // Posted at input priority: the class that turns the back face into a
        // hit-testable, focusable surface is applied on the next layout pass, so
        // focusing inline would land on a control that is still face-down.
        Dispatcher.UIThread.Post(FocusFlippedCard, DispatcherPriority.Input);
    }

    /// <summary>
    /// Puts focus on the first action on the turned card. Silently does nothing
    /// when the container is not realized — the tile is off screen, which is not
    /// a state Space can produce, since flipping selects and selection scrolls.
    /// </summary>
    private void FocusFlippedCard()
    {
        if (_library?.FlippedTile is not { } flipped)
        {
            return;
        }

        foreach (var child in TileWall.Children)
        {
            if (child.IsVisible
                && ReferenceEquals(child.DataContext, flipped)
                && child.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsVisible) is { } first)
            {
                first.Focus(NavigationMethod.Tab);
                return;
            }
        }
    }

    /// <summary>
    /// Focus back on the window, which is where the grid's own key handling
    /// lives. Called whenever a card goes face-up under a focused button.
    /// </summary>
    private void TakeGridFocus() => Focus();
}
