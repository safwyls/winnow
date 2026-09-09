using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Winnow.App.ViewModels;

namespace Winnow.App.Views.Fullscreen;

/// <summary>TV-owned edit state over the same validated library operations as desktop.</summary>
public sealed class FullscreenLibraryToolsPage : FullscreenPage
{
    private readonly LibrarySettingsViewModel _model;
    public override string Title => "Library tools";
    public FullscreenLibraryToolsPage(FullscreenContext context) : base(context)
    {
        _model = context.Services is { } services ? ActivatorUtilities.CreateInstance<LibrarySettingsViewModel>(services, new FullscreenExecutablePicker(context), new FullscreenAcquisitionDestination(context)) : new();
        _model.ReloadLibrary = async () => { await context.Shared.Library.LoadCommand.ExecuteAsync(null); await context.RefreshAsync(); };
        Render();
        AttachedToVisualTree += async (_, _) => { try { await _model.RefreshAsync(); Render(); } catch (Exception) { context.Notify("Couldn't load library tools. Try again."); } };
    }

    private void Render()
    {
        var body = FullscreenUi.Stack(FullscreenUi.Text("Library tools", 64));
        var focus = new List<Control[]>();
        void Add(string label, Action action) { var b = FullscreenUi.Button(label, action); body.Children.Add(b); focus.Add([b]); }
        Add("Add game", () => { _model.BeginAddCommand.Execute(null); Context.Push(new FullscreenManualGamePage(Context, _model)); });
        if (_model.CanExportAcquisitions) Add("Export acquisition CSV", async () =>
        {
            await _model.ExportAcquisitionsCommand.ExecuteAsync(null);
            Context.Notify(_model.AcquisitionExportStatus);
        });
        Add("Possible identity matches", () => Context.Push(new FullscreenIdentityPage(Context)));
        body.Children.Add(FullscreenUi.Text("Hidden games", 32));
        if (_model.HiddenGames.Count == 0) body.Children.Add(FullscreenUi.Text(_model.HiddenEmptyMessage, 28, "TextDim"));
        foreach (var row in _model.HiddenGames)
            Add($"{row.Title}     Unhide", async () => { try { await _model.UnhideCommand.ExecuteAsync(row); Render(); FocusInitial(); } catch (Exception) { Context.Notify("Couldn't unhide this game. Try again."); } });
        body.Children.Add(FullscreenUi.Text("Games you added", 32));
        if (_model.ManualEntries.Count == 0) body.Children.Add(FullscreenUi.Text(_model.ManualEmptyMessage, 28, "TextDim"));
        foreach (var row in _model.ManualEntries)
            Add(row.Title, () => Context.ShowActions(row.Title, [new("Edit game", async () => { await _model.BeginEditCommand.ExecuteAsync(row); Context.Push(new FullscreenManualGamePage(Context, _model)); }),
                new("Remove entry", () => { _model.BeginDeleteCommand.Execute(row); Context.ShowActions(_model.DeleteConfirmMessage, [new("Remove entry", async () => { await _model.ConfirmDeleteCommand.ExecuteAsync(null); Render(); FocusInitial(); }), new("Cancel", () => _model.CancelDeleteCommand.Execute(null))]); })]));
        Content = FullscreenUi.Scroll(body); SetFocusRows(focus.ToArray());
    }
}

