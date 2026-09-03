using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Winnow.Ingest.Steam.AccountPages;

/// <summary>
/// Parses the Steam purchase-history page
/// (<c>store.steampowered.com/account/history/</c>) with AngleSharp (per
/// game-library-design.md).
///
/// <para>The table is <c>table.wallet_history_table</c>, recognised by a
/// <c>thead th.wht_date</c> header. Rows are <c>tr.wallet_table_row</c>. Cells:
/// <c>wht_date</c>, <c>wht_items</c>, <c>wht_type</c>, <c>wht_base_price</c>,
/// <c>wht_total</c>, <c>wht_wallet_change</c>, <c>wht_wallet_balance</c>.</para>
///
/// <para>The payment sub-element is spelled <c>wth_payment</c> (transposed h/t),
/// and it appears both inside the type cell (payment method) and inside the items
/// cell (gift recipient, in-game item quantity). The items reader skips it rather
/// than treating it as a product line.</para>
///
/// <para>Items cell holds one <c>div style="clear: both"</c> per product, so a
/// bundle is N items under one price. Discounts wrap in
/// <c>.wht_base_price_discounted</c> containing <c>.wht_discount_pct</c>,
/// <c>.wht_original_price</c> and <c>.wht_discounted_price</c>; the discounted
/// price is what was paid. Verified 2026-08-29.</para>
/// </summary>
public static partial class SteamPurchaseHistoryPageParser
{
    [GeneratedRegex(@"[?&]appid=(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex AppIdRegex { get; }

    /// <summary>Parses a purchase-history page document into rows, or returns Absent/NotRecognized.</summary>
    public static SteamPurchaseHistoryPageResult Parse(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return SteamPurchaseHistoryPageResult.Absent();
        }

        var document = new HtmlParser().ParseDocument(html);

        var table = document
            .QuerySelectorAll("table.wallet_history_table")
            .FirstOrDefault(t => t.QuerySelector("thead th.wht_date") is not null);

        if (table is null)
        {
            return SteamPurchaseHistoryPageResult.NotRecognized(
                "no table.wallet_history_table carrying a thead th.wht_date header");
        }

        var rows = new List<SteamPurchaseRow>();
        var skipped = 0;
        var unparsedDate = 0;
        var unparsedTotal = 0;
        var index = 0;

        foreach (var tr in table.QuerySelectorAll("tr.wallet_table_row"))
        {
            index++;

            var dateCell = tr.QuerySelector("td.wht_date");
            var itemsCell = tr.QuerySelector("td.wht_items");
            var typeCell = tr.QuerySelector("td.wht_type");

            if (dateCell is null || itemsCell is null || typeCell is null)
            {
                skipped++;
                continue;
            }

            var purchasedAt = SteamPageValues.TryParseDateUtc(dateCell.TextContent);
            if (purchasedAt is null)
            {
                unparsedDate++;
            }

            var (items, note) = ReadItems(itemsCell);
            var transactionType = ReadFirstDivText(typeCell);
            var paymentKind = ReadPaymentKind(typeCell);

            // A row with neither a product, a note, nor a type is not a shape
            // this parser knows. It is counted, never guessed at.
            if (items.Count == 0 && note is null && transactionType.Length == 0)
            {
                skipped++;
                continue;
            }

            var total = SteamPageValues.TryParseMoney(tr.QuerySelector("td.wht_total")?.TextContent);
            var (basePrice, originalPrice, discountPercent) = ReadPrices(tr.QuerySelector("td.wht_base_price"));
            var walletChange = SteamPageValues.TryParseMoney(tr.QuerySelector("td.wht_wallet_change")?.TextContent);

            if (total is null && basePrice is not null)
            {
                unparsedTotal++;
            }

            rows.Add(new SteamPurchaseRow
            {
                RowIndex = index,
                PurchasedAtUtc = purchasedAt,
                Items = items,
                Note = note,
                TransactionType = transactionType,
                PaymentKind = paymentKind,
                Total = total,
                BasePrice = basePrice,
                OriginalPrice = originalPrice,
                DiscountPercent = discountPercent,
                WalletChange = walletChange,
                Refunded = IsRefunded(tr, itemsCell, typeCell),
                AppId = ReadAppId(tr),
            });
        }

        return new SteamPurchaseHistoryPageResult
        {
            Outcome = SteamAccountPageParseOutcome.Parsed,
            Rows = rows,
            SkippedRows = skipped,
            RowsWithUnparsedDate = unparsedDate,
            RowsWithUnparsedTotal = unparsedTotal,
            HasMoreToLoad = HasVisibleLoadMore(document),
        };
    }

    private static (IReadOnlyList<string> Items, string? Note) ReadItems(IElement itemsCell)
    {
        var items = new List<string>();

        foreach (var child in itemsCell.Children)
        {
            if (!string.Equals(child.TagName, "DIV", StringComparison.Ordinal))
            {
                continue;
            }

            // wth_payment inside the items cell is the gift recipient or the
            // in-game item quantity, never a product line.
            if (child.ClassList.Contains("wth_payment"))
            {
                continue;
            }

            var text = SteamPageValues.Collapse(child.TextContent);
            if (text.Length > 0)
            {
                items.Add(text);
            }
        }

        if (items.Count > 0)
        {
            return (items, null);
        }

        // No product divs: a wallet movement whose "item" is a line of prose
        // ("Digital Gift Card Redemption"). Kept as a note so the importer can
        // see it and decline to match it against a game.
        var clone = (IElement)itemsCell.Clone(deep: true);
        foreach (var payment in clone.QuerySelectorAll(".wth_payment"))
        {
            payment.Remove();
        }

        var noteText = SteamPageValues.Collapse(clone.TextContent);
        return ([], noteText.Length == 0 ? null : noteText);
    }

    private static string ReadFirstDivText(IElement typeCell)
    {
        foreach (var child in typeCell.Children)
        {
            if (!string.Equals(child.TagName, "DIV", StringComparison.Ordinal)
                || child.ClassList.Contains("wth_payment"))
            {
                continue;
            }

            return SteamPageValues.Collapse(child.TextContent);
        }

        return string.Empty;
    }

    private static string? ReadPaymentKind(IElement typeCell)
        => SteamPaymentKinds.Classify(typeCell.QuerySelector(".wth_payment")?.TextContent);

    private static (SteamMoney? Base, SteamMoney? Original, int? DiscountPercent) ReadPrices(IElement? priceCell)
    {
        if (priceCell is null)
        {
            return (null, null, null);
        }

        var discounted = priceCell.QuerySelector(".wht_base_price_discounted");
        if (discounted is null)
        {
            return (SteamPageValues.TryParseMoney(priceCell.TextContent), null, null);
        }

        return (
            SteamPageValues.TryParseMoney(discounted.QuerySelector(".wht_discounted_price")?.TextContent),
            SteamPageValues.TryParseMoney(discounted.QuerySelector(".wht_original_price")?.TextContent),
            SteamPageValues.TryParsePercent(discounted.QuerySelector(".wht_discount_pct")?.TextContent));
    }

    private static bool IsRefunded(IElement row, IElement itemsCell, IElement typeCell)
        => itemsCell.ClassList.Contains("wht_item_refunded")
            || typeCell.ClassList.Contains("wht_refunded")
            || row.QuerySelector("td.wht_total.wht_refunded") is not null;

    private static string? ReadAppId(IElement row)
    {
        var onclick = row.GetAttribute("onclick");
        if (onclick is null)
        {
            return null;
        }

        var match = AppIdRegex.Match(onclick);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static bool HasVisibleLoadMore(IDocument document)
    {
        var button = document.GetElementById("load_more_button");
        if (button is null)
        {
            return false;
        }

        // Steam's own script hides this control rather than removing it, so
        // presence alone does not mean there is more to load. The ancestors are
        // walked as well as the button, because Steam may hide the area around
        // it (div.load_more_history_area) instead of the button itself. Both
        // mean there is nothing more to load, but a check on the button alone
        // misses the second.
        //
        // This reads inline style, which is all a static document carries. A
        // control hidden by a stylesheet rule cannot be detected here at all,
        // which is why a harvested capture reports completeness from the live
        // session instead of from this: see SteamPageHarvestResult.
        for (IElement? element = button; element is not null; element = element.ParentElement)
        {
            if (IsHiddenInline(element))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsHiddenInline(IElement element)
    {
        var style = element.GetAttribute("style");
        if (string.IsNullOrEmpty(style))
        {
            return false;
        }

        var collapsed = style.Replace(" ", string.Empty, StringComparison.Ordinal);

        return collapsed.Contains("display:none", StringComparison.OrdinalIgnoreCase)
            || collapsed.Contains("visibility:hidden", StringComparison.OrdinalIgnoreCase);
    }
}
