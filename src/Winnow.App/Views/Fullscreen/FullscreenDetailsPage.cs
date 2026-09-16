using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Winnow.App.Services;
using Winnow.App.ViewModels;

namespace Winnow.App.Views.Fullscreen;

/// <summary>A television composition over the shared game-detail operations.</summary>
public sealed class FullscreenDetailsPage : FullscreenPage
{
    private readonly GameDetailsViewModel _details;
    private readonly ContentControl _body = new();
    private readonly List<Control[]> _rows = [];
    private readonly Button[] _tabs;
    private readonly FullscreenBackdrop? _backdrop;
    private readonly LinearGradientBrush _dividerMask = new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Absolute),
        EndPoint = new RelativePoint(120, 0, RelativeUnit.Absolute),
        GradientStops = [new GradientStop(Colors.White, 0), new GradientStop(Colors.Transparent, 1)]
    };
    private readonly StackPanel _actions = new() { Orientation = Orientation.Horizontal, Spacing = 24 };
    private readonly TextBlock _identity = FullscreenUi.Text("", 24, "TextDim");
    private int _tab;

    public FullscreenDetailsPage(FullscreenContext context, GameDetailsViewModel details, int selectedSection = 0) : base(context)
    {
        _details = details;
        _backdrop = context is null ? null : new FullscreenBackdrop(context, details.Tile, cinematic: true);
        DataContext = details;
        Styles.Add(new Style(s => s.OfType<Button>().Class("tv-details-primary"))
        {
            Setters = {
                new Setter(Button.BackgroundProperty, new DynamicResourceExtension("Volt")),
                new Setter(Button.ForegroundProperty, new DynamicResourceExtension("VoltInk")),
                new Setter(Button.BorderBrushProperty, new DynamicResourceExtension("Volt"))
            }
        });
        Styles.Add(new Style(s => s.OfType<Button>().Class("tv-details-primary").Class(":focus"))
        { Setters = {
            new Setter(Button.BorderBrushProperty, new DynamicResourceExtension("VoltInk")),
            new Setter(Button.ForegroundProperty, new DynamicResourceExtension("VoltInk")) } });
        Styles.Add(new Style(s => s.OfType<Button>().Class("tv-details-primary").Class(":pointerover"))
        { Setters = { new Setter(Button.BackgroundProperty, new DynamicResourceExtension("VoltHover")) } });
        Styles.Add(new Style(s => s.OfType<Button>().Class("tv-details-primary").Class(":pressed"))
        { Setters = { new Setter(Button.BackgroundProperty, new DynamicResourceExtension("VoltPress")) } });
        Styles.Add(new Style(s => s.OfType<Button>().Class("tv-details-tab").Class("current"))
        { Setters = { new Setter(Button.BorderBrushProperty, new DynamicResourceExtension("Volt")) } });
        Styles.Add(new Style(s => s.OfType<Button>().Class("tv-details-tab").Class(":focus"))
        { Setters = {
            new Setter(Button.BackgroundProperty, new DynamicResourceExtension("SurfaceRaised")),
            new Setter(Button.BorderBrushProperty, Brushes.Transparent) } });
        Styles.Add(new Style(s => s.OfType<Button>().Class("tv-details-tab").Class("current").Class(":focus"))
        { Setters = { new Setter(Button.BorderBrushProperty, new DynamicResourceExtension("Volt")) } });
        var actions = _actions;
        RefreshHero();
        var title = DetailText(details.Title, details.Title.Length > 45 ? 64 : 76, weight: FontWeight.Bold);
        title.Bind(TextBlock.TextProperty, new Binding(nameof(GameDetailsViewModel.Title)) { Source = details });
        title.MaxLines = 2;
        title.MaxWidth = 1050;
        title.HorizontalAlignment = HorizontalAlignment.Left;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.Classes.Add("tv-title");
        var hero = new Grid { MinHeight = 232 };
        var heroText = FullscreenUi.Stack(title, _identity, actions);
        heroText.Spacing = 12;
        heroText.MaxWidth = 1050;
        heroText.HorizontalAlignment = HorizontalAlignment.Left;
        hero.Children.Add(heroText);
        _tabs = new[] { "Overview", "Updates", "Journal", "Library" }.Select((label, index) =>
            FullscreenUi.Tab(label, () => SelectTab(index))).ToArray();
        var tabRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24, Margin = new Thickness(0, 8),
            HorizontalAlignment = HorizontalAlignment.Left };
        tabRow.Children.Add(FullscreenGlyphs.Icon("LT"));
        foreach (var tab in _tabs)
        {
            tab.Classes.Add("tv-details-tab");
            tab.FontSize = 24;
            tab.MinHeight = 52;
            tab.Padding = new Thickness(18, 8);
            tabRow.Children.Add(tab);
        }
        tabRow.Children.Add(FullscreenGlyphs.Icon("RT"));
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        layout.Children.Add(hero);
        var tabRegion = new Border { Child = tabRow, BorderThickness = new Thickness(0, 0, 0, 1) };
        tabRegion[!Border.BorderBrushProperty] = new DynamicResourceExtension("Line");
        // Keep the tabs opaque; only the rule beyond the final trigger fades into the artwork.
        tabRow.SizeChanged += (_, _) =>
        {
            _dividerMask.StartPoint = new RelativePoint(tabRow.Bounds.Width + 24, 0, RelativeUnit.Absolute);
            _dividerMask.EndPoint = new RelativePoint(tabRow.Bounds.Width + 120, 0, RelativeUnit.Absolute);
        };
        tabRegion.OpacityMask = _dividerMask;
        Grid.SetRow(tabRegion, 1);
        layout.Children.Add(tabRegion);
        Grid.SetRow(_body, 2);
        layout.Children.Add(_body);
        Content = layout;
        _rows.Add(actions.Children.ToArray());
        _rows.Add(_tabs);
        AttachedToVisualTree += (_, _) =>
        {
            details.RequestCover(800);

            if (_tab is 0 or 2)
            {
                var restore = PreserveFocus();
                SelectTab(_tab, false);
                restore();
            }
        };
        SelectTab(Math.Clamp(selectedSection, 0, 3), false);
        details.SnapshotChanged += DetailsSnapshotChanged;
    }

    public override string Title => _details.Title;
    public override Control? Backdrop => _backdrop;
    public override string Hints => "A Select   B Back   Y More";
    public override string RightHints => "LT / RT Section";
    public int SelectedSection => _tab;

    private void RefreshHero()
    {
        _actions.Children.Clear();
        if (_details.HasPrimaryAction)
        {
            var primary = FullscreenUi.Button(_details.PrimaryAction!.Label, () => Context.Play(_details.Tile));
            primary.FontSize = 36;
            primary.MinHeight = 76;
            primary.MinWidth = 220;
            primary.BorderThickness = new Thickness(3);
            primary.Classes.Add("tv-details-primary");
            primary.Template = new FuncControlTemplate<Button>((button, _) =>
            {
                var border = new Border { CornerRadius = new CornerRadius(8) };
                border.Bind(Border.BackgroundProperty, new Binding(nameof(Button.Background)) { Source = button });
                border.Bind(Border.BorderBrushProperty, new Binding(nameof(Button.BorderBrush)) { Source = button });
                border.Bind(Border.BorderThicknessProperty, new Binding(nameof(Button.BorderThickness)) { Source = button });
                border.Bind(Border.PaddingProperty, new Binding(nameof(Button.Padding)) { Source = button });
                var presenter = new ContentPresenter { VerticalContentAlignment = VerticalAlignment.Center };
                presenter.Bind(ContentPresenter.ContentProperty, new Binding(nameof(Button.Content)) { Source = button });
                border.Child = presenter;
                return border;
            });
            var label = FullscreenUi.Text(_details.PrimaryAction.Label, 36, "VoltInk");
            label.FontWeight = FontWeight.SemiBold;
            primary.Content = _details.Tile.IsPlayAction
                ? new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16,
                    Children = { FullscreenUi.Text("▶", 28, "VoltInk"), label } }
                : label;
            AutomationProperties.SetAutomationId(primary, "details-primary-action");
            _actions.Children.Add(primary);
        }
        if (_details.Tile.Entries.Count > 1)
            _actions.Children.Add(FullscreenUi.Button("Choose launch version", () => Context.ChooseVersion(_details.Tile)));
        _actions.Children.Add(FullscreenUi.Button("More", ShowMore));
        _identity.Text = string.Join(" · ", new[] { _details.HasInstallState ? _details.InstallText : null, _details.StoreNames }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private void DetailsSnapshotChanged(object? sender, EventArgs e)
    {
        var restore = PreserveFocus();
        RefreshHero();
        _rows[0] = _actions.Children.ToArray();
        _backdrop?.Select(_details.Tile);
        SelectTab(_tab, false);

        restore();
    }

    public override void Dispose()
    {
        _details.SnapshotChanged -= DetailsSnapshotChanged;
        base.Dispose();
    }

    public override bool Handle(GamepadButtons buttons)
    {
        if (buttons.HasFlag(GamepadButtons.PagePrevious)) { SelectTab((_tab + 3) % 4); return true; }
        if (buttons.HasFlag(GamepadButtons.PageNext)) { SelectTab((_tab + 1) % 4); return true; }
        if (buttons.HasFlag(GamepadButtons.Keyboard)) { ShowMore(); return true; }
        return base.Handle(buttons);
    }

    private void SelectTab(int index, bool focus = true)
    {
        _tab = index;
        var updatesLabel = new FullscreenNavigationLabel
        { Text = _details.HasUnreadUpdates ? $"Updates {_details.UnreadUpdateCount}" : "Updates" };
        var unreadDot = FullscreenUi.Text("●", 24, "Flare");
        unreadDot.FontWeight = FontWeight.Normal;
        _tabs[1].Content = _details.HasUnreadUpdates
            ? new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16,
                Children = { unreadDot, updatesLabel } }
            : updatesLabel;
        AutomationProperties.SetName(_tabs[1], _details.UpdatesTabAutomationName);
        for (var i = 0; i < _tabs.Length; i++) _tabs[i].Classes.Set("current", i == index);
        while (_rows.Count > 2) _rows.RemoveAt(_rows.Count - 1);
        var content = index switch
        {
            1 => Updates(),
            2 => Journal(),
            3 => Library(),
            _ => Overview(),
        };
        _body.Content = FullscreenUi.Scroll(content);
        SetFocusRows(_rows.ToArray());
        Changed();
        if (focus) FocusControl(_tabs[index]);
    }

    private Control Overview()
    {
        var history = new StackPanel { Spacing = 14 };
        history.Children.Add(DetailText("YOUR HISTORY", 18, "TextDim"));
        var figures = new Grid { ColumnDefinitions = new ColumnDefinitions("160,Auto,*"), ColumnSpacing = 28 };
        if (_details.PlaytimeText != "—")
        {
            var played = Metric(_details.PlaytimeText, "played");
            figures.Children.Add(played);
        }
        if (_details.HasGap)
        {
            if (figures.Children.Count > 0)
            {
                var metricRule = Rule(false, 84);
                Grid.SetColumn(metricRule, 1);
                figures.Children.Add(metricRule);
            }
            var away = Metric(_details.IdleText, "since last played");
            if (figures.Children.Count > 0) Grid.SetColumn(away, 2);
            else Grid.SetColumnSpan(away, 3);
            figures.Children.Add(away);
        }
        if (figures.Children.Count > 0) history.Children.Add(figures);
        if (_details.HasGap)
        {
            var lastPlayed = DetailText($"Last played {_details.LastPlayedText}", 22, "TextDim");
            lastPlayed.Margin = new Thickness(0, 10, 0, 0);
            history.Children.Add(lastPlayed);
        }
        history.Children.Add(DetailText(_details.BucketLabel, 32, weight: FontWeight.Bold));
        if (_details.HasUnreadUpdates)
            history.Children.Add(DetailText(_details.UpdatesShortcutText, 24, "Flare"));
        var historyButton = DetailLink("Play history", () => Context.Push(new FullscreenDetailsHistoryPage(Context,
            _details.Tracker, _details.SteamActivity, () => _details.ShowSteamActivity)));
        history.Children.Add(historyButton);
        var divider = Rule(true);
        divider.Margin = new Thickness(0, 20, 0, 18);
        history.Children.Add(divider);
        var latest = _details.Journal?.Entries.FirstOrDefault(entry => entry.HasNote);
        history.Children.Add(DetailText(latest is null ? "JOURNAL" : "LATEST NOTE", 18, "TextDim"));
        if (latest is not null)
        {
            var note = DetailText(latest.Note!, 24);
            note.Bind(TextBlock.TextProperty, new Binding(nameof(JournalEntryViewModel.Note)) { Source = latest });
            note.MaxLines = 3;
            note.TextTrimming = TextTrimming.CharacterEllipsis;
            history.Children.Add(note);
        }
        var journalButton = DetailLink("Open journal", () => SelectTab(2));
        history.Children.Add(journalButton);

        var summary = DetailText(_details.SummaryText ?? _details.EmptyBodyText, 24);
        summary.MaxLines = 2;
        summary.TextTrimming = TextTrimming.CharacterEllipsis;
        var aboutButton = DetailLink("Read more", ShowAbout);
        var prefix = new StackPanel { Spacing = 12 };
        prefix.Children.Add(DetailText("ABOUT", 18, "TextDim"));
        prefix.Children.Add(summary);
        prefix.Children.Add(aboutButton);
        var about = new StackPanel { Spacing = 12 };
        about.Children.Add(prefix);
        _rows.Add([historyButton, aboutButton]);
        var screenshotControls = new List<Control> { journalButton };
        Button? gallery = null;
        if (_details.Screenshots is { HasShots: true } shots)
        {
            var heading = DetailText("SCREENSHOTS", 18, "TextDim");
            heading.Margin = new Thickness(0, 8, 0, 0);
            prefix.Children.Add(heading);
            var row = new Grid { Name = "FullscreenOverviewScreenshots", ColumnDefinitions = new ColumnDefinitions("*,*"),
                ColumnSpacing = 16, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
            foreach (var (shot, index) in shots.Shots.Take(2).Select((shot, index) => (shot, index)))
            {
                var button = FullscreenUi.Button(shot.AutomationName, () =>
                {
                    shots.SelectCommand.Execute(shot);
                    if (shots.Lightbox is { } lightbox) Context.Push(new FullscreenDetailsScreenshotPage(Context, lightbox));
                });
                button.Content = new FullscreenScreenshotPreview(shots, shot);
                button.Padding = new Thickness(0, 0, 0, 4);
                button.MinHeight = 0;
                button.VerticalAlignment = VerticalAlignment.Top;
                button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                button.VerticalContentAlignment = VerticalAlignment.Stretch;
                AutomationProperties.SetName(button, shot.AutomationName);
                button.GotFocus += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (!button.IsKeyboardFocusWithin) return;
                    button.UpdateLayout();
                    button.BringIntoView();
                }, Avalonia.Threading.DispatcherPriority.Background);
                Grid.SetColumn(button, index);
                row.Children.Add(button);
                screenshotControls.Add(button);
            }
            about.Children.Add(row);
            gallery = DetailLink("View gallery", () =>
            {
                shots.SelectCommand.Execute(shots.Shots[0]);
                if (shots.Lightbox is { } lightbox) Context.Push(new FullscreenDetailsScreenshotPage(Context, lightbox));
            });
            about.Children.Add(gallery);
            row.SizeChanged += (_, _) =>
            {
                if (row.Children.OfType<Button>().FirstOrDefault(button => button.IsKeyboardFocusWithin) is not { } focused) return;
                // A new image or text scale can resize the row after the initial focus scroll.
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (focused.IsKeyboardFocusWithin) focused.BringIntoView();
                }, Avalonia.Threading.DispatcherPriority.Loaded);
            };
            // Spend the remaining overview height on full images. Enlarged text can scroll.
            row.LayoutUpdated += (_, _) =>
            {
                if (_body.Bounds.Height <= 0 || prefix.Bounds.Height <= 0) return;
                var minimumPreview = Math.Min(180, Math.Max(80, _body.Bounds.Height - 8));
                var available = Math.Max(minimumPreview, _body.Bounds.Height - prefix.Bounds.Height - gallery.Bounds.Height - 48);
                if (Math.Abs(row.MaxHeight - available) > .5 || double.IsInfinity(row.MaxHeight))
                    row.MaxHeight = available;
                var aspect = row.Children.OfType<Button>().Select(button => ((FullscreenScreenshotPreview)button.Content!).AspectRatio).DefaultIfEmpty(16d / 9).Min();
                var width = Math.Min(about.Bounds.Width, Math.Max(0, available - 4) * aspect * 2 + 16);
                if (width > 0 && (double.IsNaN(row.Width) || Math.Abs(row.Width - width) > .5)) row.Width = width;
            };
        }
        _rows.Add(screenshotControls.ToArray());
        if (gallery is not null) _rows.Add([gallery]);
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("28*,72*"), Name = "FullscreenDetailsOverview",
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 20, 0, 0) };
        history.Margin = new Thickness(0, 0, 44, 0);
        columns.Children.Add(history);
        var aboutRegion = new Border { Child = about, BorderThickness = new Thickness(1, 0, 0, 0),
            Padding = new Thickness(44, 0, 0, 0) };
        aboutRegion[!Border.BorderBrushProperty] = new DynamicResourceExtension("Line");
        Grid.SetColumn(aboutRegion, 1);
        columns.Children.Add(aboutRegion);
        return columns;
    }

    private static TextBlock DetailText(string text, double size, string brush = "Text", FontWeight? weight = null)
        => FullscreenInformation.Text(text, size, brush, weight);

    private static StackPanel Metric(string value, string caption) => new()
    {
        Spacing = 4,
        Children = { DetailText(value, 60, weight: FontWeight.Bold), DetailText(caption, 22, "TextDim") }
    };

    private Border Rule(bool horizontal, double length = double.NaN)
    {
        var rule = new Border { Name = horizontal ? "FullscreenDetailsHorizontalRule" : "FullscreenDetailsMetricRule" };
        rule[!Border.BackgroundProperty] = new DynamicResourceExtension("Line");
        if (horizontal) { rule.Height = 1; rule.Width = length; rule.OpacityMask = _dividerMask; }
        else { rule.Width = 1; rule.Height = length; rule.VerticalAlignment = VerticalAlignment.Center; }
        return rule;
    }

    private static Button DetailLink(string text, Action action)
    {
        var button = FullscreenInformation.Link(text, action);
        button.Classes.Add("tv-details-link");
        return button;
    }

    private void ShowAbout()
    {
        var paragraphs = new List<string>();
        if (_details.HasIdentityLine) paragraphs.Add($"{_details.IdentityYearText}{_details.Publisher}");
        paragraphs.Add(_details.Summary ?? _details.EmptyBodyText);
        if (_details.Reception is { } reception)
            paragraphs.AddRange(reception.Figures.Select(figure => $"{figure.Source}: {figure.Value} · {figure.Count}"));
        Context.Push(new FullscreenDetailsReadingPage(Context, _details.Title, string.Join("\n\n", paragraphs)));
    }

    private Control Updates()
    {
        var content = SectionContent("Updates", "UPDATES");
        if (_details.HasNoUpdates) content.Children.Add(DetailText("No updates recorded yet.", 24, "TextDim"));
        foreach (var update in _details.Updates)
        {
            if (content.Children.Count > 1) AddDivider(content);
            var button = Action($"{update.DateText} · {update.Headline}", () =>
            {
                if (update.Link is { } link) Context.OpenLink(link);
                else Context.Push(new FullscreenDetailsReadingPage(Context, update.Headline, "No patch notes page available for this update."));
            });
            button.Bind(AutomationProperties.NameProperty, new Binding(nameof(update.AutomationName)) { Source = update });
            AutomationProperties.SetAutomationId(button, $"update-{update.ReleaseId}-{update.OccurredAtUtc.Ticks}-{update.IsAnnouncement}");
            var metadata = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
            var marker = DetailText("●", 18, "Flare");
            marker.Bind(IsVisibleProperty, new Binding(nameof(update.IsUnread)) { Source = update });
            metadata.Children.Add(marker);
            metadata.Children.Add(DetailText(update.DateText, 22, "TextDim"));
            var headline = DetailText(update.Headline, 32, weight: FontWeight.Bold);
            headline.MaxLines = 2;
            headline.TextTrimming = TextTrimming.CharacterEllipsis;
            button.Content = TimelineContent(metadata, headline);
            StretchRow(button);
            content.Children.Add(button);
        }
        if (_details.HasGogPatchNotes)
        {
            AddDivider(content);
            content.Children.Add(Action("Patch notes", () => Context.Push(new FullscreenDetailsReadingPage(Context, "Patch notes", _details.GogPatchNotes!))));
        }
        if (_details.ShowDismissFlag || _details.ShowRestoreFlag)
        {
            AddSection(content, "READ STATUS");
            content.Children.Add(Action(_details.ShowDismissFlag ? _details.DismissFlagLabel : _details.RestoreFlagLabel, async () =>
            {
                if (_details.ShowDismissFlag) await _details.DismissFlagCommand.ExecuteAsync(null);
                else await _details.RestoreFlagCommand.ExecuteAsync(null);
                if (_details.FlagProblem is { } error) Context.Notify(error);
                SelectTab(1);
            }));
            content.Children.Add(DetailText(_details.ShowDismissFlag ? _details.DismissFlagNote : _details.RestoreFlagNote, 22, "TextDim"));
        }
        return content;
    }

    private Control Journal()
    {
        var content = SectionContent("Journal", "JOURNAL");
        if (_details.Journal is not { HasEntries: true } journal)
        {
            content.Children.Add(DetailText(_details.Journal?.EmptyText ?? "No journal entries yet.", 24, "TextDim"));
            return content;
        }
        foreach (var entry in journal.Entries)
        {
            if (content.Children.Count > 1) AddDivider(content);
            var button = Action($"{entry.DateText}  {entry.RatingText}\n{entry.Note}", () =>
                Context.Push(new FullscreenDetailsJournalPage(Context, entry)));
            AutomationProperties.SetAutomationId(button, $"journal-{entry.SessionId}");
            var metadata = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24 };
            metadata.Children.Add(DetailText(entry.DateText, 22, "TextDim"));
            var rating = DetailText(entry.RatingText, 22, "Volt");
            rating.Bind(TextBlock.TextProperty, new Binding(nameof(entry.RatingText)) { Source = entry });
            rating.Bind(IsVisibleProperty, new Binding(nameof(entry.HasRating)) { Source = entry });
            metadata.Children.Add(rating);
            var note = DetailText(entry.Note ?? "", 24);
            note.MaxLines = 3;
            note.TextTrimming = TextTrimming.CharacterEllipsis;
            note.Bind(TextBlock.TextProperty, new Binding(nameof(entry.Note)) { Source = entry });
            note.Bind(IsVisibleProperty, new Binding(nameof(entry.HasNote)) { Source = entry });
            button.Content = TimelineContent(metadata, note);
            StretchRow(button);
            content.Children.Add(button);
        }
        return content;
    }

    private Control Library()
    {
        var content = SectionContent("Library", "YOUR COPIES");
        if (!_details.ShowCopyBreakdown)
            foreach (var copy in _details.OwnCopies)
            {
                if (content.Children.Count > 1) AddDivider(content);
                content.Children.Add(DetailText(copy.Store, 32, weight: FontWeight.Bold));
                content.Children.Add(DetailText(string.Join(" · ", new[] { copy.InstallState,
                    copy.Playtime, copy.HasLastPlayed ? $"Last played {copy.LastPlayed}" : null }.Where(value => value is not null)), 22, "TextDim"));
            }
        if (_details.Coverage is { Rows.Count: > 0 } coverage)
        {
            if (coverage.HasCoverage) AddSection(content, "ALSO COVERS");
            if (coverage.IsComposite)
            {
                content.Children.Add(DetailText($"{coverage.TotalPlaytimeText} total · Last played {coverage.TotalLastPlayedText}", 24));
                content.Children.Add(DetailText(coverage.TotalNote, 22, "TextDim"));
            }
            foreach (var (row, index) in coverage.Rows.Select((row, index) => (row, index)))
            {
                if (index > 0 || coverage.IsComposite) AddDivider(content);
                content.Children.Add(DetailText(row.Title, 32, weight: FontWeight.Bold));
                content.Children.Add(DetailText($"{row.StoreBadge} · {row.PlaytimeText} · {row.LastPlayedText}", 22, "TextDim"));
                if (row.Achievements is { } achievements)
                    content.Children.Add(DetailText($"Achievements: {achievements.SummaryText}", 22, "TextDim"));
                if (row.IsCovered)
                    content.Children.Add(Action(row.SeparateAutomationName, () => Context.ShowActions($"Separate {row.Title}?",
                        [new("Cancel", () => { }), new(row.SeparateLabel, async () =>
                        {
                            await coverage.SeparateCommand.ExecuteAsync(row);
                            Context.Notify(coverage.Problem ?? "Title separated.");
                        })])));
            }
        }
        if (_details.Expansions is { } expansions)
        {
            if (expansions.HasExpansions)
            {
                AddSection(content, "EXPANSIONS");
                content.Children.Add(DetailText(expansions.Note, 22, "TextDim"));
                foreach (var expansion in expansions.Expansions) AddExpansion(content, expansions, expansion);
            }
            if (expansions.Extends is { } parent)
            {
                AddSection(content, "EXTENDS");
                content.Children.Add(DetailText(expansions.ExtendsNote, 22, "TextDim"));
                AddExpansion(content, expansions, parent);
            }
        }
        if (_details.Acquisition is { } acquired && (acquired.HasDate || acquired.HasLicence))
        {
            AddSection(content, "ACQUISITION");
            content.Children.Add(DetailText(string.Join(" · ", new[] { acquired.HasDate ? $"Acquired {acquired.DateText}" : null,
                acquired.HasLicence ? acquired.LicenseText : null }.Where(value => value is not null)), 22, "TextDim"));
        }
        if (_details.HasLifecycle)
        {
            AddSection(content, "STATUS");
            content.Children.Add(DetailText(_details.LifecycleText!, 24));
        }
        if (_details.HasTechnicalFacts)
        {
            AddSection(content, "TECHNICAL FACTS");
            if (_details.HasSteamAppId) content.Children.Add(DetailText($"Steam app ID: {_details.SteamAppId}", 22, "TextDim"));
            if (_details.HasInstallPath)
                content.Children.Add(Action("Installation path", () => Context.Push(new FullscreenDetailsReadingPage(Context, "Installation path", _details.InstallPath!))));
        }
        if (_details.Lists is { } || _details.AddToListCommand is not null) AddSection(content, "LISTS");
        if (_details.Lists is { } lists)
        {
            foreach (var list in lists.Rows)
            {
                var button = Action(list.SelectionLabel, () => list.IsMember = !list.IsMember);
                var label = DetailText(list.SelectionLabel, 24);
                label.Bind(TextBlock.TextProperty, new Binding(nameof(list.SelectionLabel)) { Source = list });
                button.Content = label;
                button.Bind(AutomationProperties.NameProperty, new Binding(nameof(list.AutomationName)) { Source = list });
                button.Bind(AutomationProperties.ItemStatusProperty, new Binding(nameof(list.StatusText)) { Source = list });
                content.Children.Add(button);
                var status = DetailText("", 22, "Amber");
                status.Bind(TextBlock.TextProperty, new Binding(nameof(list.StatusText)) { Source = list });
                status.Bind(IsVisibleProperty, new Binding(nameof(list.HasStatus)) { Source = list });
                content.Children.Add(status);
            }
        }
        if (_details.AddToListCommand is { } add)
            content.Children.Add(Action("Add to list", () => add.Execute(_details.Tile)));
        return content;
    }

    private void AddExpansion(StackPanel content, GameExpansionsViewModel expansions, ExpansionRowViewModel row)
    {
        content.Children.Add(DetailText(row.Title, 32, weight: FontWeight.Bold));
        content.Children.Add(DetailText($"{row.StoreNames} · {row.PlaytimeText} · {row.LastPlayedText}", 22, "TextDim"));
        content.Children.Add(Action(row.UngroupAutomationName, () => Context.ShowActions($"Ungroup {row.Title}?",
            [new("Cancel", () => { }), new(row.UngroupLabel, async () =>
            {
                await expansions.UngroupCommand.ExecuteAsync(row);
                Context.Notify(expansions.Problem ?? "Expansion ungrouped.");
            })])));
    }

    private Button Action(string text, Action action)
    {
        var button = DetailLink(text, action);
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        _rows.Add([button]);
        return button;
    }

    private StackPanel SectionContent(string name, string heading)
    {
        var content = FullscreenInformation.Column($"FullscreenDetails{name}");
        content.Children.Add(FullscreenInformation.Heading(heading));
        return content;
    }

    private void AddDivider(StackPanel content)
    {
        var divider = Rule(true);
        divider.Margin = new Thickness(0, 12);
        content.Children.Add(divider);
    }

    private void AddSection(StackPanel content, string heading)
    {
        AddDivider(content);
        content.Children.Add(DetailText(heading, 18, "TextDim"));
    }

    private static StackPanel TimelineContent(Control metadata, Control body) => new()
    {
        Spacing = 10, Children = { metadata, body }
    };

    private static void StretchRow(Button button)
    {
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.Padding = new Thickness(0, 8);
    }

    private void ShowMore()
    {
        var actions = new List<FullscreenAction>();
        if (_details.AddToListCommand is { } add) actions.Add(new("Add to list", () => add.Execute(_details.Tile)));
        foreach (var link in _details.Links) actions.Add(new(link.Label, () => Context.OpenLink(link), IconLabel: "Open website"));
        if (_details.ManagementAction is { } management) actions.Add(new(management.Label, () => Context.OpenLink(management), IconLabel: "Manage installation"));
        if (_details.OpenableFolder is { } folder)
            actions.Add(new("Browse install folder", () => Context.BrowseFolder(folder)));
        if (_details.MetadataEditor is { } editor)
            actions.Add(new(editor.OpenLabel, async () =>
            {
                await editor.OpenCommand.ExecuteAsync(null);
                Context.Push(new FullscreenDetailsMetadataPage(Context, editor));
            }));
        if (_details.IgdbMatch is { } match)
            actions.Add(new(match.OpenLabel, () =>
            {
                match.OpenCommand.Execute(null);
                Context.Push(new FullscreenDetailsMatchPage(Context, match));
            }));
        if (_details.Refetch is { } refetch)
            actions.Add(new(refetch.MenuLabel, async () =>
            {
                await refetch.RefetchCommand.ExecuteAsync(null);
                if (refetch.Status is { } status) Context.Notify(status);
            }));
        if (_details.HideCommand is { } hide)
            actions.Add(new(_details.HideLabel, () => Context.ShowActions($"Hide {_details.Title}?",
                [new("Cancel", () => { }), new("Hide game", async () =>
                {
                    try
                    {
                        if (hide is CommunityToolkit.Mvvm.Input.IAsyncRelayCommand command) await command.ExecuteAsync(_details.Tile);
                        else hide.Execute(_details.Tile);
                    }
                    catch (Exception) { Context.Notify("Could not hide this game. Try again."); }
                })])));
        Context.ShowActions("More", actions);
    }
}
