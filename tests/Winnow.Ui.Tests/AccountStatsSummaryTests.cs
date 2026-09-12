using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Winnow.App.Services;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Winnow.App.Design;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class AccountStatsSummaryTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Both_surfaces_show_shared_count_percentages_and_preserve_currency_boundaries(bool fullscreen, bool mixed)
    {
        using var db = new TempDatabase();
        var facts = new AccountFactRepository(db.Factory);
        async Task Add(string name, long? cents, bool refunded = false, string kind = "purchase", string currency = "$")
            => await facts.TryAppendAsync(new AccountTransactionFact { Source = "steam", AccountRef = "10001", Kind = kind,
                TransactionTypeRaw = kind, ItemNames = name == "Bundle" ? [name, "Second game"] : [name],
                TotalCents = cents, Refunded = refunded,
                CurrencySymbol = currency, CapturedAt = DateTime.UtcNow });
        await Add("Bundle", 1000);
        await Add("Single", 2000, currency: mixed ? "€" : "$");
        await Add("Reversed purchase", 3000, true);
        await Add("Missing price", null);
        await Add("Wallet", 90000, kind: AccountTransactionKinds.WalletCreditPurchase);
        await Add("Standalone refund", 3000, kind: AccountTransactionKinds.Refund);
        var repository = new AccountStatsRepository(db.Factory);
        var model = new AccountStatsViewModel(repository);
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("33.3%", model.RefundedShare);
        Assert.Equal("50%", model.BundleShare);
        Assert.Equal(mixed ? "$10.00" : "$30.00", model.NetSpendValue);
        using var services = new ServiceCollection().AddSingleton<IAccountStatsRepository>(repository).BuildServiceProvider();
        using var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell, services);
        using var page = new FullscreenLibrarySummaryPage(context);
        var window = new Window { Width = fullscreen ? 1920 : 700, Height = 1080,
            Content = fullscreen ? page : new AccountStatsView { DataContext = model } };
        try
        {
            window.Show();
            if (fullscreen) await page.PendingRefresh;
            Dispatcher.UIThread.RunJobs();
            var visible = window.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).ToArray();
            var percentage = Assert.Single(visible, text => text.Text == "33.3%");
            Assert.Contains(visible, text => text.Text == "50%");
            Assert.Contains(visible, text => text.Text == model.NetSpendValue);
            Assert.Contains(visible, text => text.Text == model.SummaryNote);
            Assert.True(percentage.FontSize >= (fullscreen ? 40 : 22));
            Assert.True(percentage.Bounds.Width > 0);
            Assert.True(percentage.Bounds.Width <= Assert.IsAssignableFrom<Control>(percentage.Parent).Bounds.Width);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Missing_prices_zero_denominators_and_ambiguous_accounts_do_not_invent_figures()
    {
        var repository = new StatsRepository();
        var model = new AccountStatsViewModel(repository);
        foreach (var stats in new[]
        {
            AccountStats.Empty("steam"),
            new AccountStats { Source = "steam", LicenseCount = 1 },
            new AccountStats { Source = "steam", TransactionCount = 1, TransactionsWithoutCurrency = 1 },
            new AccountStats { Source = "steam", TransactionCount = 4, GrossProductTransactionCount = 4,
                RefundedProductTransactionCount = 1, BundlePurchases = new(1, 100), KnownAccountCount = 1, UnknownAccountFactCount = 1 },
        })
        {
            repository.Value = stats;
            await model.RefreshCommand.ExecuteAsync(null);
            Assert.Equal("Not available", model.NetSpendValue);
            Assert.Equal("Not available", model.RefundedShare);
            Assert.Equal("Not available", model.BundleShare);
        }
        repository.Value = new AccountStats { Source = "steam", TransactionCount = 2, GrossProductTransactionCount = 2,
            RefundedProductTransactionCount = 2, Currencies = [new("$", 2)] };
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("100%", model.RefundedShare);
        Assert.Equal("Not available", model.BundleShare);
    }

    [AvaloniaTheory]
    [InlineData(false, 1200)]
    [InlineData(false, 600)]
    [InlineData(true, 1920)]
    [InlineData(true, 1280)]
    public async Task Currency_charts_switch_keep_focus_and_render_without_horizontal_overflow(bool fullscreen, int width)
    {
        AccountStats Group(string symbol, long factor) => new()
        {
            Source = "steam", TransactionCount = 90, GrossProductTransactionCount = 80,
            GrossProductSpendCents = factor * 13000, RefundedProductTransactionCount = 5,
            RefundedProductSpendCents = factor * 1000, Purchases = new(60, factor * 9000),
            GiftPurchases = new(10, factor * 2000), InGamePurchases = new(5, factor * 1000),
            Currencies = [new(symbol, 90)], BundlePurchases = new(15, factor * 5000),
            SpendByYear = [new(2020, 10, factor * 1000), new(2021, 15, factor * 2000), new(2022, 10, factor * 1500),
                new(2023, 20, factor * 3500), new(2024, 10, factor * 2500), new(2025, 10, factor * 1500)]
        };
        var usd = Group("$", 10); var euro = Group("€", 2);
        var repository = new StatsRepository { Value = new AccountStats
        {
            Source = "steam", TransactionCount = 181, TransactionsWithoutCurrency = 1,
            GrossProductTransactionCount = 160, RefundedProductTransactionCount = 10,
            BundlePurchases = new(30, 0), Currencies = [new("$", 90), new("€", 90)], CurrencyGroups = [usd, euro],
            KnownAccountCount = 1, LicenseCount = 230,
            LicenseAcquisitions = [new("steam_store", 120), new("retail", 70), new("complimentary", 30), new("gift", 10)]
        }};
        var model = new AccountStatsViewModel(repository); await model.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(2, model.CurrencySummaries.Count);
        Assert.Equal(120000m, model.KindChart.Sum(x => x.Value));
        Assert.Contains("2023", model.SpendInsight);
        Assert.Contains("$16.00", model.AverageSpendInsight);
        using var services = new ServiceCollection().AddSingleton<IAccountStatsRepository>(repository).BuildServiceProvider();
        using var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell, services);
        using var page = new FullscreenLibrarySummaryPage(context);
        var content = fullscreen ? (Control)page : new AccountStatsView { DataContext = model };
        var window = new Window { Width = width, Height = fullscreen ? 1080 : 900, Content = content,
            Background = (IBrush)content.FindResource("Ground")! };
        try
        {
            window.Show(); if (fullscreen) await page.PendingRefresh;
            Dispatcher.UIThread.RunJobs();
            ApplyLargeText();
            Capture("overview");
            var dashboard = window.GetVisualDescendants().OfType<AccountStatsDashboard>().Single();
            var actual = (AccountStatsViewModel)dashboard.DataContext!;
            if (fullscreen)
            {
                page.FocusInitial(); page.Handle(GamepadButtons.Right); page.Handle(GamepadButtons.Accept);
            }
            else
            {
                var picker = dashboard.GetVisualDescendants().OfType<ComboBox>().Single();
                picker.Focus(); picker.SelectedItem = "€";
            }
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("€", actual.SelectedCurrency);
            Assert.Equal("€240.00", actual.NetSpendValue);
            Assert.Equal(24000m, actual.KindChart.Sum(x => x.Value));
            Assert.All(actual.YearChart, x => Assert.StartsWith("€", x.ValueText));
            if (!fullscreen) Assert.True(window.GetVisualDescendants().OfType<ComboBox>().Single().IsFocused);
            await actual.RefreshCommand.ExecuteAsync(null);
            Assert.Equal("€", actual.SelectedCurrency);
            Dispatcher.UIThread.RunJobs();
            ApplyLargeText();
            var scroll = window.GetVisualDescendants().OfType<ScrollViewer>().Where(x => x.IsEffectivelyVisible && x.Extent.Height > x.Viewport.Height).OrderByDescending(x => x.Viewport.Height).First();
            window.FocusManager?.ClearFocus();
            var yearHeading = window.GetVisualDescendants().OfType<TextBlock>().Single(x => x.Text == "Spending over time");
            scroll.Offset = new Vector(0, scroll.Offset.Y + yearHeading.TranslatePoint(default, scroll)!.Value.Y - 20);
            Dispatcher.UIThread.RunJobs();
            Capture("charts");
            var kindHeading = window.GetVisualDescendants().OfType<TextBlock>().First(x => x.Text == "Where the money went" && x.IsEffectivelyVisible);
            scroll.Offset = new Vector(0, scroll.Offset.Y + kindHeading.TranslatePoint(default, scroll)!.Value.Y - 20);
            Dispatcher.UIThread.RunJobs(); Capture("composition");
            Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1);
            void ApplyLargeText()
            {
                if (!fullscreen || width != 1280) return;
                // Match the shell's 140% text adjustment while testing this isolated page.
                foreach (var block in window.GetVisualDescendants().OfType<TextBlock>())
                    if (block.FontSize < 48) block.FontSize *= 1.4;
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            }
            void Capture(string section)
            {
                if (Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is not { } directory) return;
                Directory.CreateDirectory(directory);
                using var frame = window.CaptureRenderedFrame();
                frame?.Save(Path.Combine(directory, $"stats-{(fullscreen ? "fullscreen" : "desktop")}-{width}-{section}.png"));
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Overlapping_accounts_with_currency_groups_suppress_money_charts_but_keep_licence_counts()
    {
        var group = new AccountStats { Source = "steam", TransactionCount = 1, Currencies = [new("$", 1)],
            GrossProductTransactionCount = 1, GrossProductSpendCents = 100,
            Purchases = new(1, 100), SpendByYear = [new(2025, 1, 100)] };
        var model = new AccountStatsViewModel(new StatsRepository { Value = group with
        {
            CurrencyGroups = [group], KnownAccountCount = 1, UnknownAccountFactCount = 1,
            LicenseAcquisitions = [new("retail", 5)]
        }});
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("Not available", Assert.Single(model.CurrencySummaries).Net);
        Assert.Empty(model.YearChart); Assert.Empty(model.KindChart);
        Assert.Equal(5, Assert.Single(model.LicenceChart).Value);
    }

    [AvaloniaFact]
    public async Task Negative_amounts_stay_signed_and_zero_years_remain_recorded_facts()
    {
        var repository = new StatsRepository { Value = new AccountStats { Source = "steam", TransactionCount = 2,
            Currencies = [new("$", 2)], GrossProductTransactionCount = 2, GrossProductSpendCents = 8000,
            Purchases = new(1, 10000), InGamePurchases = new(1, -2000), SpendByYear = [new(2024, 1, -2000), new(2025, 1, 10000)] } };
        var model = new AccountStatsViewModel(repository); await model.RefreshCommand.ExecuteAsync(null);
        Assert.Empty(model.KindChart); Assert.Contains("negative", model.KindChartNote);
        Assert.Equal(-2000, model.YearChart[0].Value); Assert.Equal("$-20.00", model.YearChart[0].ValueText);
        repository.Value = repository.Value with { SpendByYear = [new(2025, 1, 0)] };
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Contains("2025", model.SpendInsight); Assert.Contains("$0.00", model.SpendInsight);
    }

    private sealed class StatsRepository : IAccountStatsRepository
    {
        public AccountStats Value { get; set; } = AccountStats.Empty("steam");
        public Task<AccountStats> GetAsync(string source, CancellationToken ct = default) => Task.FromResult(Value);
    }
}