public sealed class FullscreenManualGamePage : FullscreenPage
{
    private readonly LibrarySettingsViewModel _model;
    public override string Title => _model.FormTitle;
    public FullscreenManualGamePage(FullscreenContext context, LibrarySettingsViewModel model) : base(context)
    {
        _model = model;
        var body = FullscreenUi.Stack(FullscreenUi.Text(model.FormTitle, 64));
        var focus = new List<Control[]>();
        var refreshers = new List<Action>();
        TextBox Field(string label, Func<string> read, Action<string> update)
        {
            var field = new TextBox { Text = read(), FontSize = 28, MinHeight = 64 };
            field.TextChanged += (_, _) => update(field.Text ?? "");
            refreshers.Add(() => field.Text = read());
            var edit = FullscreenUi.Button($"Edit {label}", () => context.EditText(field));
            body.Children.Add(FullscreenUi.Text(label, 28, "TextDim")); body.Children.Add(field); body.Children.Add(edit); focus.Add([edit]);
            return field;
        }
        Field(model.TitleFieldLabel, () => model.DraftTitle, v => model.DraftTitle = v);
        Field(model.YearFieldLabel, () => model.DraftYear, v => model.DraftYear = v);
        Field(model.PlatformFieldLabel, () => model.DraftPlatform, v => model.DraftPlatform = v);
        Field(model.ExecutableFieldLabel, () => model.DraftExecutable, v => model.DraftExecutable = v);
        var browse = FullscreenUi.Button("Choose executable", async () =>
        {
            await model.BrowseForExecutableCommand.ExecuteAsync(null);
            foreach (var refresh in refreshers) refresh();
            if (model.HasCandidates) Candidates();
        });
        body.Children.Add(browse); focus.Add([browse]);
        Field(model.IgdbFieldLabel, () => model.DraftIgdbId, v => model.DraftIgdbId = v);
        Field(model.SteamAppIdFieldLabel, () => model.DraftSteamAppId, v => model.DraftSteamAppId = v);
        void Candidates()
        {
            context.ShowActions("Choose the matching game", model.Candidates.Select(candidate => new FullscreenAction($"{candidate.Name} · {candidate.FirstReleaseYear}", () =>
            { model.UseCandidateCommand.Execute(candidate); foreach (var refresh in refreshers) refresh(); })).Append(new("Keep my entry", () => model.DismissMatchCommand.Execute(null))).ToArray());
        }
        if (model.ShowMatchBlock)
        {
            var search = FullscreenUi.Button("Find game metadata", async () =>
            {
                if (!model.SearchIgdbCommand.CanExecute(null)) { context.Notify("Enter a title first."); return; }
                await model.SearchIgdbCommand.ExecuteAsync(null);
                if (model.HasCandidates) Candidates(); else context.Notify(model.MatchProblem ?? model.MatchNoMatchesText);
            });
            body.Children.Add(search); focus.Add([search]);
        }
        var status = FullscreenUi.Text("", 28, "Amber"); body.Children.Add(status);
        var save = FullscreenUi.Button("Save", async () =>
        {
            try
            {
                await model.SaveFormCommand.ExecuteAsync(null);
                if (!model.IsFormOpen) context.Back();
                else status.Text = string.Join("\n", new[] { model.TitleError, model.YearError, model.IgdbIdError, model.SteamAppIdError, model.Problem }.Where(x => !string.IsNullOrWhiteSpace(x)));
            }
            catch (Exception) { status.Text = "Couldn't save this game. Try again."; }
        });
        var cancel = FullscreenUi.Button("Cancel", () => { model.CancelFormCommand.Execute(null); context.Back(); });
        body.Children.Add(save); body.Children.Add(cancel); focus.Add([save, cancel]);
        Content = FullscreenUi.Scroll(body); SetFocusRows(focus.ToArray());
    }
}

public sealed class FullscreenIdentityPage : FullscreenPage
{
    private readonly MergeQueueViewModel? _model;
    private bool _disposed;
    private string _status = "Reading possible matches…";
    public override string Title => "Possible identity matches";
    public FullscreenIdentityPage(FullscreenContext context) : base(context)
    {
        _model = context.Services is { } services ? ActivatorUtilities.CreateInstance<MergeQueueViewModel>(services) : null;
        Render();
        AttachedToVisualTree += async (_, _) =>
        {
            try { if (_model is not null) await _model.EnsureLoadedAsync(); _status = "No possible matches to review."; Render(); }
            catch (Exception) { _status = "Couldn't read possible matches. Reopen this page to try again."; Render(); }
        };
    }

