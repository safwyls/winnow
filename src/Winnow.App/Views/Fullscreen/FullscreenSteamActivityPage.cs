using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Layout;
using Microsoft.Extensions.DependencyInjection;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Core.Repositories;

namespace Winnow.App.Views.Fullscreen;

public sealed class FullscreenSteamActivityPage : FullscreenPage
{
    private readonly SteamReportedActivityViewModel _model;
    private readonly bool _ownsModel;
    public Task PendingRefresh { get; private set; } = Task.CompletedTask;
    public override string Title => SteamReportedActivityViewModel.Heading;
    public override string Hints => "A  Read activity     LT / RT  Page     B  Back";

    public FullscreenSteamActivityPage(FullscreenContext context, SteamReportedActivityViewModel? model = null) : base(context)
    {
        _ownsModel = model is null;
        _model = model ?? new SteamReportedActivityViewModel(context.Services?.GetService<ISteamPlaytimeObservationRepository>(),
            Scope(), context.Services?.GetService<ISettingsRepository>());
        _model.Changed += ModelChanged;
        if (_ownsModel) context.Library.TilesChanged += LibraryChanged;
        AttachedToVisualTree += (_, _) => PendingRefresh = _model.RefreshAsync();
        Render();
    }

    private IReadOnlyDictionary<long, string> Scope() => Context.Library.AllTiles
        .SelectMany(tile => tile.OwnershipIds.Select(id => (id, tile.Title)))
        .GroupBy(pair => pair.id).ToDictionary(group => group.Key, group => group.First().Title);
    private void LibraryChanged(object? sender, EventArgs e)
    { _model.UpdateScope(Scope()); PendingRefresh = _model.RefreshAsync(); }
    private void ModelChanged(object? sender, EventArgs e)
    { var restore = PreserveFocus(); Render(); restore(); }

    private void Render()
    {
        var content = FullscreenInformation.Column();
        content.Children.Add(FullscreenInformation.Title(Title));
        content.Children.Add(FullscreenInformation.Text(SteamReportedActivityViewModel.Explanation));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20 };
        var refresh = FullscreenUi.Button("Refresh", () => PendingRefresh = _model.RefreshAsync());
        refresh.IsEnabled = !_model.IsLoading;
        actions.Children.Add(refresh);
        var controls = new List<Control> { refresh };
        if (_model.HasPrevious)
        { var previous = FullscreenUi.Button("Previous", () => _model.PreviousPageCommand.Execute(null)); actions.Children.Add(previous); controls.Add(previous); }
        if (_model.HasNext)
        { var next = FullscreenUi.Button("Next", () => _model.NextPageCommand.Execute(null)); actions.Children.Add(next); controls.Add(next); }
        content.Children.Add(actions);
        if (_model.Status.Length > 0)
        {
            var status = FullscreenInformation.Text(_model.Status);
            AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
            content.Children.Add(status);
        }
        List<Control[]> rows = [controls.ToArray()];
        foreach (var entry in _model.Entries)
        {
            var button = FullscreenUi.Button(entry.AutomationName, () => Context.Push(new FullscreenDetailsReadingPage(Context,
                entry.GameTitle, $"{entry.Duration}\n\n{entry.ObservationBounds}\n\n{entry.Uncertainty}\n\n{SteamReportedActivityViewModel.Explanation}")));
            var words = new StackPanel { Spacing = 8 };
            words.Children.Add(FullscreenInformation.Title(entry.GameTitle));
            words.Children.Add(FullscreenInformation.Text(entry.Duration));
            words.Children.Add(FullscreenInformation.Metadata(entry.ObservationBounds));
            words.Children.Add(FullscreenInformation.Text(entry.Uncertainty));
            button.Content = words;
            content.Children.Add(FullscreenInformation.Rule());
            content.Children.Add(button); rows.Add([button]);
        }
        content.Children.Add(FullscreenInformation.Metadata(_model.PageLabel));
        Content = FullscreenUi.Scroll(content);
        SetFocusRows(rows.ToArray());
    }

    public override bool Handle(GamepadButtons buttons)
    {
        if (buttons.HasFlag(GamepadButtons.PagePrevious)) { _model.PreviousPageCommand.Execute(null); FocusInitial(); return true; }
        if (buttons.HasFlag(GamepadButtons.PageNext)) { _model.NextPageCommand.Execute(null); FocusInitial(); return true; }
        return base.Handle(buttons);
    }
    public override void Dispose()
    {
        _model.Changed -= ModelChanged;
        if (_ownsModel) { Context.Library.TilesChanged -= LibraryChanged; _model.Dispose(); }
        base.Dispose();
    }
}
