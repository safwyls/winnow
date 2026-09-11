using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;

namespace Winnow.App.Views.Fullscreen;

/// <summary>Personal history, with its own selection and week independent of the desktop.</summary>
public sealed class FullscreenActivityPage : FullscreenPage
{
    public override Control? Backdrop { get; } = new FullscreenAmbientBackdrop("activity");
    private readonly List<ActivityEntry> _entries = [];
    private readonly StackPanel _preview = new() { Spacing = 24 };
    private ActivityEntry? _selected;
    private string _section = "Sessions";
    private int _week;
    internal int WeekOffset => _week;
    private bool _tabsFocused;
    private bool _loaded;
    private bool _dirty = true;
    private bool _loading;
    private int _revision;
    private CancellationTokenSource? _readCancellation;
    private ActivityCursor? _next;
    private bool _append;
    private bool _reading;
    private bool _readingAppend;
    private bool _retryAppend;
    private string? _problem;
    public Task PendingRefresh { get; private set; } = Task.CompletedTask;
    internal long? SelectedSessionId => _selected?.Session?.Id;
    private Control? _initial;
    private bool _attached;
    private bool _disposed;
    private string _status = "Reading your activity…";
    public override string Title => "Activity";
    public override string Hints => (_selected is null ? "A  Select" : $"A  Open event{(_selected.Session is null ? "" : "     X  Edit note")}{(string.IsNullOrWhiteSpace(_selected.Note?.Note) ? "" : "     Y  Read note")}") + "     ← / →  Change week";
    public override string RightHints => "LT / RT  Section";

    public FullscreenActivityPage(FullscreenContext context) : base(context)
    {
        Render();
        Context.Library.TilesChanged += LibraryChanged;
        AttachedToVisualTree += async (_, _) => { _attached = true; _dirty |= !_loaded; await EnsureRefreshAsync(); };
        DetachedFromVisualTree += (_, _) =>
        {
            _attached = false; _revision++;
            // A completed page remains valid while its note editor is open.
            // A cancelled read, or a committed library change, still needs another read.
            if (_reading) { _dirty = true; _append = _readingAppend; }
            _readCancellation?.Cancel();
        };
    }

    private void LibraryChanged(object? sender, EventArgs e)
    {
        _revision++; _dirty = true; _loaded = false; _append = false; _next = null; _problem = null;
        _readCancellation?.Cancel();
        _entries.RemoveAll(entry => entry.Session is { } session
            ? Context.Library.TileForOwnership(session.OwnershipId) is null
            : entry.Update is { } update && Context.Library.TileForRelease(update.ReleaseId) is null);
        if (_selected is not null && !_entries.Contains(_selected)) _selected = null;
        if (_attached) { Render(); _ = EnsureRefreshAsync(); }
    }

    private Task EnsureRefreshAsync()
    {
        if (_loading || !_attached || _disposed || !_dirty) return PendingRefresh;
        _loading = true;
        return PendingRefresh = RefreshLoopAsync();
    }

    private async Task RefreshLoopAsync()
    {
        try
        {
            while (_dirty && _attached && !_disposed)
            {
                _dirty = false;
                await LoadAsync(_revision);
            }
        }
        finally { _loading = false; }
    }

