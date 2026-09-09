using System.ComponentModel;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.ViewModels.Filters;
using Winnow.App.ViewModels.Fullscreen;
using Winnow.App.ViewModels.Lists;
using Winnow.Core.Queries;

namespace Winnow.App.Views.Fullscreen;

public sealed class FullscreenBrowsePage : FullscreenPage
{
    private readonly bool _feed;
    private FullscreenBrowseState _state = new();
    private readonly Dictionary<string, FullscreenBrowseState> _collections = [];
    private readonly Dictionary<string, int> _shelfPositions = [];
    private readonly List<FeedShelfViewModel> _observedShelves = [];
    private readonly List<FeedCardViewModel> _observedCards = [];
    private readonly List<Button> _tiles = [];
    private Button[] _collectionButtons = [];
    private int _headerFocus = -1;
    private int _shelf;
    private int _card;
    private bool _pending;
    private ContentControl _hero = new();
    private ContentControl _art = new();
    private GameTileViewModel? _selected;
    private Window? _window;
    private readonly Dictionary<Button, FeedCardViewModel> _visibleCards = [];
    private bool _observationPending;
    private readonly DispatcherTimer _observer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    public FullscreenBrowsePage(FullscreenContext context, bool feed) : base(context)
    {
        _feed = feed;
        _observer.Tick += (_, _) => QueueObservation();
        _collections["all"] = _state;
        AttachedToVisualTree += (_, _) =>
        {
            _window = TopLevel.GetTopLevel(this) as Window;
            if (_window is not null) _window.PropertyChanged += WindowChanged;
            if (_feed) _observer.Start();
            Context.Library.PropertyChanged += OnLibraryChanged;
            Context.Feed.PropertyChanged += OnFeedChanged;
            Context.Feed.Shelves.CollectionChanged += OnShelvesChanged;
            ObserveShelves();
            Rebuild();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (_window is not null) _window.PropertyChanged -= WindowChanged;
            _window = null;
            _observer.Stop();
            Context.Library.PropertyChanged -= OnLibraryChanged;
            Context.Feed.PropertyChanged -= OnFeedChanged;
            Context.Feed.Shelves.CollectionChanged -= OnShelvesChanged;
            foreach (var shelf in _observedShelves) shelf.Cards.CollectionChanged -= OnShelvesChanged;
            _observedShelves.Clear();
            foreach (var card in _observedCards) { card.PropertyChanged -= OnCardChanged; card.IsFocusWithin = false; }
            _observedCards.Clear();
        };
        Rebuild();
    }

    public override string Title => _feed ? "For you" : "Library";
    public override string Hints => _feed
        ? $"A  {(_selected is null ? "Choose" : "Open game")}{PlayHint}    Y  More    View  Search    ↑ ↓  Change shelf"
        : $"A  Open game{PlayHint}    Y  Filter & sort    View  Search    LT / RT  Page {_state.Page + 1} / {_state.PageCount(Context.Library.VisibleTiles.Count)}";
    private string PlayHint => _selected is { IsOnDisk: true, IsPlayAction: true } ? "    X  Play" : string.Empty;

    public override void FocusInitial()
    {
        if (!_feed && _headerFocus >= 0 && _headerFocus < _collectionButtons.Length) FocusControl(_collectionButtons[_headerFocus]);
        else if (_tiles.Count > 0)
            FocusControl(_tiles[Math.Clamp(_feed ? _card % 5 : _state.PositionOnPage, 0, _tiles.Count - 1)]);
        else base.FocusInitial();
        QueueObservation();
    }

