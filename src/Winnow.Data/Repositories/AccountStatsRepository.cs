using Dapper;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

/// <summary>
/// Computes <see cref="AccountStats"/> from the fact tables (migration 0014)
/// in one lease and several small named queries. Legibility is the whole reason
/// Dapper was chosen over EF Core (§3.1), and a single expression per figure
/// is more legible than one query doing everything.
///
/// <para>"Spend" means the three
/// <see cref="AccountTransactionKinds.ProductSpend"/> kinds: purchase, gift
/// purchase and in-game purchase. Wallet movements are deliberately outside it:
/// counting a wallet top-up and the product it later paid for would count the
/// same money twice.</para>
/// </summary>
public sealed class AccountStatsRepository : IAccountStatsRepository
{
    private readonly ISqliteConnectionFactory _factory;

    public AccountStatsRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<AccountStats> GetAsync(string source, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        var kinds = AccountTransactionKinds.ProductSpend;

        using var lease = _factory.Lease();
        var conn = lease.Connection;
        var tx = lease.Transaction;

        var shape = await conn.QuerySingleAsync<ShapeRow>(new CommandDefinition("""
            SELECT COUNT(*)                                                          AS TransactionCount,
                   COALESCE(SUM(CASE WHEN occurred_at IS NULL THEN 1 END), 0)        AS TransactionsWithoutDate,
                   COALESCE(SUM(CASE WHEN currency_symbol IS NULL THEN 1 END), 0)    AS TransactionsWithoutCurrency,
                   MIN(occurred_at)                                                  AS FirstTransactionAt,
                   MAX(occurred_at)                                                  AS LastTransactionAt
            FROM account_transactions
            WHERE source = @source;
            """, new { source }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

        // Gross is every product transaction the pages reported; refunded is the
        // subset Steam marked reversed. Net is the subtraction, never a stored
        // number.
        var spend = await conn.QuerySingleAsync<SpendRow>(new CommandDefinition("""
            SELECT COUNT(*)                                                                AS GrossCount,
                   COALESCE(SUM(total_cents), 0)                                           AS GrossCents,
                   COALESCE(SUM(CASE WHEN refunded = 1 THEN 1 END), 0)                     AS RefundedCount,
                   COALESCE(SUM(CASE WHEN refunded = 1 THEN total_cents END), 0)           AS RefundedCents,
                   COALESCE(SUM(CASE WHEN refunded = 0 AND occurred_at IS NULL THEN 1 END), 0)
                                                                                           AS UndatedCount,
                   COALESCE(SUM(CASE WHEN refunded = 0 AND occurred_at IS NULL THEN total_cents END), 0)
                                                                                           AS UndatedCents
            FROM account_transactions
            WHERE source = @source
              AND kind IN @kinds
              AND total_cents IS NOT NULL;
            """, new { source, kinds }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

        var byYear = await conn.QueryAsync<YearRow>(new CommandDefinition("""
            SELECT CAST(strftime('%Y', occurred_at) AS INTEGER) AS Year,
                   COUNT(*)                                     AS TransactionCount,
                   SUM(total_cents)                             AS Cents
            FROM account_transactions
            WHERE source = @source
              AND kind IN @kinds
              AND total_cents IS NOT NULL
              AND refunded = 0
              AND occurred_at IS NOT NULL
            GROUP BY CAST(strftime('%Y', occurred_at) AS INTEGER)
            ORDER BY CAST(strftime('%Y', occurred_at) AS INTEGER);
            """, new { source, kinds }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

        // A wallet-credit redemption carries no total — the page reports only the
        // balance change — so the value falls back to that change. Every other
        // kind is valued at its total.
        var slices = await conn.QueryAsync<SliceRow>(new CommandDefinition("""
            SELECT kind                                                       AS Kind,
                   COUNT(*)                                                   AS Count,
                   COALESCE(SUM(COALESCE(total_cents, wallet_change_cents)), 0) AS Cents
            FROM account_transactions
            WHERE source = @source
              AND refunded = 0
            GROUP BY kind;
            """, new { source }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

        var byKind = slices.ToDictionary(
            s => s.Kind, s => new AccountSpendSlice((int)s.Count, s.Cents), StringComparer.Ordinal);

        // A bundle's total is a real fact; its per-item split is not, and is
        // never computed. This is the total, exposed as its own fact (§4.7).
        var bundles = await conn.QuerySingleAsync<SliceTotalsRow>(new CommandDefinition("""
            SELECT COUNT(*)                      AS Count,
                   COALESCE(SUM(total_cents), 0) AS Cents
            FROM account_transactions
            WHERE source = @source
              AND kind IN @kinds
              AND total_cents IS NOT NULL
              AND refunded = 0
              AND item_count > 1;
            """, new { source, kinds }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

        var discounted = await conn.QuerySingleAsync<DiscountRow>(new CommandDefinition("""
            SELECT COUNT(*)                           AS Count,
                   COALESCE(SUM(total_cents), 0)      AS Cents,
                   COALESCE(SUM(list_price_cents), 0) AS ListCents
            FROM account_transactions
            WHERE source = @source
              AND kind IN @kinds
              AND total_cents IS NOT NULL
              AND list_price_cents IS NOT NULL
              AND refunded = 0;
            """, new { source, kinds }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

        var biggest = await conn.QuerySingleOrDefaultAsync<BiggestRow>(new CommandDefinition("""
            SELECT total_cents     AS Cents,
                   occurred_at     AS OccurredAt,
                   item_names_json AS ItemNamesJson,
                   item_count      AS ItemCount,
                   currency_symbol AS CurrencySymbol
            FROM account_transactions
            WHERE source = @source
              AND kind IN @kinds
              AND total_cents IS NOT NULL
              AND refunded = 0
            ORDER BY total_cents DESC, occurred_at DESC, id DESC
            LIMIT 1;
            """, new { source, kinds }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

        var currencies = await conn.QueryAsync<CurrencyRow>(new CommandDefinition("""
            SELECT currency_symbol AS Symbol,
                   COUNT(*)        AS TransactionCount
            FROM account_transactions
            WHERE source = @source
              AND currency_symbol IS NOT NULL
            GROUP BY currency_symbol
            ORDER BY COUNT(*) DESC, currency_symbol;
            """, new { source }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

        var licenses = await conn.QuerySingleAsync<LicenseShapeRow>(new CommandDefinition("""
            SELECT COUNT(*)                                                        AS LicenseCount,
                   COALESCE(SUM(CASE WHEN acquisition_kind IS NULL THEN 1 END), 0) AS UnrecognizedCount,
                   COALESCE(SUM(CASE WHEN acquired_at IS NULL THEN 1 END), 0)      AS UndatedCount,
                   MIN(acquired_at)                                                AS FirstLicenseAt,
                   MAX(acquired_at)                                                AS LastLicenseAt
            FROM account_licenses
            WHERE source = @source;
            """, new { source }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

        var acquisitions = await conn.QueryAsync<AcquisitionRow>(new CommandDefinition("""
            SELECT acquisition_kind AS Kind,
                   COUNT(*)         AS Count
            FROM account_licenses
            WHERE source = @source
            GROUP BY acquisition_kind
            ORDER BY COUNT(*) DESC, COALESCE(acquisition_kind, '');
            """, new { source }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

        return new AccountStats
        {
            Source = source,
            TransactionCount = (int)shape.TransactionCount,
            LicenseCount = (int)licenses.LicenseCount,
            GrossProductSpendCents = spend.GrossCents,
            GrossProductTransactionCount = (int)spend.GrossCount,
            RefundedProductSpendCents = spend.RefundedCents,
            RefundedProductTransactionCount = (int)spend.RefundedCount,
            SpendByYear = byYear
                .Select(y => new AccountSpendYear((int)y.Year, (int)y.TransactionCount, y.Cents))
                .ToList(),
            UndatedNetSpendCents = spend.UndatedCents,
            UndatedNetTransactionCount = (int)spend.UndatedCount,
            Purchases = Slice(byKind, AccountTransactionKinds.Purchase),
            GiftPurchases = Slice(byKind, AccountTransactionKinds.GiftPurchase),
            InGamePurchases = Slice(byKind, AccountTransactionKinds.InGamePurchase),
            RefundTransactions = Slice(byKind, AccountTransactionKinds.Refund),
            WalletCreditPurchases = Slice(byKind, AccountTransactionKinds.WalletCreditPurchase),
            WalletCreditRedemptions = Slice(byKind, AccountTransactionKinds.WalletCreditRedemption),
            BundlePurchases = new AccountSpendSlice((int)bundles.Count, bundles.Cents),
            DiscountedPurchases = new AccountSpendSlice((int)discounted.Count, discounted.Cents),
            DiscountedPurchaseListCents = discounted.ListCents,
            BiggestPurchase = biggest is null ? null : new AccountBiggestPurchase
            {
                Cents = biggest.Cents,
                OccurredAt = biggest.OccurredAt,
                ItemNames = AccountFactJson.ReadItemNames(biggest.ItemNamesJson),
                ItemCount = (int)biggest.ItemCount,
                CurrencySymbol = biggest.CurrencySymbol,
            },
            FirstTransactionAt = shape.FirstTransactionAt,
            LastTransactionAt = shape.LastTransactionAt,
            TransactionsWithoutDate = (int)shape.TransactionsWithoutDate,
            Currencies = currencies
                .Select(c => new AccountCurrencyUse(c.Symbol, (int)c.TransactionCount))
                .ToList(),
            TransactionsWithoutCurrency = (int)shape.TransactionsWithoutCurrency,
            LicenseAcquisitions = acquisitions
                .Select(a => new AccountLicenseAcquisition(a.Kind, (int)a.Count))
                .ToList(),
            LicensesWithUnrecognizedMethod = (int)licenses.UnrecognizedCount,
            LicensesWithoutDate = (int)licenses.UndatedCount,
            FirstLicenseAt = licenses.FirstLicenseAt,
            LastLicenseAt = licenses.LastLicenseAt,
        };
    }

    private static AccountSpendSlice Slice(IReadOnlyDictionary<string, AccountSpendSlice> byKind, string kind)
        => byKind.TryGetValue(kind, out var slice) ? slice : AccountSpendSlice.Empty;

    // SQLite hands COUNT() and SUM() back as 64-bit integers, and a MIN() over an
    // empty table has no declared type at all. These are property-mapped classes
    // rather than positional records because Dapper converts per property but
    // requires a constructor whose parameter types match exactly.
    private sealed class ShapeRow
    {
        public long TransactionCount { get; init; }

        public long TransactionsWithoutDate { get; init; }

        public long TransactionsWithoutCurrency { get; init; }

        public DateTime? FirstTransactionAt { get; init; }

        public DateTime? LastTransactionAt { get; init; }
    }

    private sealed class SpendRow
    {
        public long GrossCount { get; init; }

        public long GrossCents { get; init; }

        public long RefundedCount { get; init; }

        public long RefundedCents { get; init; }

        public long UndatedCount { get; init; }

        public long UndatedCents { get; init; }
    }

    private sealed class SliceRow
    {
        public string Kind { get; init; } = string.Empty;

        public long Count { get; init; }

        public long Cents { get; init; }
    }

    private sealed class SliceTotalsRow
    {
        public long Count { get; init; }

        public long Cents { get; init; }
    }

    private sealed class DiscountRow
    {
        public long Count { get; init; }

        public long Cents { get; init; }

        public long ListCents { get; init; }
    }

    private sealed class YearRow
    {
        public long Year { get; init; }

        public long TransactionCount { get; init; }

        public long Cents { get; init; }
    }

    private sealed class CurrencyRow
    {
        public string Symbol { get; init; } = string.Empty;

        public long TransactionCount { get; init; }
    }

    private sealed class AcquisitionRow
    {
        public string? Kind { get; init; }

        public long Count { get; init; }
    }

    private sealed class BiggestRow
    {
        public long Cents { get; init; }

        public DateTime? OccurredAt { get; init; }

        public string ItemNamesJson { get; init; } = "[]";

        public long ItemCount { get; init; }

        public string? CurrencySymbol { get; init; }
    }

    private sealed class LicenseShapeRow
    {
        public long LicenseCount { get; init; }

        public long UnrecognizedCount { get; init; }

        public long UndatedCount { get; init; }

        public DateTime? FirstLicenseAt { get; init; }

        public DateTime? LastLicenseAt { get; init; }
    }
}