    private async Task LoadAsync(int revision)
    {
        using var cancellation = new CancellationTokenSource();
        _readCancellation = cancellation;
        var ct = cancellation.Token;
        var append = _append; _append = false;
        _reading = true; _readingAppend = append; _problem = null;
        _status = "Reading your activity…";
        Render();
        var preserveFocus = _entries.Count > 0 || _tabsFocused;
        try
        {
            var repository = Context.Services?.GetService<IActivityRepository>();
            if (repository is null) { _status = "Activity is unavailable. Reopen Winnow to try again."; return; }
            var tiles = Context.Library.AllTiles.SelectMany(tile => tile.OwnershipIds.Select(id => (id, tile)))
                .ToDictionary(pair => pair.id, pair => pair.tile);
            var start = WeekStart;
            var section = _section switch { "Updates" => ActivitySection.Updates, "Journal" => ActivitySection.Journal, _ => ActivitySection.Sessions };
            var cursor = append ? _next : null;
            var page = await Task.Run(() => repository.GetPageAsync(tiles.Keys.ToArray(), start.ToUniversalTime(),
                start.AddDays(7).ToUniversalTime(), section, cursor, ct: ct), ct);
            if (_disposed || ct.IsCancellationRequested || revision != _revision) return;
            var rows = page.Rows.Where(row => tiles.ContainsKey(row.OwnershipId))
                .Select(row => new ActivityEntry(tiles[row.OwnershipId], row.AtUtc, row.Session, row.Note, row.Update, row.Store)).ToArray();
            var selected = rows.FirstOrDefault(row => row.Session is { } session && session.Id == _selected?.Session?.Id
                || row.Update is { } update && update.Id == _selected?.Update?.Id);
            if (!append) { _entries.Clear(); _selected = selected; }
            _entries.AddRange(rows); _next = page.Next; _loaded = true;
            _status = "No activity this week. Play a game or choose an earlier week.";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
        catch (Exception)
        {
            if (_disposed || ct.IsCancellationRequested || revision != _revision) return;
            _problem = "Couldn't read your activity. Try again.";
            _retryAppend = append;
        }
        finally
        {
            _reading = false;
            if (ReferenceEquals(_readCancellation, cancellation)) _readCancellation = null;
            if (_attached && !_disposed && !ct.IsCancellationRequested && revision == _revision)
            {
                // The user may have moved to a section tab while this read was pending.
                preserveFocus |= _tabsFocused;
                Render(preserveFocus);
                if (!preserveFocus) FocusInitial();
            }
        }
    }

    private DateTime WeekStart => DateTime.Today.AddDays(-(((int)DateTime.Today.DayOfWeek + 6) % 7) - _week * 7);

    private void ChangePeriod()
    {
        _revision++; _dirty = true; _loaded = false; _append = false; _next = null; _problem = null;
        _entries.Clear(); _selected = null; _readCancellation?.Cancel();
        _status = "Reading your activity…";
        Render(preserveFocus: false); FocusInitial(); _ = EnsureRefreshAsync();
    }

    private void Render(bool preserveFocus = true)
    {
        if (_disposed) return;
        var restoreFocus = preserveFocus ? PreserveFocus() : null;
        var tabs = new[] { "Sessions", "Updates", "Journal" }.Select(label =>
            FullscreenUi.Button(label, () => { _section = label; ChangePeriod(); })).ToArray();
        var tabBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24 };
        foreach (var tab in tabs) { tab.Classes.Set("current", Equals(tab.Content, _section)); tab.GotFocus += (_, _) => _tabsFocused = true; tabBar.Children.Add(tab); }
        var start = WeekStart;
        var list = new StackPanel { Spacing = 16 };
        list.Children.Add(FullscreenUi.Text(_week == 0 ? "THIS WEEK" : $"{start:d MMM} – {start.AddDays(6):d MMM yyyy}", 24, "TextDim"));
        var rows = _entries.Where(e => e.At.ToLocalTime() >= start && e.At.ToLocalTime() < start.AddDays(7))
            .Where(e => _section == "Updates" ? e.Update is not null : e.Session is not null && (_section != "Journal" || HasJournal(e.Note))).ToArray();
        var buttons = new List<Control[]> { tabs };
        _initial = null;
        foreach (var row in rows)
        {
            var button = FullscreenUi.Button($"{row.Tile.Title}\n{row.At.ToLocalTime():ddd d MMM} · {row.At.ToLocalTime():t}   {row.Description}", () => Open(row));
            AutomationProperties.SetAutomationId(button, row.Session is { } session ? $"activity-session-{session.Id}" : $"activity-update-{row.Update!.Id}");
            button.MinHeight = 116;
            var content = new Grid { ColumnDefinitions = new ColumnDefinitions("144,*"), ColumnSpacing = 24 };
            content.Children.Add(new FullscreenCover(row.Tile) { Height = 112, Width = 144 });
            var words = FullscreenUi.Stack(FullscreenUi.Text(row.Tile.Title, 32), FullscreenHistoryTypography.Data($"{row.At.ToLocalTime():ddd d MMM} · {row.At.ToLocalTime():t}   {row.Description}", 24));
            if (HasJournal(row.Note)) words.Children.Add(FullscreenUi.Text("Journal entry", 24, "TextDim"));
            Grid.SetColumn(words, 1); content.Children.Add(words); button.Content = content;
            button.GotFocus += (_, _) => { _tabsFocused = false; _initial = button; Select(row); };
            if (_initial is null || row == _selected) _initial = button;
            list.Children.Add(button); buttons.Add([button]);
        }
        if (_problem is { } problem)
        {
            list.Children.Add(FullscreenUi.Text(problem, 28, "Amber"));
            var retry = FullscreenUi.Button("Try again", () =>
            {
                if (_loading) return;
                _append = _retryAppend; _dirty = true; _ = EnsureRefreshAsync();
            });
            list.Children.Add(retry); buttons.Add([retry]);
            _initial ??= retry;
        }
        else if (rows.Length == 0)
        {
            var empty = _section switch
            {
                "Journal" => ("No journal entries this week", "Your notes and ratings appear here. Choose a session and press X to add one."),
                "Updates" => ("No updates this week", "Updates for your visible games appear here as they arrive. Choose an earlier week to look back."),
                _ => ("No sessions this week", "Play a game to start your history, or choose an earlier week.")
            };
            list.Children.Add(FullscreenUi.Text(_loaded ? empty.Item1 : _status, 32));
            if (_loaded) list.Children.Add(FullscreenUi.Text(empty.Item2, 28, "TextDim"));
        }
        if (_reading && rows.Length > 0) list.Children.Add(FullscreenUi.Text(_status, 24, "TextDim"));
        if (_next is not null && _problem is null)
        {
            var more = FullscreenUi.Button("Load more", () =>
            {
                if (_loading) return;
                _append = true; _dirty = true; _ = EnsureRefreshAsync();
            });
            more.IsEnabled = !_reading;
            more.GotFocus += (_, _) => { _tabsFocused = false; _initial = more; };
            list.Children.Add(more); buttons.Add([more]);
        }
        var summary = FullscreenUi.Button("Library summary", () => Context.Push(new FullscreenLibrarySummaryPage(Context)));
        summary.GotFocus += (_, _) => { _tabsFocused = false; _initial = summary; };
        _initial ??= summary;
        list.Children.Add(summary); buttons.Add([summary]);
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 48 };
        if (_preview.Parent is ScrollViewer previous) previous.Content = null;
        columns.Children.Add(FullscreenUi.Scroll(list));
        var previewScroll = FullscreenUi.Scroll(_preview);
        Grid.SetColumn(previewScroll, 1); columns.Children.Add(previewScroll);
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), RowSpacing = 24 };
        layout.Children.Add(FullscreenUi.Text("Your activity", 64)); Grid.SetRow(tabBar, 1); layout.Children.Add(tabBar);
        Grid.SetRow(columns, 2); layout.Children.Add(columns); Content = layout;
        SetFocusRows(buttons.ToArray());
        Select(rows.FirstOrDefault(r => r == _selected) ?? rows.FirstOrDefault());
        restoreFocus?.Invoke();
    }

    public override void FocusInitial()
    {
        if (_initial is not null) FocusControl(_initial); else base.FocusInitial();
    }

    public override void Dispose() { _disposed = true; _revision++; _readCancellation?.Cancel(); Context.Library.TilesChanged -= LibraryChanged; base.Dispose(); }

    private void Select(ActivityEntry? row)
    {
        _selected = row; _preview.Children.Clear();
        if (row is null) { Changed(); return; }
        _preview.Children.Add(new FullscreenCover(row.Tile) { Height = 300, HorizontalAlignment = HorizontalAlignment.Stretch });
        _preview.Children.Add(FullscreenUi.Text(row.Tile.Title, 48));
        _preview.Children.Add(FullscreenHistoryTypography.Data($"{row.At.ToLocalTime():f}\n{row.Description}", 28));
        _preview.Children.Add(FullscreenUi.Text(row.Update?.Title ?? "YOUR NOTE", 28));
        _preview.Children.Add(FullscreenUi.Text(row.Note?.Note ?? (row.Update is null ? "No note for this session." : "Open the game to read its updates.")));
        if (row.Note?.Rating is { } rating) _preview.Children.Add(FullscreenUi.Text($"How was that?  {rating} / 5", 28, "Volt"));
        _preview.Children.Add(FullscreenUi.Text($"Played through {row.Store}", 24, "TextDim"));
        Changed();
    }

    private void Open(ActivityEntry row)
    {
        if (row.Session is null) { Context.OpenGame(row.Tile); return; }
        Context.ShowActions(row.Tile.Title, [new("Open game", () => Context.OpenGame(row.Tile)),
            new("Edit note", () => Context.Push(new FullscreenSessionNotePage(Context, row.Session.Id, row.Tile.Title, row.Note, note => ApplyNote(row, note))))]);
    }

    private void ApplyNote(ActivityEntry row, SessionNote note)
    {
        if (_disposed || !_entries.Contains(row)) return;
        row.Note = note; _selected = row;
        Render();
    }

    public override bool Handle(GamepadButtons buttons)
    {
        if (buttons.HasFlag(GamepadButtons.Keyboard) && _selected is { Note.Note: { Length: > 0 } note } selected)
        {
            Context.Push(new FullscreenDetailsReadingPage(Context, selected.Tile.Title, note));
            return true;
        }
        if ((buttons & (GamepadButtons.PagePrevious | GamepadButtons.PageNext)) != 0)
        {
            string[] sections = ["Sessions", "Updates", "Journal"];
            var direction = buttons.HasFlag(GamepadButtons.PageNext) ? 1 : -1;
            _section = sections[(Array.IndexOf(sections, _section) + direction + sections.Length) % sections.Length];
            ChangePeriod(); return true;
        }
        if (!_tabsFocused && buttons.HasFlag(GamepadButtons.Left)) { _week++; ChangePeriod(); return true; }
        if (!_tabsFocused && buttons.HasFlag(GamepadButtons.Right)) { _week = Math.Max(0, _week - 1); ChangePeriod(); return true; }
        if ((buttons & GamepadButtons.Play) != 0 && _selected?.Session is { } session)
        {
            var row = _selected;
            Context.Push(new FullscreenSessionNotePage(Context, session.Id, row.Tile.Title, row.Note, note => ApplyNote(row, note)));
            return true;
        }
        return base.Handle(buttons);
    }

    private static bool HasJournal(SessionNote? note) => note?.Rating is not null || !string.IsNullOrWhiteSpace(note?.Note);

    private sealed class ActivityEntry(GameTileViewModel tile, DateTime at, Session? session, SessionNote? note, UpdateEvent? update, string store)
    {
        public GameTileViewModel Tile { get; } = tile;
        public DateTime At { get; } = at;
        public Session? Session { get; } = session;
        public SessionNote? Note { get; set; } = note;
        public UpdateEvent? Update { get; } = update;
        public string Store { get; } = store;
        public string Description => Update is not null ? Update.Title ?? "Game update" : Session?.DurationSeconds is { } seconds ? $"{seconds / 3600} h {seconds % 3600 / 60} m" : "Duration not recorded";
    }
}