    private void WindowChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property.Name is "IsActive" or "IsVisible" or "WindowState") QueueObservation();
    }
    private void QueueObservation()
    {
        if (!_feed || _observationPending) return;
        _observationPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _observationPending = false;
            if (_window is not { IsActive: true, IsVisible: true } window || window.WindowState == WindowState.Minimized || !IsEffectivelyVisible) return;
            foreach (var (button, card) in _visibleCards)
            {
                if (button.TranslatePoint(default, window) is not { } origin) continue;
                var visible = new Rect(origin, button.Bounds.Size).Intersect(new Rect(window.ClientSize));
                if (visible.Width > 0 && visible.Height > 0 && window.InputHitTest(visible.Center, enabledElementsOnly: false) is Visual hit
                    && (ReferenceEquals(hit, button) || hit.GetVisualAncestors().Contains(button)))
                    _ = Context.Feed.RecordViewportEntryAsync(card);
            }
        }, DispatcherPriority.Background);
    }

    public override bool Handle(GamepadButtons buttons)
    {
        if (buttons.HasFlag(GamepadButtons.Search)) { Context.Push(new FullscreenBrowseSearchPage(Context)); return true; }
        if (buttons.HasFlag(GamepadButtons.Play))
        {
            if (_selected is { IsOnDisk: true, IsPlayAction: true } tile) Context.Play(tile);
            return true;
        }
        if (buttons.HasFlag(GamepadButtons.Keyboard))
        {
            if (_feed) More(); else Context.Push(new FullscreenBrowseFiltersPage(Context));
            return true;
        }
        if (_feed && (buttons.HasFlag(GamepadButtons.Up) || buttons.HasFlag(GamepadButtons.Down)))
        {
            var next = Math.Clamp(_shelf + (buttons.HasFlag(GamepadButtons.Down) ? 1 : -1), 0, Math.Max(0, Context.Feed.Shelves.Count - 1));
            if (next != _shelf) { _shelf = next; Rebuild(); FocusInitial(); }
            return true;
        }
        if (_feed && (buttons.HasFlag(GamepadButtons.Left) || buttons.HasFlag(GamepadButtons.Right)) && Context.Feed.Shelves.Count > _shelf)
        {
            var shelf = Context.Feed.Shelves[_shelf];
            var next = Math.Clamp(_card + (buttons.HasFlag(GamepadButtons.Right) ? 1 : -1), 0, Math.Max(0, shelf.Cards.Count - 1));
            if (next / 5 != _card / 5)
            {
                _shelfPositions[shelf.Id] = next;
                Rebuild();
                FocusInitial();
                return true;
            }
        }
        if (!_feed && (buttons.HasFlag(GamepadButtons.PageNext) || buttons.HasFlag(GamepadButtons.PagePrevious)))
        {
            if (_state.MovePage(buttons.HasFlag(GamepadButtons.PageNext) ? 1 : -1,
                    Context.Library.VisibleTiles.Select(t => t.ReleaseId).ToArray()))
            { Rebuild(); FocusInitial(); }
            return true;
        }
        return base.Handle(buttons);
    }

    private void OnLibraryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_feed && e.PropertyName is nameof(LibraryViewModel.VisibleTiles) or nameof(LibraryViewModel.EmptyMessage)) QueueRebuild();
    }
    private void OnFeedChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_feed && e.PropertyName is nameof(FeedViewModel.Message) or nameof(FeedViewModel.IsLoading)) QueueRebuild();
    }
    private void OnShelvesChanged(object? sender, NotifyCollectionChangedEventArgs e) { ObserveShelves(); if (_feed) QueueRebuild(); }
    private void ObserveShelves()
    {
        foreach (var shelf in _observedShelves) shelf.Cards.CollectionChanged -= OnShelvesChanged;
        foreach (var card in _observedCards) card.PropertyChanged -= OnCardChanged;
        _observedCards.Clear();
        _observedShelves.Clear();
        _observedShelves.AddRange(Context.Feed.Shelves);
        foreach (var shelf in _observedShelves) shelf.Cards.CollectionChanged += OnShelvesChanged;
        _observedCards.AddRange(_observedShelves.SelectMany(s => s.Cards));
        foreach (var card in _observedCards) card.PropertyChanged += OnCardChanged;
    }
    private void OnCardChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_feed && e.PropertyName is nameof(FeedCardViewModel.IsSetAside) or nameof(FeedCardViewModel.SetAsideNote) or nameof(FeedCardViewModel.Problem)) QueueRebuild();
    }
    private void QueueRebuild()
    {
        if (_pending) return;
        _pending = true;
        Dispatcher.UIThread.Post(() => { _pending = false; var focused = IsKeyboardFocusWithin; Rebuild(); if (focused) FocusInitial(); });
    }

    private void Rebuild()
    {
        _tiles.Clear();
        if (_feed) BuildHome(); else BuildLibrary();
        Changed();
    }

    private void BuildHome()
    {
        _visibleCards.Clear();
        _hero = new ContentControl();
        _art = new ContentControl();
        if (Context.Feed.Shelves.Count == 0)
        {
            _selected = null;
            var retry = FullscreenUi.Button("Refresh recommendations", () => Context.Feed.LoadCommand.Execute(null));
            var history = FullscreenUi.Button("What you've told the feed", () => Context.Push(new FullscreenBrowseHistoryPage(Context)));
            Content = FullscreenUi.Stack(FullscreenUi.Text("Where to start", 64),
                FullscreenUi.Text(Context.Feed.Message ?? "No recommendations right now."), retry, history);
            SetFocusRows([retry], [history]);
            return;
        }
        _shelf = Math.Clamp(_shelf, 0, Context.Feed.Shelves.Count - 1);
        var shelf = Context.Feed.Shelves[_shelf];
        _card = Math.Clamp(_shelfPositions.GetValueOrDefault(shelf.Id), 0, Math.Max(0, shelf.Cards.Count - 1));
        var grid = new Grid { RowDefinitions = new RowDefinitions("*,Auto,*") };
        var heading = FullscreenUi.Text($"{shelf.Title}     {_shelf + 1} / {Context.Feed.Shelves.Count}", 32);
        heading.Margin = new Thickness(0, 24, 0, 16);
        Grid.SetRow(heading, 1);
        var shelfGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*") };
        Grid.SetRow(shelfGrid, 2);
        // The feed's engine currently offers five cards per shelf; paging keeps any larger shelf reachable.
        var offset = _card / 5 * 5;
        foreach (var (card, index) in shelf.Cards.Skip(offset).Take(5).Select((c, i) => (c, i)))
        {
            var button = MakeTile(card.Tile, index, () =>
            {
                _card = offset + index;
                _shelfPositions[shelf.Id] = _card;
                SetHero(card, shelf);
            });
            Grid.SetColumn(button, index);
            shelfGrid.Children.Add(button);
            _visibleCards.Add(button, card);
        }
        _hero.Margin = new Thickness(0, 0, 600, 0);
        grid.Children.Add(_hero);
        grid.Children.Add(heading);
        grid.Children.Add(shelfGrid);
        Content = new Panel { Children = { _art, grid } };
        if (shelf.Cards.Count > 0) SetHero(shelf.Cards[_card], shelf);
        SetFocusRows(_tiles.Cast<Control>().ToArray());
    }

    private void SetHero(FeedCardViewModel card, FeedShelfViewModel shelf)
    {
        _selected = card.Tile;
        var reason = FullscreenUi.Text(card.IsSetAside ? card.SetAsideNote : card.Reason, 28);
        reason.MaxLines = 3;
        var title = FullscreenUi.Text(card.Tile.Title, 64);
        title.MaxLines = 2;
        title.TextTrimming = TextTrimming.WordEllipsis;
        _hero.Content = FullscreenUi.Stack(FullscreenUi.Text(shelf.Title.ToUpperInvariant(), 24, "TextDim"),
            title, reason,
            FullscreenUi.Text($"{card.Tile.PlaytimeText} played · {card.Tile.LastPlayedText}", 24, "TextDim"),
            FullscreenUi.Text($"{(card.Tile.IsOnDisk ? "Installed" : "Not installed")} · {card.Tile.StoreNames}", 24, "TextDim"));
        _art.Content = new FullscreenBackdrop(Context, card.Tile) { HorizontalAlignment = HorizontalAlignment.Right, Width = 1100 };
        Changed();
    }

    private void BuildLibrary()
    {
        var library = Context.Library;
        if (library.Lists.Open is { } open)
        {
            var key = $"list:{open.Id}";
            if (!_collections.TryGetValue(key, out var state)) _collections[key] = state = new FullscreenBrowseState();
            _state = state;
        }
        _state.Reconcile(library.VisibleTiles.Select(t => t.ReleaseId).ToArray());
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var title = FullscreenUi.Text(library.Lists.Open?.Name ?? library.SelectedBucket?.Name
            ?? (library.Filters.ToFilter().Installed == true ? "Installed games" : "Your library"), 48);
        title.MaxLines = 1;
        title.TextTrimming = TextTrimming.WordEllipsis;
        heading.Children.Add(title);
        var count = FullscreenUi.Text($"{library.VisibleCountText} games · {library.SortLabel}", 24, "TextDim");
        count.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(count, 1);
        heading.Children.Add(count);
        grid.Children.Add(heading);
        var collections = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Margin = new Thickness(0, 16, 0, 24) };
        Grid.SetRow(collections, 1);
        var all = FullscreenUi.Button("All games", () => ChooseCollection("all"));
        var installed = FullscreenUi.Button("Installed", () => ChooseCollection("installed"));
        var never = FullscreenUi.Button("Never played", () => ChooseCollection("never"));
        var patched = FullscreenUi.Button("Patched", () => ChooseCollection("patched"));
        var lists = FullscreenUi.Button("My lists", () => Context.Push(new FullscreenBrowseListsPage(Context)));
        var actions = FullscreenUi.Button("More", LibraryActions);
        _collectionButtons = [all, installed, never, patched, lists, actions];
        foreach (var (control, index) in _collectionButtons.Select((button, index) => (button, index)))
        {
            control.GotFocus += (_, _) => _headerFocus = index;
            collections.Children.Add(control);
        }
        grid.Children.Add(collections);
        var wall = new Grid { RowDefinitions = new RowDefinitions("*,*"), ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*,*") };
        Grid.SetRow(wall, 2);
        foreach (var (tile, index) in library.VisibleTiles.Skip(_state.Page * FullscreenBrowseState.PageSize).Take(FullscreenBrowseState.PageSize).Select((t, i) => (t, i)))
        {
            var button = MakeTile(tile, index, () => { _state.Select(tile.ReleaseId, index); Changed(); });
            Grid.SetRow(button, index / 6);
            Grid.SetColumn(button, index % 6);
            wall.Children.Add(button);
        }
        if (_tiles.Count == 0) wall.Children.Add(FullscreenUi.Text(library.EmptyMessage ?? "No games match this collection. Change your filters or search."));
        grid.Children.Add(wall);
        Content = grid;
        SetFocusRows([all, installed, never, patched, lists, actions], _tiles.Take(6).Cast<Control>().ToArray(), _tiles.Skip(6).Cast<Control>().ToArray());
        _selected = library.VisibleTiles.FirstOrDefault(t => t.ReleaseId == _state.SelectedReleaseId);
    }

    private Button MakeTile(GameTileViewModel tile, int index, Action selected)
    {
        var cover = new FullscreenCover(tile);
        var title = FullscreenUi.Text(tile.Title, 24);
        title.MaxLines = 1;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        var panel = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        panel.Children.Add(cover);
        title.Margin = new Thickness(0, 8, 0, 0);
        Grid.SetRow(title, 1);
        panel.Children.Add(title);
        var button = FullscreenUi.Button(tile.Title, () => Context.OpenGame(tile));
        button.Classes.Add("tv-cover");
        button.Content = panel;
        button.Background = Brushes.Transparent;
        button.BorderThickness = new Thickness(0);
        button.FocusAdorner = null;
        button.Padding = new Thickness(6);
        button.Margin = new Thickness(0, 0, 24, 16);
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.VerticalContentAlignment = VerticalAlignment.Stretch;
        button.GotFocus += (_, _) =>
        {
            _headerFocus = -1;
            _selected = tile; cover.SetSelected(true); selected();
            if (_feed && _observedCards.FirstOrDefault(c => ReferenceEquals(c.Tile, tile)) is { } card)
            {
                card.IsFocusWithin = true;
                QueueObservation();
            }
        };
        button.LostFocus += (_, _) =>
        {
            cover.SetSelected(false);
            if (_feed && _observedCards.FirstOrDefault(c => ReferenceEquals(c.Tile, tile)) is { } card) card.IsFocusWithin = false;
        };
        AutomationProperties.SetName(button, tile.AutomationName);
        _tiles.Add(button);
        return button;
    }

    private void ChooseCollection(string key)
    {
        _headerFocus = -1;
        if (!_collections.TryGetValue(key, out var state)) _collections[key] = state = new FullscreenBrowseState();
        _state = state;
        Context.Library.CloseListCommand.Execute(null);
        Context.Library.SearchText = string.Empty;
        Context.Library.Filters.Clear();
        Context.Library.SelectedBucket = key switch
        {
            "never" => Context.Library.Buckets.First(b => b.Key == LibraryBuckets.NeverPlayed),
            "patched" => Context.Library.Buckets.First(b => b.Key == LibraryBuckets.StaleButPatched),
            _ => null
        };
        if (key == "installed") Context.Library.Filters.Apply(new LibraryFilter { Installed = true });
        Rebuild();
        FocusInitial();
    }

    private void More()
    {
        var general = new List<FullscreenAction>
        {
            new("What you've told the feed", () => Context.Push(new FullscreenBrowseHistoryPage(Context))),
            new("Refresh recommendations", () => Context.Feed.LoadCommand.Execute(null))
        };
        if (_selected is not { } tile) { Context.ShowActions("For you", general); return; }
        var card = Context.Feed.Shelves.SelectMany(s => s.Cards).FirstOrDefault(c => ReferenceEquals(c.Tile, tile));
        var actions = new List<FullscreenAction>
        {
            new("Open game", () => Context.OpenGame(tile)),
            new("Add to list", () => { Context.Library.SelectTile(tile); Context.Library.BeginAddToListCommand.Execute(null); })
        };
        if (card is { CanGiveFeedback: true })
        {
            if (card.IsSetAside) actions.Add(new("Undo", () => card.UndoCommand.Execute(null)));
            else
            {
                actions.Add(new("Not now", () => card.NotNowCommand.Execute(null)));
                actions.Add(new("Not interested", () => card.NotInterestedCommand.Execute(null)));
            }
        }
        actions.AddRange(general);
        Context.ShowActions(tile.Title, actions);
    }

    private void LibraryActions()
    {
        var actions = new List<FullscreenAction>
        {
            new("Search", () => Context.Push(new FullscreenBrowseSearchPage(Context))),
            new("Filter & sort", () => Context.Push(new FullscreenBrowseFiltersPage(Context))),
            new("New list", () => Context.Library.BeginCreateListCommand.Execute(null)),
            new("Save as live list", () => Context.Library.BeginSaveLiveListCommand.Execute(null), Context.Library.CanSaveLiveList),
            new("Library tools", () => Context.Push(new FullscreenLibraryToolsPage(Context)))
        };
        if (Context.Library.Lists.Open is not null)
        {
            actions.Add(new("Rename list", () => Context.Library.BeginRenameListCommand.Execute(null)));
            actions.Add(new("Delete list", () => Context.Library.BeginDeleteListCommand.Execute(null)));
            if (Context.Library.Lists.IsLiveListOpen)
            {
                actions.Add(new("Save live-list changes", () => Context.Library.UpdateLiveListCommand.Execute(null), Context.Library.IsLiveListEdited));
                actions.Add(new("Restore saved live-list filters", () => Context.Library.RevertLiveListCommand.Execute(null), Context.Library.IsLiveListEdited));
            }
        }
        if (_selected is { } tile)
        {
            actions.Add(new("Add selected game to list", () => { Context.Library.SelectTile(tile); Context.Library.BeginAddToListCommand.Execute(null); }));
            Context.Library.SelectTile(tile);
            if (Context.Library.CanEditOpenList)
            {
                actions.Add(new("Remove selected game from list", () => Context.Library.RemoveFromOpenListCommand.Execute(null)));
                if (Context.Library.Sort != LibrarySort.ListOrder)
                    actions.Add(new("Show list order", () => Context.Library.Sort = LibrarySort.ListOrder));
                actions.Add(new("Move selected game earlier", () => Context.Library.MoveUpInListCommand.Execute(null), Context.Library.CanMoveUpInList));
                actions.Add(new("Move selected game later", () => Context.Library.MoveDownInListCommand.Execute(null), Context.Library.CanMoveDownInList));
            }
        }
        Context.ShowActions("Library", actions);
    }
}

