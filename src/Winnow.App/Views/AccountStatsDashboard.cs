using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Winnow.App.ViewModels;

namespace Winnow.App.Views;

/// <summary>Shared, labelled charts; geometry never carries information without adjacent text.</summary>
public sealed class AccountStatsDashboard : UserControl
{
    public bool Fullscreen { get; init; }
    public bool ShowCurrencyPicker { get; init; } = true;
    private AccountStatsViewModel? _model;
    private bool _wide;
    private double Scale => Fullscreen ? 1.7 : 1;
    public AccountStatsDashboard()
    {
        SizeChanged += (_, _) =>
        {
            var wide = Bounds.Width >= (Fullscreen ? 1400 : 850);
            if (_wide != wide) { _wide = wide; RenderDashboard(); }
        };
        DataContextChanged += (_, _) => Connect();
        AttachedToVisualTree += (_, _) => Connect();
        DetachedFromVisualTree += (_, _) => { if (_model is not null) _model.PropertyChanged -= Changed; };
    }
    private void Connect()
    {
        if (_model is not null) _model.PropertyChanged -= Changed;
        _model = DataContext as AccountStatsViewModel;
        if (_model is not null) _model.PropertyChanged += Changed;
        RenderDashboard();
    }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AccountStatsViewModel.DashboardVersion)) RenderDashboard();
    }
    private StatsChartVisuals Visuals => new(Fullscreen);
    private TextBlock Text(string text, double size = 13, string ink = "TextDim", bool data = false) => Visuals.Text(text, size, ink, data);
    private TextBlock Heading(string text) => Visuals.Heading(text);
    private StackPanel Stack(params Control[] children) => Visuals.Stack(children);
    private Border Card(Control content) => Visuals.Card(content);
    private Control Bars(IReadOnlyList<AccountChartItem> items) => Visuals.Bars(items);
    private void RenderDashboard()
    {
        if (_model is not { } model) { Content = null; return; }
        var restorePickerFocus = IsKeyboardFocusWithin;
        ComboBox? currencyPicker = null;
        var body = Stack();
        var summary = Stack(Heading(model.SummaryHeading));
        foreach (var currency in model.CurrencySummaries)
        {
            var net = Stack(Text(currency.Currency + " · net spend", 12), Text(currency.Net, 28, "Text", true));
            var other = Stack(Text("Gross  " + currency.Gross, 12, data: true), Text("Refunded  " + currency.Refunded, 12, data: true));
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 20 * Scale };
            row.Children.Add(net); Grid.SetColumn(other, 1); row.Children.Add(other); summary.Children.Add(row);
        }
        if (model.CurrencySummaries.Count == 0) summary.Children.Add(Text(model.NetSpendValue, 28, "Text", true));
        var ratios = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 20 * Scale };
        ratios.Children.Add(Stack(Text(model.RefundedShareLabel), Text(model.RefundedShare, 24, "Text", true)));
        var bundle = Stack(Text(model.BundleShareLabel), Text(model.BundleShare, 24, "Text", true));
        Grid.SetColumn(bundle, 1); ratios.Children.Add(bundle); summary.Children.Add(ratios);
        summary.Children.Add(Text(model.SummaryNote, 11));
        body.Children.Add(Card(summary));
        if (ShowCurrencyPicker && model.CurrencyOptions.Count > 1)
        {
            var picker = new ComboBox { ItemsSource = model.CurrencyOptions, SelectedItem = model.SelectedCurrency, MinWidth = 160 };
            currencyPicker = picker;
            AutomationProperties.SetName(picker, "Chart and detail currency");
            picker.SelectionChanged += (_, _) => { if (picker.SelectedItem is string symbol) model.SelectedCurrency = symbol; };
            body.Children.Add(Stack(Text("Chart and detail currency"), picker));
        }
        var years = Card(Stack(Heading("Spending over time"), Text($"{model.SelectedCurrency ?? "No currency"} · net spend by year · bars share a zero baseline"),
            Bars(model.YearChart), Text(model.SpendInsight, 13, "Text"), Text(model.AverageSpendInsight, 13, "Text")));
        var kinds = Card(Stack(Heading("Where the money went"), Text($"{model.SelectedCurrency ?? "No currency"} · kept product transactions; wallet credit excluded"), Composition(model.KindChart, model.KindChartNote)));
        var licences = Card(Stack(Heading("How your licences arrived"), Text("Captured packages by acquisition method, not individual games."), Bars(model.LicenceChart)));
        if (_wide)
        {
            var charts = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 16 * Scale };
            years.VerticalAlignment = VerticalAlignment.Top;
            charts.Children.Add(years);
            var right = Stack(kinds, licences); Grid.SetColumn(right, 1); charts.Children.Add(right);
            body.Children.Add(charts);
        }
        else { body.Children.Add(years); body.Children.Add(kinds); body.Children.Add(licences); }
        Content = body;
        if (restorePickerFocus && currencyPicker is { } target) Avalonia.Threading.Dispatcher.UIThread.Post(() => target.Focus());
    }
    private Control Composition(IReadOnlyList<AccountChartItem> items, string emptyNote)
    {
        if (items.Count == 0) return Text(emptyNote);
        var total = items.Sum(x => x.Value);
        var chart = new Canvas { Width = 160 * Scale, Height = 160 * Scale };
        var angle = -Math.PI / 2;
        foreach (var item in items)
        {
            var sweep = (double)(item.Value / total) * Math.PI * 2;
            var geometry = new StreamGeometry();
            using (var pen = geometry.Open())
            {
                Point At(double radius, double theta) => new((80 + radius * Math.Cos(theta)) * Scale, (80 + radius * Math.Sin(theta)) * Scale);
                pen.BeginFigure(At(76, angle), true);
                var steps = Math.Max(2, (int)Math.Ceiling(sweep * 40));
                for (var i = 1; i <= steps; i++) pen.LineTo(At(76, angle + sweep * i / steps));
                for (var i = steps; i >= 0; i--) pen.LineTo(At(52, angle + sweep * i / steps));
                pen.EndFigure(true);
            }
            var path = new Avalonia.Controls.Shapes.Path { Data = geometry, StrokeThickness = 2 };
            path[!Avalonia.Controls.Shapes.Shape.FillProperty] = new DynamicResourceExtension(item.Ink);
            path[!Avalonia.Controls.Shapes.Shape.StrokeProperty] = new DynamicResourceExtension("Surface"); chart.Children.Add(path);
            angle += sweep;
        }
        var legend = Stack();
        foreach (var item in items)
        {
            var pct = (100 * item.Value / total).ToString("0.#", CultureInfo.CurrentCulture);
            legend.Children.Add(Stack(Text(item.Label, 13, item.Ink), Text($"{item.ValueText} · {pct}%", 14, "Text", true)));
        }
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 24 * Scale };
        grid.Children.Add(chart); Grid.SetColumn(legend, 1); grid.Children.Add(legend);
        return grid;
    }
}