internal static class FullscreenHistoryTypography
{
    public static TextBlock Data(string text, double size)
    {
        var block = FullscreenUi.Text(text, size, "TextDim");
        block[!TextBlock.FontFamilyProperty] = new DynamicResourceExtension("DataFont");
        block.FontFeatures = new FontFeatureCollection { FontFeature.Parse("tnum") };
        return block;
    }
}

public sealed class FullscreenSessionNotePage : FullscreenPage
{
    private readonly JournalEntryViewModel? _entry;
    private bool _disposed;
    public override string Title => "Your note";
    public FullscreenSessionNotePage(FullscreenContext context, long sessionId, string title, SessionNote? original, Action<SessionNote> saved) : base(context)
    {
        if (context.Services?.GetService<ISessionRepository>() is not { } repository)
        {
            var back = FullscreenUi.Button("Back", context.Back);
            Content = FullscreenUi.Stack(FullscreenUi.Text("Journal is unavailable."), back);
            SetFocusRows([back]);
            return;
        }
        _entry = new JournalEntryViewModel(sessionId, original, repository);
        _entry.EditCommand.Execute(null);
        DataContext = _entry;
        var field = new TextBox { AcceptsReturn = true, FontSize = 28, MinHeight = 180, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        field.Bind(TextBox.TextProperty, new Avalonia.Data.Binding(nameof(JournalEntryViewModel.DraftNote)) { Source = _entry, Mode = Avalonia.Data.BindingMode.TwoWay });
        field.Bind(IsEnabledProperty, new Avalonia.Data.Binding(nameof(JournalEntryViewModel.CanEdit)) { Source = _entry });
        AutomationProperties.SetName(field, "Journal note");
        var edit = FullscreenUi.Button("Edit note", () => context.EditText(field));
        var rate = FullscreenUi.Button("How was that?", () => context.ShowActions("How was that?", Enumerable.Range(1, 5)
            .Select(n => new FullscreenAction($"{n} / 5", () => _entry.RateCommand.Execute(n.ToString(System.Globalization.CultureInfo.InvariantCulture))))
            .Append(new("No rating", () => _entry.ClearRatingCommand.Execute(null))).ToArray()));
        var rating = FullscreenUi.Text("", 28, "TextDim");
        rating.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(JournalEntryViewModel.DraftRatingText)) { Source = _entry });
        var status = FullscreenUi.Text("", 24, "TextDim");
        status.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(JournalEntryViewModel.Problem)) { Source = _entry });
        var save = FullscreenUi.Button("Save", async () =>
        {
            await _entry.SaveCommand.ExecuteAsync(null);
            if (!_disposed && !_entry.IsEditing)
            {
                saved(new SessionNote { SessionId = sessionId, Note = _entry.Note, Rating = _entry.Rating });
                context.Back();
            }
        });
        var cancel = FullscreenUi.Button("Cancel", Cancel);
        foreach (var button in new[] { edit, rate, save, cancel })
            button.Bind(IsEnabledProperty, new Avalonia.Data.Binding(nameof(JournalEntryViewModel.CanEdit)) { Source = _entry });
        Content = FullscreenUi.Scroll(FullscreenUi.Stack(FullscreenUi.Text(title, 64), field, edit, rate, rating, status, save, cancel));
        SetFocusRows([edit], [rate], [save, cancel]);
    }

    public override bool Handle(GamepadButtons buttons)
    {
        if (buttons.HasFlag(GamepadButtons.Back)) { Cancel(); return true; }
        return base.Handle(buttons);
    }

    private void Cancel()
    {
        if (_entry?.IsSaving == true) return;
        _entry?.CancelEditCommand.Execute(null);
        Context.Back();
    }

    public override void Dispose() { _disposed = true; base.Dispose(); }
}