/// <summary>Each fullscreen surface leases its own cover; leaving a page releases decoded art.</summary>
public sealed class FullscreenCover : Border
{
    private readonly GameTileViewModel _tile;
    private readonly bool _background;
    private CoverPresenter? _presenter;
    private readonly Image _floor = new() { Stretch = Stretch.UniformToFill };
    private readonly Image _vivid = new() { Stretch = Stretch.UniformToFill };
    private readonly TextBlock _placeholder;
    private bool _selected;

    public FullscreenCover(GameTileViewModel tile, bool background = false)
    {
        _tile = tile;
        _background = background;
        ClipToBounds = true;
        CornerRadius = new CornerRadius(6);
        BorderThickness = new Thickness(background ? 0 : 3);
        BorderBrush = Brushes.Transparent;
        if (!background) HorizontalAlignment = HorizontalAlignment.Left;
        this[!BackgroundProperty] = new DynamicResourceExtension("Surface");
        _placeholder = FullscreenUi.Text(tile.Title, 24);
        _placeholder.TextWrapping = TextWrapping.WrapWithOverflow;
        _placeholder.MaxLines = 3;
        _placeholder.TextTrimming = TextTrimming.WordEllipsis;
        _placeholder.Margin = new Thickness(16);
        _placeholder.VerticalAlignment = VerticalAlignment.Bottom;
        var layers = new Panel { Children = { _floor, _vivid, _placeholder } };
        if (tile.HasUnread && !background)
        {
            var dot = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12) };
            dot[!BackgroundProperty] = new DynamicResourceExtension("Flare");
            layers.Children.Add(dot);
        }
        Child = layers;
        if (!background) { _floor.Stretch = Stretch.Uniform; _vivid.Stretch = Stretch.Uniform; }
        if (background) { Opacity = .16; IsHitTestVisible = false; }
        AttachedToVisualTree += (_, _) =>
        {
            _tile.PropertyChanged += OnTileChanged;
            _presenter = tile.NewCoverPresenter();
            _presenter.PropertyChanged += OnArtChanged;
            RequestArt();
            Paint();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _tile.PropertyChanged -= OnTileChanged;
            if (_presenter is null) return;
            _presenter.PropertyChanged -= OnArtChanged;
            _floor.Source = null;
            _vivid.Source = null;
            _presenter.Dispose();
            _presenter = null;
        };
        SizeChanged += (_, _) => RequestArt();
    }

    private void RequestArt()
    {
        var top = TopLevel.GetTopLevel(this);
        var scale = top is null ? 1 : Math.Abs(this.TransformToVisual(top)?.M11 ?? 1) * top.RenderScaling;
        _presenter?.Request(Math.Max(200, Bounds.Width * scale));
    }

    public void SetSelected(bool selected)
    {
        _selected = selected;
        if (selected) this[!BorderBrushProperty] = new DynamicResourceExtension("Volt");
        else BorderBrush = Brushes.Transparent;
        Paint();
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        if (_background) return base.MeasureOverride(availableSize);
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 240;
        if (double.IsFinite(availableSize.Height)) width = Math.Min(width, availableSize.Height * 2 / 3);
        var size = new Size(Math.Max(0, width), Math.Max(0, width * 1.5));
        base.MeasureOverride(size);
        return size;
    }
    private void OnArtChanged(object? sender, PropertyChangedEventArgs e) => Paint();
    private void OnTileChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GameTileViewModel.DormancyAlpha) or nameof(GameTileViewModel.DisplayAlpha)) Paint();
    }
    private void Paint()
    {
        _floor.Source = _presenter?.Floor;
        _vivid.Source = _presenter?.Vivid;
        _vivid.Opacity = _selected || _background ? 1 : _tile.DormancyAlpha;
        _placeholder.IsVisible = _presenter?.HasCover != true && !_background;
    }
}

