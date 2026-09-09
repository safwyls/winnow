using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Winnow.App.Services;
using Winnow.App.ViewModels;

namespace Winnow.App.Views.Fullscreen;

public sealed class FullscreenDetailsMatchPage : FullscreenPage
{
    private readonly GameIgdbMatchViewModel _match;
    private readonly TextBox _query;
    private readonly StackPanel _results = new() { Spacing = 16 };
    private readonly Control[] _search;

    public FullscreenDetailsMatchPage(FullscreenContext context, GameIgdbMatchViewModel match) : base(context)
    {
        _match = match;
        _query = new TextBox { FontSize = 28, Watermark = match.FieldWatermark };
        _query.Bind(TextBox.TextProperty, new Binding(nameof(GameIgdbMatchViewModel.Query)) { Source = match, Mode = BindingMode.TwoWay });
        AutomationProperties.SetName(_query, match.FieldLabel);
        var edit = FullscreenUi.Button("Edit search", () => Context.EditText(_query));
        var search = FullscreenUi.Button(match.SearchLabel, async () =>
        {
            await match.SearchCommand.ExecuteAsync(null);
            Results();
        });
        var clear = FullscreenUi.Button(match.ClearLabel, () => Context.ShowActions("Clear the assigned game?",
            [new("Cancel", () => { }), new(match.ClearLabel, async () =>
            {
                await match.ClearCommand.ExecuteAsync(null);
                Context.Notify(match.Problem ?? match.Note ?? "Assignment cleared.");
                Results();
            })]));
        _search = [edit, search, clear];
        Content = FullscreenUi.Scroll(FullscreenUi.Stack(FullscreenUi.Text(match.SectionHeading, 32), _query,
            edit, search, clear, _results));
        Results();
    }

    public override string Title => "Wrong game?";
    public override string Hints => "A Select   Y Keyboard   B Back";
    public override bool Handle(GamepadButtons buttons)
    {
        if (buttons.HasFlag(GamepadButtons.Keyboard)) { Context.EditText(_query); return true; }
        return base.Handle(buttons);
    }

    private void Results()
    {
        _results.Children.Clear();
        var rows = _search.Select(control => new[] { control }).ToList();
        if (_match.Problem is { } problem) _results.Children.Add(FullscreenUi.Text(problem, 28, "Amber"));
        if (_match.ShowNoMatches) _results.Children.Add(FullscreenUi.Text(_match.NoMatchesText));
        if (_match.ShowIdMiss) _results.Children.Add(FullscreenUi.Text(_match.IdMissText));
        if (_match.Claim is { } claim)
        {
            _results.Children.Add(FullscreenUi.Text(claim.Headline));
            var link = FullscreenUi.Button(_match.ClaimLinkLabel, () => Context.ShowActions(claim.Headline,
                [new("Cancel", () => { }), new(_match.ClaimLinkLabel, async () =>
                {
                    await _match.LinkClaimCommand.ExecuteAsync(null);
                    Context.Notify(_match.Problem ?? _match.Note ?? "Games linked.");
                    Results();
                })]));
            var decline = FullscreenUi.Button(_match.ClaimDeclineLabel, () =>
            {
                _match.DeclineClaimCommand.Execute(null);
                Results();
            });
            _results.Children.Add(link);
            _results.Children.Add(decline);
            rows.Add([link]);
            rows.Add([decline]);
        }
        else
        foreach (var candidate in _match.Candidates)
        {
            var choose = FullscreenUi.Button($"{candidate.Name} · {candidate.YearText}{candidate.PlatformsText}", () =>
                Context.ShowActions($"Use {candidate.Name}?", [new("Cancel", () => { }), new(candidate.AssignLabel, async () =>
                {
                    await _match.AssignCommand.ExecuteAsync(candidate);
                    if (_match.Claim is null) Context.Notify(_match.Problem ?? _match.Note ?? "Game assigned.");
                    Results();
                })]));
            _results.Children.Add(choose);
            rows.Add([choose]);
        }
        SetFocusRows(rows.ToArray());
        Changed();
    }
}