public sealed class FullscreenLibrarySummaryPage : FullscreenPage
{
    private readonly AccountStatsViewModel? _model;
    private bool _disposed;
    private bool _loading;
    private bool _loaded;
    private string? _problem;
    public Task PendingRefresh { get; private set; } = Task.CompletedTask;
    public override string Title => "Library summary";
    public FullscreenLibrarySummaryPage(FullscreenContext context) : base(context)
    {
        _model = context.Services?.GetService<IAccountStatsRepository>() is { } repository ? new AccountStatsViewModel(repository) : null;
        Render();
        AttachedToVisualTree += (_, _) => _ = RefreshAsync();
    }

    private Task RefreshAsync()
    {
        if (_disposed || _loading || _model is null) return PendingRefresh;
        _loading = true; _problem = null;
        var recoverFocus = IsKeyboardFocusWithin;
        Render();
        return PendingRefresh = RefreshCoreAsync(recoverFocus);
    }

    private async Task RefreshCoreAsync(bool recoverFocus)
    {
        try { await _model!.RefreshCommand.ExecuteAsync(null); _loaded = true; }
        catch (OperationCanceledException) when (_disposed) { }
        catch (Exception) { _problem = "Couldn't read account statistics. Try again."; }
        finally
        {
            _loading = false;
            if (!_disposed)
            {
                var hadCurrentFocus = IsKeyboardFocusWithin;
                Render();
                // A fast retry can replace its temporary focus target before the
                // queued layout restoration runs. The completed page still needs a target.
                if (recoverFocus && !hadCurrentFocus) FocusInitial();
            }
        }
    }