public sealed class FullscreenBrowseSearchPage : FullscreenPage
{
    private readonly TextBox _query = new() { Watermark = "Search games", FontSize = 32 };
    private readonly ContentControl _results = new();
    private readonly TextBlock _count = FullscreenUi.Text(string.Empty, 24, "TextDim");
    private readonly FullscreenBrowseState _state = new();
    private readonly Button _edit;
    private readonly Button _showResults;
    private readonly List<Button> _games = [];
    private bool _inResults;
    private IReadOnlyList<GameTileViewModel> _matches = [];

    public FullscreenBrowseSearchPage(FullscreenContext context) : base(context)
    {
        _edit = FullscreenUi.Button("Enter search", () => Context.EditText(_query));
        _edit.GotFocus += (_, _) => _inResults = false;
        _query.GotFocus += (_, _) => _inResults = false;
        _showResults = FullscreenUi.Button("Go to results", () => { if (_games.Count > 0) FocusControl(_games[0]); });
        var input = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        input.Children.Add(_query);
        Grid.SetColumn(_edit, 1);
        Grid.SetColumn(_showResults, 2);
        input.Children.Add(_edit);
        input.Children.Add(_showResults);
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*") };
        grid.Children.Add(FullscreenUi.Text("Search", 64));
        Grid.SetRow(input, 1);
        grid.Children.Add(input);
        _count.Margin = new Thickness(0, 16);
        Grid.SetRow(_count, 2);
        grid.Children.Add(_count);
        Grid.SetRow(_results, 3);
        grid.Children.Add(_results);
        Content = grid;
        _query.TextChanged += (_, _) => Refresh();
        Refresh();
    }

