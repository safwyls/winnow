namespace Winnow.App.ViewModels;

public sealed record AccountChartItem(string Label, decimal Value, string ValueText, string Ink);
public sealed record AccountCurrencySummary(string Currency, string Net, string Gross, string Refunded);
