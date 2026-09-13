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
    private readonly StackPanel _actions = new() { Orientation = Orientation.Horizontal, Spacing = 24 };
    private readonly TextBlock _identity = FullscreenUi.Text("", 28, "TextDim");
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
        var title = FullscreenUi.Text(details.Title, details.Title.Length > 45 ? 72 : 80);
        title.Bind(TextBlock.TextProperty, new Binding(nameof(GameDetailsViewModel.Title)) { Source = details });
        title.MaxLines = 2;
        title.MaxWidth = 1050;
        title.HorizontalAlignment = HorizontalAlignment.Left;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.Classes.Add("tv-title");
        var hero = new Grid { MinHeight = 240 };
        var heroText = FullscreenUi.Stack(title, _identity, actions);
        heroText.MaxWidth = 1050;
        heroText.HorizontalAlignment = HorizontalAlignment.Left;
        hero.Children.Add(heroText);
        _tabs = new[] { "Overview", "Updates", "Journal", "Library" }.Select((label, index) =>
            FullscreenUi.Button(label, () => SelectTab(index))).ToArray();
        var tabRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24, Margin = new Thickness(0, 12) };
        tabRow.Children.Add(FullscreenGlyphs.Icon("LT"));
        foreach (var tab in _tabs) { tab.Classes.Add("tv-details-tab"); tabRow.Children.Add(tab); }
        tabRow.Children.Add(FullscreenGlyphs.Icon("RT"));
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        layout.Children.Add(hero);
        Grid.SetRow(tabRow, 1);
        layout.Children.Add(tabRow);
        Grid.SetRow(_body, 2);
        layout.Children.Add(_body);
        Content = layout;
        _rows.Add(actions.Children.ToArray());
        _rows.Add(_tabs);
        AttachedToVisualTree += (_, _) =>
        {
            details.RequestCover(800);
            details.Screenshots?.RequestThumbnails(3);
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
        _details.Screenshots?.RequestThumbnails(3);
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
        var updatesLabel = FullscreenUi.Text(_details.HasUnreadUpdates ? $"Updates {_details.UnreadUpdateCount}" : "Updates");
        _tabs[1].Content = _details.HasUnreadUpdates
            ? new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16,
                Children = { FullscreenUi.Text("●", 24, "Flare"), updatesLabel } }
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
        var history = FullscreenUi.Stack(FullscreenUi.Text("YOUR HISTORY", 24, "TextDim"));
        var figures = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 24 };
        if (_details.PlaytimeText != "—")
        {
            var played = FullscreenUi.Stack(FullscreenUi.Text(_details.PlaytimeText, 56), FullscreenUi.Text("played", 24, "TextDim"));
            played.Spacing = 4;
            figures.Children.Add(played);
        }
        if (_details.HasGap)
        {
            var away = FullscreenUi.Stack(FullscreenUi.Text(_details.IdleText, 56), FullscreenUi.Text("since last played", 24, "TextDim"));
            away.Spacing = 4;
            Grid.SetColumn(away, 1);
            figures.Children.Add(away);
        }
        if (figures.Children.Count > 0) history.Children.Add(figures);
        if (_details.HasGap) history.Children.Add(FullscreenUi.Text($"Last played {_details.LastPlayedText}", 24, "TextDim"));
        history.Children.Add(FullscreenUi.Text(_details.BucketLabel, 28));
        if (_details.HasUnreadUpdates) history.Children.Add(FullscreenUi.Text(_details.UpdatesShortcutText, 28, "Flare"));
        var historyButton = FullscreenUi.Button("Play history", () => Context.Push(new FullscreenDetailsHistoryPage(Context, _details.Tracker)));
        historyButton.HorizontalAlignment = HorizontalAlignment.Left;
        historyButton.MinHeight = 44;
        historyButton.Padding = new Thickness(12, 4);
        history.Children.Add(historyButton);
        var overviewControls = new List<Control>();
        if (_details.Journal?.Entries.FirstOrDefault(entry => entry.HasNote) is { } latest)
        {
            var note = FullscreenUi.Text(latest.Note!, 28);
            note.Bind(TextBlock.TextProperty, new Binding(nameof(JournalEntryViewModel.Note)) { Source = latest });
            note.MaxLines = 2;
            note.TextTrimming = TextTrimming.CharacterEllipsis;
            var openJournal = FullscreenUi.Button("Open journal", () => SelectTab(2));
            openJournal.HorizontalAlignment = HorizontalAlignment.Left;
            openJournal.MinHeight = 44;
            openJournal.Padding = new Thickness(12, 4);
            var journal = FullscreenUi.Stack(FullscreenUi.Text("LATEST NOTE", 24, "TextDim"), note, openJournal);
            journal.Spacing = 8;
            journal.Margin = new Thickness(0, 12, 0, 0);
            history.Children.Add(journal);
            overviewControls.Add(openJournal);
        }
        var summary = FullscreenUi.Text(_details.SummaryText ?? _details.EmptyBodyText, 28);
        summary.MaxLines = 3;
        summary.TextTrimming = TextTrimming.CharacterEllipsis;
        var aboutButton = FullscreenUi.Button("Read more", ShowAbout);
        var about = FullscreenUi.Stack(OverviewHeading("ABOUT", aboutButton), summary);
        about.Spacing = 8;
        history.Spacing = 8;
        _rows.Add([historyButton, aboutButton]);
        Button? gallery = null;
        if (_details.Screenshots is { HasShots: true } shots)
        {
            gallery = FullscreenUi.Button("View gallery", () =>
            {
                shots.SelectCommand.Execute(shots.Shots[0]);
                if (shots.Lightbox is { } lightbox) Context.Push(new FullscreenDetailsScreenshotPage(Context, lightbox));
            });
            about.Children.Add(OverviewHeading("SCREENSHOTS", gallery));
            overviewControls.Add(gallery);
            _rows.Add(overviewControls.ToArray());
            overviewControls.Clear();
            var row = new Grid { Name = "FullscreenOverviewScreenshots", ColumnDefinitions = new ColumnDefinitions("*,*"), Height = 240 };
            var controls = new List<Control>();
            foreach (var shot in shots.Shots.Take(2))
            {
                var button = FullscreenUi.Button(shot.AutomationName, () =>
                {
                    shots.SelectCommand.Execute(shot);
                    if (shots.Lightbox is { } lightbox) Context.Push(new FullscreenDetailsScreenshotPage(Context, lightbox));
                });
                var image = new Image { Stretch = Stretch.UniformToFill };
                image.Bind(Image.SourceProperty, new Binding(nameof(GameScreenshotViewModel.Image)) { Source = shot });
                button.Content = image;
                button.ClipToBounds = true;
                button.Padding = new Thickness(0, 0, 0, 8);
                button.Margin = new Thickness(controls.Count == 0 ? 0 : 16, 0, 0, 0);
                button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                button.VerticalContentAlignment = VerticalAlignment.Stretch;
                AutomationProperties.SetName(button, shot.AutomationName);
                Grid.SetColumn(button, controls.Count);
                row.Children.Add(button);
                controls.Add(button);
            }
            about.Children.Add(row);
            overviewControls.AddRange(controls);
        }
        if (overviewControls.Count > 0) _rows.Add(overviewControls.ToArray());
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("3*,7*"), Name = "FullscreenDetailsOverview" };
        history.Margin = new Thickness(0, 0, 40, 0);
        columns.Children.Add(history);
        var aboutRegion = new Border { Child = about, BorderThickness = new Thickness(1, 0, 0, 0),
            Padding = new Thickness(40, 0, 0, 0) };
        aboutRegion[!Border.BorderBrushProperty] = new DynamicResourceExtension("Line");
        Grid.SetColumn(aboutRegion, 1);
        columns.Children.Add(aboutRegion);
        return columns;
    }

    private static Grid OverviewHeading(string text, Button action)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var heading = FullscreenUi.Text(text, 24, "TextDim");
        heading.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(heading);
        action.MinHeight = 44;
        action.Padding = new Thickness(12, 4);
        Grid.SetColumn(action, 1);
        row.Children.Add(action);
        return row;
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
        var content = FullscreenUi.Stack(FullscreenUi.Text("Updates", 32));
        if (_details.HasNoUpdates) content.Children.Add(FullscreenUi.Text("No updates recorded yet."));
        foreach (var update in _details.Updates)
        {
            var button = Action($"{update.DateText} · {update.Headline}", () =>
            {
                if (update.Link is { } link) Context.OpenLink(link);
                else Context.Push(new FullscreenDetailsReadingPage(Context, update.Headline, "No patch notes page available for this update."));
            });
            AutomationProperties.SetName(button, update.AutomationName);
            AutomationProperties.SetAutomationId(button, $"update-{update.ReleaseId}-{update.OccurredAtUtc.Ticks}-{update.IsAnnouncement}");
            if (update.IsUnread)
            {
                var label = (Control)button.Content!;
                button.Content = null;
                var line = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
                line.Children.Add(FullscreenUi.Text("●", 28, "Flare"));
                label.Margin = new Thickness(24, 0, 0, 0);
                Grid.SetColumn(label, 1);
                line.Children.Add(label);
                button.Content = line;
            }
            content.Children.Add(button);
        }
        if (_details.HasGogPatchNotes)
            content.Children.Add(Action("Patch notes", () => Context.Push(new FullscreenDetailsReadingPage(Context, "Patch notes", _details.GogPatchNotes!))));
        if (_details.ShowDismissFlag || _details.ShowRestoreFlag)
        {
            content.Children.Add(Action(_details.ShowDismissFlag ? _details.DismissFlagLabel : _details.RestoreFlagLabel, async () =>
            {
                if (_details.ShowDismissFlag) await _details.DismissFlagCommand.ExecuteAsync(null);
                else await _details.RestoreFlagCommand.ExecuteAsync(null);
                if (_details.FlagProblem is { } error) Context.Notify(error);
                SelectTab(1);
            }));
            content.Children.Add(FullscreenUi.Text(_details.ShowDismissFlag ? _details.DismissFlagNote : _details.RestoreFlagNote, 24, "TextDim"));
        }
        return content;
    }

    private Control Journal()
    {
        var content = FullscreenUi.Stack(FullscreenUi.Text("Journal", 32));
        if (_details.Journal is not { HasEntries: true } journal)
        {
            content.Children.Add(FullscreenUi.Text(_details.Journal?.EmptyText ?? "No journal entries yet."));
            return content;
        }
        foreach (var entry in journal.Entries)
        {
            var button = Action($"{entry.DateText}  {entry.RatingText}\n{entry.Note}", () =>
                Context.Push(new FullscreenDetailsJournalPage(Context, entry)));
            AutomationProperties.SetAutomationId(button, $"journal-{entry.SessionId}");
            content.Children.Add(button);
        }
        return content;
    }

    private Control Library()
    {
        var content = FullscreenUi.Stack(FullscreenUi.Text("Your copies", 32));
        if (!_details.ShowCopyBreakdown)
            foreach (var copy in _details.OwnCopies)
                content.Children.Add(FullscreenUi.Text(string.Join(" · ", new[] { copy.Store, copy.InstallState,
                    copy.Playtime, copy.HasLastPlayed ? $"Last played {copy.LastPlayed}" : null }.Where(value => value is not null))));
        if (_details.Coverage is { Rows.Count: > 0 } coverage)
        {
            if (coverage.HasCoverage) content.Children.Add(FullscreenUi.Text("Also covers", 32));
            if (coverage.IsComposite)
            {
                content.Children.Add(FullscreenUi.Text($"{coverage.TotalPlaytimeText} total · Last played {coverage.TotalLastPlayedText}"));
                content.Children.Add(FullscreenUi.Text(coverage.TotalNote, 24, "TextDim"));
            }
            foreach (var row in coverage.Rows)
            {
                content.Children.Add(FullscreenUi.Text($"{row.Title} · {row.StoreBadge}\n{row.PlaytimeText} · {row.LastPlayedText}"));
                if (row.Achievements is { } achievements)
                    content.Children.Add(FullscreenUi.Text($"Achievements: {achievements.SummaryText}", 24, "TextDim"));
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
                content.Children.Add(FullscreenUi.Text("Expansions", 32));
                content.Children.Add(FullscreenUi.Text(expansions.Note, 24, "TextDim"));
                foreach (var expansion in expansions.Expansions) AddExpansion(content, expansions, expansion);
            }
            if (expansions.Extends is { } parent)
            {
                content.Children.Add(FullscreenUi.Text("Extends", 32));
                content.Children.Add(FullscreenUi.Text(expansions.ExtendsNote, 24, "TextDim"));
                AddExpansion(content, expansions, parent);
            }
        }
        if (_details.Acquisition is { } acquired)
            content.Children.Add(FullscreenUi.Text(string.Join(" · ", new[] { acquired.HasDate ? $"Acquired {acquired.DateText}" : null,
                acquired.HasLicence ? acquired.LicenseText : null }.Where(value => value is not null)), 24, "TextDim"));
        if (_details.HasLifecycle) content.Children.Add(FullscreenUi.Text(_details.LifecycleText!, 28));
        if (_details.HasTechnicalFacts)
        {
            content.Children.Add(FullscreenUi.Text("Technical facts", 32));
            if (_details.HasSteamAppId) content.Children.Add(FullscreenUi.Text($"Steam app ID: {_details.SteamAppId}", 24, "TextDim"));
            if (_details.HasInstallPath)
                content.Children.Add(Action("Installation path", () => Context.Push(new FullscreenDetailsReadingPage(Context, "Installation path", _details.InstallPath!))));
        }
        if (_details.Lists is { } lists)
        {
            content.Children.Add(FullscreenUi.Text("Lists", 32));
            foreach (var list in lists.Rows)
            {
                var button = Action(list.SelectionLabel, () => list.IsMember = !list.IsMember);
                button.Bind(ContentControl.ContentProperty, new Binding(nameof(list.SelectionLabel)) { Source = list });
                button.Bind(AutomationProperties.NameProperty, new Binding(nameof(list.AutomationName)) { Source = list });
                button.Bind(AutomationProperties.ItemStatusProperty, new Binding(nameof(list.StatusText)) { Source = list });
                content.Children.Add(button);
                var status = FullscreenUi.Text("", 24, "Amber");
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
        content.Children.Add(FullscreenUi.Text($"{row.Title} · {row.StoreNames}\n{row.PlaytimeText} · {row.LastPlayedText}"));
        content.Children.Add(Action(row.UngroupAutomationName, () => Context.ShowActions($"Ungroup {row.Title}?",
            [new("Cancel", () => { }), new(row.UngroupLabel, async () =>
            {
                await expansions.UngroupCommand.ExecuteAsync(row);
                Context.Notify(expansions.Problem ?? "Expansion ungrouped.");
            })])));
    }

    private Button Action(string text, Action action)
    {
        var button = FullscreenUi.Button(text, action);
        button.Content = new TextBlock { Text = text, FontSize = 28, TextWrapping = TextWrapping.Wrap,
            MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis };
        AutomationProperties.SetName(button, text);
        _rows.Add([button]);
        return button;
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
