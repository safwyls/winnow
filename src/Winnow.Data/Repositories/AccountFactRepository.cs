using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

/// <summary>
/// SQLite storage for the account-page facts (migration 0014). Transaction
/// facts round-trip through <see cref="AccountFactJson"/> for the item-names
/// array; licence facts map directly.
/// </summary>
public sealed class AccountFactRepository : IAccountFactRepository
{
    private readonly ISqliteConnectionFactory _factory;

    public AccountFactRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<long?> TryAppendAsync(AccountTransactionFact fact, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fact);

        var row = TransactionRow.From(fact);

        using var lease = _factory.Lease();
        return await lease.Connection.ExecuteScalarAsync<long?>(new CommandDefinition("""
            INSERT INTO account_transactions (
                source, kind, transaction_type_raw, occurred_at,
                item_names_json, item_count, note,
                total_cents, list_price_cents, discount_percent, wallet_change_cents,
                currency_symbol, payment_kind, refunded, gift_recipient_present,
                app_id, captured_at)
            VALUES (
                @Source, @Kind, @TransactionTypeRaw, @OccurredAt,
                @ItemNamesJson, @ItemCount, @Note,
                @TotalCents, @ListPriceCents, @DiscountPercent, @WalletChangeCents,
                @CurrencySymbol, @PaymentKind, @Refunded, @GiftRecipientPresent,
                @AppId, @CapturedAt)
            ON CONFLICT DO NOTHING
            RETURNING id;
            """, row, transaction: lease.Transaction, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<long?> TryAppendAsync(AccountLicenseFact fact, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fact);

        using var lease = _factory.Lease();
        return await lease.Connection.ExecuteScalarAsync<long?>(new CommandDefinition("""
            INSERT INTO account_licenses (
                source, item_name, acquired_at, acquisition_kind,
                acquisition_method_raw, package_id, captured_at)
            VALUES (
                @Source, @ItemName, @AcquiredAt, @AcquisitionKind,
                @AcquisitionMethodRaw, @PackageId, @CapturedAt)
            ON CONFLICT DO NOTHING
            RETURNING id;
            """, fact, transaction: lease.Transaction, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AccountTransactionFact>> GetTransactionsAsync(
        string source, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var rows = await lease.Connection.QueryAsync<TransactionRow>(new CommandDefinition("""
            SELECT id                     AS Id,
                   source                 AS Source,
                   kind                   AS Kind,
                   transaction_type_raw   AS TransactionTypeRaw,
                   occurred_at            AS OccurredAt,
                   item_names_json        AS ItemNamesJson,
                   item_count             AS ItemCount,
                   note                   AS Note,
                   total_cents            AS TotalCents,
                   list_price_cents       AS ListPriceCents,
                   discount_percent       AS DiscountPercent,
                   wallet_change_cents    AS WalletChangeCents,
                   currency_symbol        AS CurrencySymbol,
                   payment_kind           AS PaymentKind,
                   refunded               AS Refunded,
                   gift_recipient_present AS GiftRecipientPresent,
                   app_id                 AS AppId,
                   captured_at            AS CapturedAt
            FROM account_transactions
            WHERE source = @source
            ORDER BY occurred_at, id;
            """, new { source }, transaction: lease.Transaction, cancellationToken: ct))
            .ConfigureAwait(false);

        return rows.Select(r => r.ToFact()).ToList();
    }

    public async Task<IReadOnlyList<AccountLicenseFact>> GetLicensesAsync(
        string source, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var rows = await lease.Connection.QueryAsync<AccountLicenseFact>(new CommandDefinition("""
            SELECT id                     AS Id,
                   source                 AS Source,
                   item_name              AS ItemName,
                   acquired_at            AS AcquiredAt,
                   acquisition_kind       AS AcquisitionKind,
                   acquisition_method_raw AS AcquisitionMethodRaw,
                   package_id             AS PackageId,
                   captured_at            AS CapturedAt
            FROM account_licenses
            WHERE source = @source
            ORDER BY acquired_at, id;
            """, new { source }, transaction: lease.Transaction, cancellationToken: ct))
            .ConfigureAwait(false);

        return rows.AsList();
    }

    private sealed class TransactionRow
    {
        public long Id { get; init; }

        public string Source { get; init; } = string.Empty;

        public string Kind { get; init; } = string.Empty;

        public string TransactionTypeRaw { get; init; } = string.Empty;

        public DateTime? OccurredAt { get; init; }

        public string ItemNamesJson { get; init; } = AccountFactJson.EmptyItemNames;

        public int ItemCount { get; init; }

        public string? Note { get; init; }

        public long? TotalCents { get; init; }

        public long? ListPriceCents { get; init; }

        public int? DiscountPercent { get; init; }

        public long? WalletChangeCents { get; init; }

        public string? CurrencySymbol { get; init; }

        public string? PaymentKind { get; init; }

        public bool Refunded { get; init; }

        public bool GiftRecipientPresent { get; init; }

        public string? AppId { get; init; }

        public DateTime CapturedAt { get; init; }

        public static TransactionRow From(AccountTransactionFact fact) => new()
        {
            Id = fact.Id,
            Source = fact.Source,
            Kind = fact.Kind,
            TransactionTypeRaw = fact.TransactionTypeRaw,
            OccurredAt = fact.OccurredAt,
            ItemNamesJson = AccountFactJson.WriteItemNames(fact.ItemNames),
            ItemCount = fact.ItemNames.Count,
            Note = fact.Note,
            TotalCents = fact.TotalCents,
            ListPriceCents = fact.ListPriceCents,
            DiscountPercent = fact.DiscountPercent,
            WalletChangeCents = fact.WalletChangeCents,
            CurrencySymbol = fact.CurrencySymbol,
            PaymentKind = fact.PaymentKind,
            Refunded = fact.Refunded,
            GiftRecipientPresent = fact.GiftRecipientPresent,
            AppId = fact.AppId,
            CapturedAt = fact.CapturedAt,
        };

        public AccountTransactionFact ToFact() => new()
        {
            Id = Id,
            Source = Source,
            Kind = Kind,
            TransactionTypeRaw = TransactionTypeRaw,
            OccurredAt = OccurredAt,
            ItemNames = AccountFactJson.ReadItemNames(ItemNamesJson),
            Note = Note,
            TotalCents = TotalCents,
            ListPriceCents = ListPriceCents,
            DiscountPercent = DiscountPercent,
            WalletChangeCents = WalletChangeCents,
            CurrencySymbol = CurrencySymbol,
            PaymentKind = PaymentKind,
            Refunded = Refunded,
            GiftRecipientPresent = GiftRecipientPresent,
            AppId = AppId,
            CapturedAt = CapturedAt,
        };
    }
}

/// <summary>
/// Item names are stored as a JSON array through a source-generated context,
/// because the array is part of the row's identity and therefore must serialise
/// deterministically and identically forever. The empty array is <c>[]</c>
/// rather than NULL so the identity index never has to coalesce it.
/// </summary>
internal static class AccountFactJson
{
    internal const string EmptyItemNames = "[]";

    internal static string WriteItemNames(IReadOnlyList<string> names)
        => names.Count == 0
            ? EmptyItemNames
            : JsonSerializer.Serialize([.. names], AccountFactJsonContext.Default.StringArray);

    internal static IReadOnlyList<string> ReadItemNames(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        return JsonSerializer.Deserialize(json, AccountFactJsonContext.Default.StringArray) ?? [];
    }
}

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(string[]))]
internal sealed partial class AccountFactJsonContext : JsonSerializerContext;
