using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Layout;
using Winnow.App.ViewModels;

namespace Winnow.App.Views;

public sealed class GameplayStatsDashboard : UserControl
{
    public bool Fullscreen { get; init; }
    private GameplayStatsViewModel? _model;
    private bool _wide;
    public GameplayStatsDashboard()
    {
        DataContextChanged += (_, _) => Connect();
        AttachedToVisualTree += (_, _) => Connect();
        DetachedFromVisualTree += (_, _) => { if (_model is not null) _model.PropertyChanged -= Changed; };
        SizeChanged += (_, _) =>
        {
            var wide = Bounds.Width >= (Fullscreen ? 1400 : 850);
            if (wide != _wide) { _wide = wide; RenderDashboard(); }
        };
    }
    private void Connect()
    {
        if (_model is not null) _model.PropertyChanged -= Changed;
        _model = DataContext as GameplayStatsViewModel;
        if (_model is not null) _model.PropertyChanged += Changed;
        RenderDashboard();
    }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(GameplayStatsViewModel.DashboardVersion)) RenderDashboard(); }
    private void RenderDashboard()
    {
        if (_model is not { HasData: true } model) { Content = null; return; }
        var v = new StatsChartVisuals(Fullscreen);
        var figures = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 20 };
        var labels = new[] { "Recorded hours", "Games played", "Median session" };
        var values = new[] { model.HoursText, model.GamesText, model.MedianText };
        for (var i = 0; i < labels.Length; i++)
        {
            var figure = v.Stack(v.Text(labels[i]), v.Text(values[i], 28, "Text", true));
            Grid.SetColumn(figure, i); figures.Children.Add(figure);
        }
        var body = v.Stack(v.Card(v.Stack(v.Heading("Recorded on this library"), v.Text(model.PeriodLabel), figures, v.Text(model.CoverageNote, 11))));
        var hours = v.Card(v.Stack(v.Heading("Recorded hours over time"), v.Text("Completed sessions split across local dates. Bars start at zero."), v.Bars(model.HoursChart)));
        var games = v.Card(v.Stack(v.Heading("Your top games"), v.Text("Up to ten games by recorded hours in this period. Linked store copies count as one game."),
            model.GamesChart.Count == 0 ? v.Text("No completed sessions were recorded for these games and dates.") : v.Bars(model.GamesChart)));
        var sessions = v.Card(v.Stack(v.Heading("Session lengths"), v.Text(model.SessionNote, 11), v.Bars(model.LengthChart)));
        var library = v.Card(v.Stack(v.Heading("Your library today"), v.Text(model.LibraryNote, 11), v.Bars(model.LibraryChart),
            v.Text("Store entries · games owned in more than one store appear under each store.", 11), v.Bars(model.StoresChart)));
        if (_wide)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 16 };
            var left = v.Stack(hours, sessions); var right = v.Stack(games, library);
            grid.Children.Add(left); Grid.SetColumn(right, 1); grid.Children.Add(right); body.Children.Add(grid);
        }
        else { body.Children.Add(hours); body.Children.Add(games); body.Children.Add(sessions); body.Children.Add(library); }
        Content = body;
    }
}
