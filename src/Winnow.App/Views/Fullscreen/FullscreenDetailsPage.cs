using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
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
    private int _tab;

    public FullscreenDetailsPage(FullscreenContext context, GameDetailsViewModel details, int selectedSection = 0) : base(context)
    {
        _details = details;
        _backdrop = context is null ? null : new FullscreenBackdrop(context, details.Tile, cinematic: true);
        DataContext = details;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24 };
        if (details.HasPrimaryAction)
        {
            var primary = FullscreenUi.Button(details.PrimaryAction!.Label, () => Context.Play(details.Tile));
            primary.FontSize = 36;
            primary.MinHeight = 76;
            actions.Children.Add(primary);
        }
        if (details.Tile.Entries.Count > 1)
            actions.Children.Add(FullscreenUi.Button("Choose version", () => Context.ChooseVersion(details.Tile)));
        actions.Children.Add(FullscreenUi.Button("More", ShowMore));
        var title = FullscreenUi.Text(details.Title, details.Title.Length > 45 ? 72 : 96);
        title.Bind(TextBlock.TextProperty, new Binding(nameof(GameDetailsViewModel.Title)) { Source = details });
        title.MaxLines = 2;
        title.MaxWidth = 1050;
        title.HorizontalAlignment = HorizontalAlignment.Left;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.Classes.Add("tv-title");
        var identity = string.Join(" · ", new[] { details.HasInstallState ? details.InstallText : null, details.StoreNames }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        var hero = new Grid { MinHeight = 330 };
        var heroText = FullscreenUi.Stack(title, FullscreenUi.Text(identity, 28, "TextDim"), actions);
        heroText.MaxWidth = 1050;
        heroText.HorizontalAlignment = HorizontalAlignment.Left;
        hero.Children.Add(heroText);
        _tabs = new[] { "Overview", "Updates", "Journal", "Library" }.Select((label, index) =>
            FullscreenUi.Button(label, () => SelectTab(index))).ToArray();
        var tabRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24, Margin = new Thickness(0, 12) };
        tabRow.Children.Add(FullscreenGlyphs.Icon("LT"));
        foreach (var tab in _tabs) tabRow.Children.Add(tab);
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
            if (_tab == 2) SelectTab(2, false);
        };
        SelectTab(Math.Clamp(selectedSection, 0, 3), false);
    }

    public override string Title => _details.Title;
    public override Control? Backdrop => _backdrop;
    public override string Hints => "A Select   B Back   Y More";
    public override string RightHints => "LT / RT Section";
    public int SelectedSection => _tab;

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
        _body.Content = index == 0 ? content : FullscreenUi.Scroll(content);
        SetFocusRows(_rows.ToArray());
        Changed();
        if (focus) FocusControl(_tabs[index]);
    }

    private Control Overview()
    {
        var lead = FullscreenUi.Text(_details.HasUnreadUpdates ? _details.UpdatesShortcutText : _details.BucketLabel, 48);
        lead.MaxLines = 2;
        lead.TextTrimming = TextTrimming.CharacterEllipsis;
        var historyText = FullscreenUi.Text(_details.PlaytimeText == "—" ? _details.OverviewHistoryText
            : $"{_details.PlaytimeText} played · {_details.OverviewHistoryText}", 28, "TextDim");
        historyText.MaxLines = 2;
        historyText.TextTrimming = TextTrimming.CharacterEllipsis;
        var history = FullscreenUi.Stack(FullscreenUi.Text(_details.HasUnreadUpdates ? "WHY RETURN?" : "YOUR HISTORY", 24, "TextDim"), lead, historyText);
        var historyButton = FullscreenUi.Button("Play history", () => Context.Push(new FullscreenDetailsHistoryPage(Context, _details.Tracker)));
        var aboutButton = FullscreenUi.Button("About game", ShowAbout);
        history.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { historyButton, aboutButton } });
        var overviewControls = new List<Control> { historyButton, aboutButton };
        var summary = FullscreenUi.Text(_details.SummaryText ?? _details.EmptyBodyText, 28);
        summary.MaxLines = 2;
        summary.TextTrimming = TextTrimming.CharacterEllipsis;
        var about = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        about.Children.Add(FullscreenUi.Text("ABOUT", 24, "TextDim"));
        summary.Margin = new Thickness(0, 12, 0, 16);
        Grid.SetRow(summary, 1);
        about.Children.Add(summary);
        if (_details.Screenshots is { HasShots: true } shots)
        {
            var row = new Grid { Name = "FullscreenOverviewScreenshots", ColumnDefinitions = new ColumnDefinitions("*,*"), MaxHeight = 250 };
            var controls = new List<Control>();
            foreach (var shot in shots.Shots.Take(2))
            {
                var button = FullscreenUi.Button(shot.AutomationName, () =>
                {
                    shots.SelectCommand.Execute(shot);
                    if (shots.Lightbox is { } lightbox) Context.Push(new FullscreenDetailsScreenshotPage(Context, lightbox));
                });
                var image = new Image { Stretch = Stretch.Uniform };
                image.Bind(Image.SourceProperty, new Binding(nameof(GameScreenshotViewModel.Image)) { Source = shot });
                button.Content = image;
                button.Padding = new Thickness(0, 0, 0, 8);
                button.Margin = new Thickness(controls.Count == 0 ? 0 : 16, 0, 0, 0);
                button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                button.VerticalContentAlignment = VerticalAlignment.Stretch;
                AutomationProperties.SetName(button, shot.AutomationName);
                Grid.SetColumn(button, controls.Count);
                row.Children.Add(button);
                controls.Add(button);
            }
            Grid.SetRow(row, 2);
            about.Children.Add(row);
            overviewControls.AddRange(controls);
        }
        // These controls occupy adjacent columns, so their focus neighbors run horizontally.
        _rows.Add(overviewControls.ToArray());
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("4*,5*"), Name = "FullscreenDetailsOverview" };
        history.Margin = new Thickness(0, 0, 48, 0);
        columns.Children.Add(history);
        var aboutRegion = new Border { Child = about, BorderThickness = new Thickness(1, 0, 0, 0),
            Padding = new Thickness(48, 0, 0, 0) };
        aboutRegion[!Border.BorderBrushProperty] = new DynamicResourceExtension("Line");
        Grid.SetColumn(aboutRegion, 1);
        columns.Children.Add(aboutRegion);
        return columns;
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
            content.Children.Add(Action($"{entry.DateText}  {entry.RatingText}\n{entry.Note}", () =>
                Context.Push(new FullscreenDetailsJournalPage(Context, entry))));
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
                    content.Children.Add(FullscreenUi.Text($"Achievements: {achievements.CountText} · {achievements.PercentText}", 24, "TextDim"));
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
                var button = Action($"{(list.IsMember ? "✓ " : "")}{list.Name}", async () =>
                {
                    list.IsMember = !list.IsMember;
                    await list.Pending;
                    SelectTab(3);
                });
                content.Children.Add(button);
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
        foreach (var link in _details.Links) actions.Add(new(link.Label, () => Context.OpenLink(link)));
        if (_details.ManagementAction is { } management) actions.Add(new(management.Label, () => Context.OpenLink(management)));
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