    public override string Title => "Search";
    public override string Hints => $"A  Choose    B  Back    Y  Enter search    LT / RT  Page {_state.Page + 1} / {_state.PageCount(_matches.Count)}";
    public override void FocusInitial()
    {
        if (_inResults && _games.Count > 0) FocusControl(_games[Math.Min(_state.PositionOnPage, _games.Count - 1)]);
        else FocusControl(_edit);
    }

    public override bool Handle(GamepadButtons buttons)
    {
        if (buttons.HasFlag(GamepadButtons.Keyboard)) { Context.EditText(_query); return true; }
        if (buttons.HasFlag(GamepadButtons.PageNext) || buttons.HasFlag(GamepadButtons.PagePrevious))
        {
            if (_state.MovePage(buttons.HasFlag(GamepadButtons.PageNext) ? 1 : -1, _matches.Select(t => t.ReleaseId).ToArray()))
            {
                DrawResults();
                if (_games.Count > 0) FocusControl(_games[Math.Min(_state.PositionOnPage, _games.Count - 1)]);
            }
            return true;
        }
        return base.Handle(buttons);
    }

    private void Refresh()
    {
        var query = (_query.Text ?? string.Empty).Trim();
        _matches = Context.Library.AllTiles.Where(t => t.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        _state.Reconcile(_matches.Select(t => t.ReleaseId).ToArray());
        DrawResults();
    }

    private void DrawResults()
    {
        _games.Clear();
        _count.Text = $"{_matches.Count:N0} games";
        var wall = new Grid { RowDefinitions = new RowDefinitions("*,*"), ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*,*") };
        foreach (var (tile, index) in _matches.Skip(_state.Page * FullscreenBrowseState.PageSize).Take(FullscreenBrowseState.PageSize).Select((t, i) => (t, i)))
        {
            var cover = new FullscreenCover(tile);
            var panel = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
            panel.Children.Add(cover);
            var label = FullscreenUi.Text(tile.Title, 24);
            label.MaxLines = 1;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetRow(label, 1);
            panel.Children.Add(label);
            var button = FullscreenUi.Button(tile.Title, () => Context.OpenGame(tile));
            button.Classes.Add("tv-cover");
            button.Content = panel;
            button.Background = Brushes.Transparent;
            button.BorderThickness = new Thickness(0);
            button.FocusAdorner = null;
            button.Margin = new Thickness(0, 0, 24, 16);
            button.Padding = new Thickness(6);
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.VerticalContentAlignment = VerticalAlignment.Stretch;
            button.GotFocus += (_, _) => { _inResults = true; _state.Select(tile.ReleaseId, index); cover.SetSelected(true); };
            button.LostFocus += (_, _) => cover.SetSelected(false);
            Grid.SetRow(button, index / 6);
            Grid.SetColumn(button, index % 6);
            AutomationProperties.SetName(button, tile.AutomationName);
            wall.Children.Add(button);
            _games.Add(button);
        }
        _showResults.IsEnabled = _games.Count > 0;
        if (_games.Count == 0) wall.Children.Add(FullscreenUi.Text("No games match. Try a different title."));
        _results.Content = wall;
        SetFocusRows([_query, _edit, _showResults], _games.Take(6).Cast<Control>().ToArray(), _games.Skip(6).Cast<Control>().ToArray());
        Changed();
    }
}

public sealed class FullscreenBrowseFiltersPage : FullscreenPage
{
    private readonly FullscreenBrowseFilterDraft _draft;
    private readonly TextBlock _count = FullscreenUi.Text(string.Empty, 32);
    private readonly List<Control[]> _rows = [];
    private readonly TextBox _from = new() { FontSize = 28, Watermark = "Any year" };
    private readonly TextBox _to = new() { FontSize = 28, Watermark = "Any year" };
    private readonly TextBlock _problem = FullscreenUi.Text(string.Empty, 24);
    private Button? _fromButton;
    private Button? _toButton;

    public FullscreenBrowseFiltersPage(FullscreenContext context) : base(context)
    {
        _draft = new FullscreenBrowseFilterDraft(context.Library);
        Build();
    }

    public override string Title => "Filter & sort";
    public override string Hints => "A  Choose    B  Discard changes";

    private void Build()
    {
        _rows.Clear();
        if (Content is ScrollViewer { Content: StackPanel previous }) previous.Children.Clear();
        var content = FullscreenUi.Stack(FullscreenUi.Text("Filter & sort", 64), _count);
        Add(content, FullscreenUi.Button($"Collection · {_draft.Bucket?.Name ?? "All games"}", () =>
            Context.ShowActions("Collection", new[] { new FullscreenAction("All games", () => { _draft.Bucket = null; Build(); FocusInitial(); }) }
                .Concat(Context.Library.Buckets.Select(bucket => new FullscreenAction(bucket.Name,
                    () => { _draft.Bucket = bucket; Build(); FocusInitial(); }))).ToArray())));
        var sort = FullscreenUi.Button($"Sort · {Context.Library.SortOptions.First(s => s.Sort == _draft.Sort).Label}", () =>
            Context.ShowActions("Sort", Context.Library.SortOptions.Select(option => new FullscreenAction(option.Label,
                () => { _draft.Sort = option.Sort; Build(); FocusInitial(); })).ToArray()));
        Add(content, sort);
        foreach (var group in _draft.Filters.Groups.Where(g => g.AllOptions.Count > 0))
        {
            var count = group.Checked.Count();
            var button = FullscreenUi.Button($"{group.Header} · {(count == 0 ? "Any" : $"{count} selected")}", () =>
                Context.Push(new FullscreenBrowseFilterGroupPage(Context, group, () => { Build(); })));
            Add(content, button);
        }
        if (Context.Library.Filters.HasYearData)
        {
            _from.Text = _draft.Filters.YearFromText;
            _to.Text = _draft.Filters.YearToText;
            _fromButton = FullscreenUi.Button($"Release year from · {(_from.Text is { Length: > 0 } ? _from.Text : "Any")}", () => Context.EditText(_from));
            _toButton = FullscreenUi.Button($"Release year to · {(_to.Text is { Length: > 0 } ? _to.Text : "Any")}", () => Context.EditText(_to));
            Add(content, _fromButton);
            Add(content, _toButton);
            _from.TextChanged -= YearChanged;
            _to.TextChanged -= YearChanged;
            _from.TextChanged += YearChanged;
            _to.TextChanged += YearChanged;
        }
        content.Children.Add(_problem);
        Add(content, FullscreenUi.Button("Apply", Apply));
        Add(content, FullscreenUi.Button("Clear filters", () => { _draft.Filters.Clear(); _draft.Bucket = null; Build(); FocusInitial(); }));
        Add(content, FullscreenUi.Button("Cancel", Context.Back));
        Content = FullscreenUi.Scroll(content);
        SetFocusRows(_rows.ToArray());
        Recount();
    }

    private void YearChanged(object? sender, TextChangedEventArgs e)
    {
        _draft.Filters.YearFromText = _from.Text ?? string.Empty;
        _draft.Filters.YearToText = _to.Text ?? string.Empty;
        if (_fromButton is not null)
        {
            var label = $"Release year from · {(_from.Text is { Length: > 0 } ? _from.Text : "Any")}";
            _fromButton.Content = label;
            AutomationProperties.SetName(_fromButton, label);
        }
        if (_toButton is not null)
        {
            var label = $"Release year to · {(_to.Text is { Length: > 0 } ? _to.Text : "Any")}";
            _toButton.Content = label;
            AutomationProperties.SetName(_toButton, label);
        }
        Recount();
    }

    private void Recount()
    {
        var filter = _draft.Filters.ToFilter() with
        {
            Search = Context.Library.SearchText,
            Buckets = _draft.Bucket is { } bucket ? [bucket.Key] : []
        };
        var list = Context.Library.Lists.Open;
        _count.Text = $"{Context.Library.AllTiles.Count(t => filter.Matches(t.Row) && (list is not { IsManual: true } || list.ReleaseIds.Any(t.CoversRelease))):N0} games";
    }
    private void Add(StackPanel panel, Button button) { panel.Children.Add(button); _rows.Add([button]); }
    private void Apply()
    {
        bool Valid(string? value) => string.IsNullOrWhiteSpace(value) || int.TryParse(value, out var year) && year is >= 1 and <= 9999;
        if (!Valid(_from.Text) || !Valid(_to.Text) || _draft.Filters.YearFrom > _draft.Filters.YearTo)
        { _problem.Text = "Enter years from 1 to 9999, with the earlier year first."; return; }
        _draft.Apply(Context.Library);
        Context.Back();
    }
}

internal sealed class FullscreenBrowseFilterGroupPage : FullscreenPage
{
    private readonly FilterGroupViewModel _group;
    private readonly Action _changed;
    public FullscreenBrowseFilterGroupPage(FullscreenContext context, FilterGroupViewModel group, Action changed) : base(context)
    { _group = group; _changed = changed; Build(); }
    public override string Title => _group.Header;
    public override string Hints => "A  Toggle    B  Back to filters";
    private void Build()
    {
        var panel = FullscreenUi.Stack(FullscreenUi.Text(_group.Header, 48));
        var rows = new List<Control[]>();
        foreach (var option in _group.AllOptions)
        {
            var button = FullscreenUi.Button($"{(option.IsChecked ? "✓ " : string.Empty)}{option.Label}", () => { });
            button.Click += (_, _) =>
            {
                option.IsChecked = !option.IsChecked;
                button.Content = FullscreenUi.Text($"{(option.IsChecked ? "✓ " : string.Empty)}{option.Label}");
                AutomationProperties.SetName(button, $"{option.Label}, {(option.IsChecked ? "selected" : "not selected")}");
                _changed();
            };
            panel.Children.Add(button);
            rows.Add([button]);
        }
        var done = FullscreenUi.Button("Done", Context.Back);
        panel.Children.Add(done);
        rows.Add([done]);
        Content = FullscreenUi.Scroll(panel);
        SetFocusRows(rows.ToArray());
    }
}

public sealed class FullscreenBrowseListsPage : FullscreenPage
{
    public FullscreenBrowseListsPage(FullscreenContext context) : base(context)
    {
        AttachedToVisualTree += (_, _) => { Context.Library.Lists.PropertyChanged += OnChanged; Build(); };
        DetachedFromVisualTree += (_, _) => Context.Library.Lists.PropertyChanged -= OnChanged;
        Build();
    }
    public override string Title => "My lists";
    public override string Hints => "A  Open list    B  Back";
    private void OnChanged(object? sender, PropertyChangedEventArgs e) => Build();
    private void Build()
    {
        var panel = FullscreenUi.Stack(FullscreenUi.Text("My lists", 64));
        var rows = new List<Control[]>();
        foreach (var list in Context.Library.Lists.All)
        {
            var button = FullscreenUi.Button($"{list.Name} · {list.CountText} games{(list.IsLive ? " · Live list" : string.Empty)}", () =>
            {
                Context.Library.OpenListCommand.Execute(list);
                Context.Back();
            });
            panel.Children.Add(button);
            rows.Add([button]);
        }
        if (rows.Count == 0) panel.Children.Add(FullscreenUi.Text("No lists yet. Create a list to keep games together."));
        var create = FullscreenUi.Button("New list", () => Context.Library.BeginCreateListCommand.Execute(null));
        panel.Children.Add(create);
        rows.Add([create]);
        Content = FullscreenUi.Scroll(panel);
        SetFocusRows(rows.ToArray());
    }
}