    private void Render()
    {
        var restoreFocus = PreserveFocus();
        var context = Context;
        var back = FullscreenUi.Button("Back", context.Back);
        var body = FullscreenUi.Stack(FullscreenUi.Text("Library summary", 64), FullscreenUi.Text($"{context.Library.AllGames.Count:N0} games in your library", 32));
        var focus = new List<Control[]>();
        if (_problem is { } problem)
        {
            body.Children.Add(FullscreenUi.Text(problem, 28, "Amber"));
            var retry = FullscreenUi.Button("Try again", () => _ = RefreshAsync());
            body.Children.Add(retry); focus.Add([retry]);
        }
        else if (_model is not null && (_loading || !_loaded))
            body.Children.Add(FullscreenUi.Text("Reading your account statistics…", 28, "TextDim"));
        if (_model is { HasFacts: true } stats)
        {
            body.Children.Add(FullscreenUi.Text(stats.IntroMessage, 28, "TextDim"));
            if (stats.IsMixedCurrency) body.Children.Add(FullscreenUi.Text(stats.MixedCurrencyMessage, 28, "Amber"));
            void Group(string label, IEnumerable<AccountStatRow> rows, string note)
            {
                var text = string.Join("\n\n", rows.Select(row => $"{row.Label}     {row.CountText}     {row.AmountText}"));
                var button = FullscreenUi.Button(label, () => context.Push(new FullscreenDetailsReadingPage(context, label, $"{note}\n\n{text}")));
                body.Children.Add(button); focus.Add([button]);
            }
            Group(stats.SpendHeading, stats.SpendRows, stats.SpendNote);
            Group(stats.YearHeading, stats.YearRows, stats.YearNote);
            Group(stats.KindHeading, stats.KindRows, stats.GiftsNote);
            Group(stats.RefundHeading, stats.RefundRows, stats.RefundNote);
            Group(stats.BundleHeading, stats.BundleRows, stats.BundleNote);
            Group(stats.DiscountHeading, stats.DiscountRows, stats.DiscountNote);
            Group(stats.WalletHeading, stats.WalletRows, stats.WalletNote);
            Group(stats.LicenceHeading, stats.LicenceRows, stats.LicenceNote);
            Group(stats.CurrencyHeading, stats.CurrencyRows, stats.CurrencyNote);
            Group(stats.CaptureHeading, stats.CaptureRows, stats.CaptureNote);
        }
        else if (_problem is null && !_loading && (_loaded || _model is null))
            body.Children.Add(FullscreenUi.Text(_model?.EmptyMessage ?? "Account statistics are unavailable.", 28, "TextDim"));
        body.Children.Add(back); focus.Add([back]); Content = FullscreenUi.Scroll(body); SetFocusRows(focus.ToArray());
        restoreFocus();
    }

    public override void Dispose() { _disposed = true; _model?.RefreshCommand.Cancel(); base.Dispose(); }
}
