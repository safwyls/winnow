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
        Assert.Equal(mixed ? "Not available" : "$30.00", model.NetSpendValue);
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

    private sealed class StatsRepository : IAccountStatsRepository
    {
        public AccountStats Value { get; set; } = AccountStats.Empty("steam");
        public Task<AccountStats> GetAsync(string source, CancellationToken ct = default) => Task.FromResult(Value);
    }
}