    private void Render()
    {
        if (_disposed) return;
        var body = FullscreenUi.Stack(FullscreenUi.Text(Title, 64));
        var focus = new List<Control[]>();
        var count = 0;
        if (_model is not null)
        {
            void Add(string label, Action action) { var button = FullscreenUi.Button(label, action); body.Children.Add(button); focus.Add([button]); }
            Add("Sort · " + _model.SortOptions.First(option => option.IsSelected).Label, () => Context.ShowActions("Sort possible matches", _model.SortOptions.Select(option => new FullscreenAction(option.Label, () => { _model.SelectSortCommand.Execute(option); Render(); FocusInitial(); })).ToArray()));
            Add("Kind · " + _model.KindOptions.First(option => option.IsSelected).Label, () => Context.ShowActions("Match kind", _model.KindOptions.Select(option => new FullscreenAction(option.Label, () => { _model.SelectKindCommand.Execute(option); Render(); FocusInitial(); })).ToArray()));
            if (_model.CanAcceptExact) Add(_model.AcceptExactLabel, () => Confirm(_model.AcceptExactTooltip, () => _model.AcceptExactCommand.ExecuteAsync(null)));
            if (_model.CanMergeSelected) Add(_model.MergeSelectedLabel, () => Confirm(_model.MergeSelectedTooltip, () => _model.MergeSelectedCommand.ExecuteAsync(null)));
            foreach (var card in _model.Sections.Where(section => section.IsVisible).SelectMany(s => s.Cards))
            {
                count++;
                var button = FullscreenUi.Button($"{(card.IsSelected ? "Selected · " : "")}{card.HeaderTitle}     {card.ConfidenceLabel}\n{card.EntryCountText} entries · {card.TotalPlaytimeText}", () => Open(card));
                body.Children.Add(button); focus.Add([button]);
            }
        }
        if (count == 0) body.Children.Add(FullscreenUi.Text(_status));
        var back = FullscreenUi.Button("Back", Context.Back); body.Children.Add(back); focus.Add([back]);
        Content = FullscreenUi.Scroll(body); SetFocusRows(focus.ToArray());
    }

    private void Confirm(string description, Func<Task> action) => Context.ShowActions(description,
        [new("Continue", async () => await Apply(action)), new("Cancel", () => { })]);

    private async Task Apply(Func<Task> action)
    {
        try { await action(); await Context.RefreshAsync(); Render(); FocusInitial(); }
        catch (Exception) { Context.Notify("Couldn't update these matches. Try again."); }
    }

    private void Open(MergeCardViewModel card)
    {
        if (_model is null) return;
        var actions = new List<FullscreenAction>();
        foreach (var row in card.Rows)
        {
            var tile = row.ReleaseIds.Select(Context.Library.TileForRelease).FirstOrDefault(t => t is not null);
            actions.Add(new($"{row.Title} · {(row.IsHeader ? "Header" : row.IsIncluded ? "Included" : "Left out")}", () =>
            {
                var choices = new List<FullscreenAction>();
                if (tile is not null) choices.Add(new("Open game", () => Context.OpenGame(tile)));
                if (!card.IsResolved && row.CanPromote && !row.IsHeader) choices.Add(new("Make header", () => { card.Promote(row); Open(card); }));
                if (!card.IsResolved && row.CanExclude) choices.Add(new(row.IsIncluded ? "Leave out" : "Include", () => { row.IsIncluded = !row.IsIncluded; Open(card); }));
                choices.Add(new("Back to proposal", () => Open(card)));
                Context.ShowActions($"{row.Title}\n{row.StoreNames} · {row.PlaytimeText} · {row.IdleText}", choices);
            }));
        }
        if (card.IsResolved) actions.Add(new("Separate again", async () => await Apply(() => _model.SeparateCommand.ExecuteAsync(card))));
        else
        {
            actions.Add(new(card.IsSelected ? "Remove from selection" : "Select for grouping", () => { card.IsSelected = !card.IsSelected; Render(); FocusInitial(); }, card.CanAnswer));
            actions.Add(new("Same game", () => Confirm($"Group these entries under {card.HeaderTitle}? Nothing is deleted.", () => _model.SameGameCommand.ExecuteAsync(card)), card.CanAnswer));
            actions.Add(new("Different games", async () => await Apply(() => _model.DifferentGamesCommand.ExecuteAsync(card)), card.CanAnswer));
        }
        Context.ShowActions(card.Reason, actions);
    }

    public override void Dispose() { _disposed = true; _model?.Dispose(); base.Dispose(); }
}
