using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
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
    private int _tab;

    public FullscreenDetailsPage(FullscreenContext context, GameDetailsViewModel details, int selectedSection = 0) : base(context)
    {
        _details = details;
        DataContext = details;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24 };
        if (details.HasPrimaryAction)
            actions.Children.Add(FullscreenUi.Button(details.PrimaryAction!.Label, () => Context.Play(details.Tile)));
        if (details.Tile.Entries.Count > 1)
            actions.Children.Add(FullscreenUi.Button("Choose version", () => Context.ChooseVersion(details.Tile)));
        actions.Children.Add(FullscreenUi.Button("More", ShowMore));
        var title = FullscreenUi.Text(details.Title, 64);
        title.Bind(TextBlock.TextProperty, new Binding(nameof(GameDetailsViewModel.Title)) { Source = details });
        title.MaxLines = 2;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.Classes.Add("tv-title");
        var identity = string.Join(" · ", new[] { details.HasInstallState ? details.InstallText : null, details.StoreNames }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        var hero = new Grid { ColumnDefinitions = new ColumnDefinitions("3*,2*"), MinHeight = 320 };
        hero.Children.Add(FullscreenUi.Stack(title, FullscreenUi.Text(identity, 28, "TextDim"), actions,
            FullscreenUi.Text($"{details.PlaytimeText} played · {details.OverviewHistoryText}", 24, "TextDim")));
        var artwork = new Image { Stretch = Stretch.UniformToFill };
        if (details.Screenshots is { Shots.Count: > 0 } heroShots)
            artwork.Bind(Image.SourceProperty, new Binding(nameof(GameScreenshotViewModel.Image)) { Source = heroShots.Shots[0] });
        else
            artwork.Bind(Image.SourceProperty, new Binding(nameof(GameDetailsViewModel.Cover)) { Source = details });
        var art = new Border { Child = artwork, Background = details.PlaceholderBrush, ClipToBounds = true,
            Margin = new Thickness(48, 0, 0, 0), Height = 320, VerticalAlignment = VerticalAlignment.Top, CornerRadius = new CornerRadius(6) };
        Grid.SetColumn(art, 1);
        hero.Children.Add(art);
        _tabs = new[] { "Overview", "Updates", "Journal", "Library" }.Select((label, index) =>
            FullscreenUi.Button(label, () => SelectTab(index))).ToArray();
        var tabRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24, Margin = new Thickness(0, 24) };
        foreach (var tab in _tabs) tabRow.Children.Add(tab);
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
    public override string Hints => "A Select   B Back   LB / RB Section   Y More";
    public int SelectedSection => _tab;

    public override bool Handle(GamepadButtons buttons)
    {
        if (buttons.HasFlag(GamepadButtons.Previous)) { SelectTab((_tab + 3) % 4); return true; }
        if (buttons.HasFlag(GamepadButtons.Next)) { SelectTab((_tab + 1) % 4); return true; }
        if (buttons.HasFlag(GamepadButtons.Keyboard)) { ShowMore(); return true; }
        return base.Handle(buttons);
    }

    private void SelectTab(int index, bool focus = true)
    {
        _tab = index;
        for (var i = 0; i < _tabs.Length; i++) _tabs[i].Classes.Set("current", i == index);
        while (_rows.Count > 2) _rows.RemoveAt(_rows.Count - 1);
        _body.Content = FullscreenUi.Scroll(index switch
        {
            1 => Updates(),
            2 => Journal(),
            3 => Library(),
            _ => Overview(),
        });
        SetFocusRows(_rows.ToArray());
        Changed();
        if (focus) FocusControl(_tabs[index]);
    }

    private Control Overview()
    {
        var history = FullscreenUi.Stack(FullscreenUi.Text("YOUR HISTORY", 24, "TextDim"),
            FullscreenUi.Text(_details.HasUnreadUpdates ? _details.UpdatesShortcutText : _details.BucketLabel, 32),
            FullscreenUi.Text(_details.OverviewHistoryText));
        history.Children.Add(Action("View play history", () => Context.Push(new FullscreenDetailsHistoryPage(Context, _details.Tracker))));
        if (_details.HasUnreadUpdates) history.Children.Add(Action("Read updates", () => SelectTab(1)));
        var about = FullscreenUi.Stack(FullscreenUi.Text("ABOUT", 24, "TextDim"),
            FullscreenUi.Text(_details.SummaryText ?? _details.EmptyBodyText));
        if (_details.HasIdentityLine)
            about.Children.Insert(1, FullscreenUi.Text($"{_details.IdentityYearText}{_details.Publisher}", 24, "TextDim"));
        if (_details.CanExpandSummary)
            about.Children.Add(Action("Read description", () => Context.Push(new FullscreenDetailsReadingPage(Context, _details.Title, _details.Summary!))));
        if (_details.Reception is { } reception)
            foreach (var figure in reception.Figures)
                about.Children.Add(FullscreenUi.Text($"{figure.Source}: {figure.Value} · {figure.Count}", 24, "TextDim"));
        if (_details.Screenshots is { HasShots: true } shots)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24 };
            var controls = new List<Control>();
            foreach (var shot in shots.Shots.Take(2))
            {
                var button = FullscreenUi.Button(shot.AutomationName, () =>
                {
                    shots.SelectCommand.Execute(shot);
                    if (shots.Lightbox is { } lightbox) Context.Push(new FullscreenDetailsScreenshotPage(Context, lightbox));
                });
                var image = new Image { Width = 320, Height = 180, Stretch = Stretch.UniformToFill };
                image.Bind(Image.SourceProperty, new Binding(nameof(GameScreenshotViewModel.Image)) { Source = shot });
                button.Content = image;
                AutomationProperties.SetName(button, shot.AutomationName);
                row.Children.Add(button);
                controls.Add(button);
            }
            about.Children.Add(row);
            _rows.Add(controls.ToArray());
        }
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        history.Margin = new Thickness(0, 0, 48, 0);
        columns.Children.Add(history);
        Grid.SetColumn(about, 1);
        columns.Children.Add(about);
        return columns;
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
