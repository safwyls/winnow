using Microsoft.Extensions.Logging.Abstractions;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Ingest;
using Winnow.Data.Repositories;
using Winnow.Ingest.Steam.AccountPages;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The importer: what it fills, what it refuses to fill, and what a second run
/// over the same pages does (nothing).
/// </summary>
public class SteamAccountPageImportTests : IDisposable
{
    private readonly TempDatabase _db = new();
    private readonly WorkRepository _works;
    private readonly ReleaseRepository _releases;
    private readonly OwnershipRepository _ownerships;

    public SteamAccountPageImportTests()
    {
        _works = new WorkRepository(_db.Factory);
        _releases = new ReleaseRepository(_db.Factory);
        _ownerships = new OwnershipRepository(_db.Factory);
    }

    public void Dispose() => _db.Dispose();

    // The write phase runs inside one unit of work under the shared sync gate,
    // so a user-clicked import cannot open a write transaction alongside the
    // startup pipeline's resolver pass. The temp database's factory is the
    // IUnitOfWorkFactory too, exactly as the real composition root wires it.
    private SteamAccountPageImportService Service() => new(
        _ownerships,
        _releases,
        _db.Factory,
        new LibrarySyncGate(),
        NullLogger<SteamAccountPageImportService>.Instance);

    private static SteamAccountPages Pages(bool licenses = true, bool history = true) => new()
    {
        CapturedAt = new DateTimeOffset(2026, 8, 29, 0, 0, 0, TimeSpan.Zero),
        Source = SteamAccountPageSource.SavedFile,
        LicensesHtml = licenses
            ? SteamAccountPageFixtures.Read(SteamAccountPageFixtures.LicensesPage1)
            : null,
        HistoryHtml = history
            ? SteamAccountPageFixtures.Read(SteamAccountPageFixtures.PurchaseHistory)
            : null,
    };

    private async Task<long> OwnAsync(
        string name,
        string store = ExternalIdProviders.Steam,
        bool provisional = false,
        DateTime? acquiredAt = null,
        string? licenseType = null,
        long? pricePaidCents = null,
        string? priceSource = null)
    {
        var workId = await _works.InsertAsync(new Work
        {
            Name = name,
            SortName = name.ToLowerInvariant(),
            NameIsProvisional = provisional,
        });

        var releaseId = await _releases.InsertAsync(new Release { WorkId = workId, Name = name });

        return await _ownerships.InsertAsync(new Ownership
        {
            ReleaseId = releaseId,
            Store = store,
            AcquiredAt = acquiredAt,
            LicenseType = licenseType,
            PricePaidCents = pricePaidCents,
            PriceSource = priceSource,
        });
    }

    private static DateTime Utc(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    // ── matching ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_exact_title_match_fills_acquisition_date_and_licence_type()
    {
        var id = await OwnAsync("Lantern Hollow");

        var report = await Service().ImportAsync(Pages());

        Assert.Equal(1, report.OwnershipsFilled);

        var stored = await _ownerships.GetAsync(id);
        Assert.Equal(Utc(2026, 8, 24), stored!.AcquiredAt);
        Assert.Equal(SteamLicenseTypes.SteamStore, stored.LicenseType);
        Assert.Equal(1349, stored.PricePaidCents);
        Assert.Equal(PriceSources.SteamAccountHistory, stored.PriceSource);
    }

    [Fact]
    public async Task A_normalised_title_match_applies()
    {
        // Marks, articles and case all fold away in TitleNormalizer, so the
        // stored name need not be byte-identical to the page's.
        var id = await OwnAsync("MISTRAL(tm): Tides of Ruin".Replace("(tm)", "™", StringComparison.Ordinal));

        var report = await Service().ImportAsync(Pages());

        Assert.True(report.AcquisitionsMatched >= 1);
        var stored = await _ownerships.GetAsync(id);
        Assert.Equal(Utc(2026, 6, 30), stored!.AcquiredAt);
    }

    [Fact]
    public async Task A_title_with_no_owned_release_is_counted_and_nothing_is_written()
    {
        await OwnAsync("Something Else Entirely");

        var report = await Service().ImportAsync(Pages());

        Assert.Equal(0, report.OwnershipsFilled);
        Assert.True(report.SkippedNoOwnershipMatch > 0);
    }

    [Fact]
    public async Task Two_owned_releases_that_normalise_alike_are_ambiguous_and_neither_is_touched()
    {
        var a = await OwnAsync("Lantern Hollow");
        var b = await OwnAsync("The Lantern Hollow");

        var report = await Service().ImportAsync(Pages());

        Assert.Equal(1, report.OwnershipsAmbiguousByTitle);
        Assert.True(report.SkippedAmbiguousTitle > 0);
        Assert.Null((await _ownerships.GetAsync(a))!.AcquiredAt);
        Assert.Null((await _ownerships.GetAsync(b))!.AcquiredAt);
    }

    [Fact]
    public async Task A_provisional_name_is_never_matched_against()
    {
        var id = await OwnAsync("Lantern Hollow", provisional: true);

        var report = await Service().ImportAsync(Pages());

        Assert.Equal(0, report.SteamOwnershipsConsidered);
        Assert.Equal(0, report.OwnershipsFilled);
        Assert.Null((await _ownerships.GetAsync(id))!.AcquiredAt);
    }

    [Fact]
    public async Task An_ownership_on_another_store_is_never_matched_against()
    {
        var id = await OwnAsync("Lantern Hollow", store: ExternalIdProviders.Gog);

        var report = await Service().ImportAsync(Pages());

        Assert.Equal(0, report.SteamOwnershipsConsidered);
        Assert.Null((await _ownerships.GetAsync(id))!.AcquiredAt);
    }

    // ── what price is refused ────────────────────────────────────────────────

    [Fact]
    public async Task A_bundle_rows_price_is_never_attributed_to_any_of_its_items()
    {
        var id = await OwnAsync("Gravewright : Ascendant - Companion : Ivory Lynx");

        var report = await Service().ImportAsync(Pages());

        Assert.Equal(1, report.SkippedBundleRows);

        var stored = await _ownerships.GetAsync(id);

        // The licences page still dates it; only the price is withheld.
        Assert.Equal(Utc(2026, 8, 20), stored!.AcquiredAt);
        Assert.Equal(SteamLicenseTypes.Complimentary, stored.LicenseType);
        Assert.Null(stored.PricePaidCents);
        Assert.Null(stored.PriceSource);
    }

    [Fact]
    public async Task A_refunded_purchase_price_is_never_applied()
    {
        var id = await OwnAsync("Mistral®: Tides of Ruin");

        var report = await Service().ImportAsync(Pages());

        Assert.Equal(2, report.SkippedRefundedRows);
        Assert.Null((await _ownerships.GetAsync(id))!.PricePaidCents);
    }

    [Fact]
    public async Task A_gift_purchase_price_is_never_applied_to_this_accounts_ownership()
    {
        var id = await OwnAsync("Cinder & Bloom");

        var report = await Service().ImportAsync(Pages());

        Assert.True(report.SkippedNonPurchaseRows > 0);

        var stored = await _ownerships.GetAsync(id);
        Assert.Equal(SteamLicenseTypes.Gift, stored!.LicenseType);
        Assert.Null(stored.PricePaidCents);
    }

    [Fact]
    public async Task An_in_game_purchase_price_is_never_applied_to_the_game()
    {
        var id = await OwnAsync("Solder Queen");

        await Service().ImportAsync(Pages());

        // Solder Queen has an in-game purchase AND a real purchase in the
        // fixture; only the latter may set the price.
        var stored = await _ownerships.GetAsync(id);
        Assert.Equal(124900, stored!.PricePaidCents);
    }

    [Fact]
    public async Task A_wallet_top_up_is_not_a_product_and_matches_nothing()
    {
        var report = await Service().ImportAsync(Pages());

        Assert.Equal(2, report.SkippedNonProductRows);
    }

    // ── fill-only ────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_existing_acquired_at_is_never_overwritten()
    {
        var existing = Utc(2020, 1, 1);
        var id = await OwnAsync("Lantern Hollow", acquiredAt: existing);

        await Service().ImportAsync(Pages());

        var stored = await _ownerships.GetAsync(id);
        Assert.Equal(existing, stored!.AcquiredAt);

        // The columns that WERE empty are still filled in the same pass.
        Assert.Equal(SteamLicenseTypes.SteamStore, stored.LicenseType);
        Assert.Equal(1349, stored.PricePaidCents);
    }

    [Fact]
    public async Task An_existing_price_and_its_source_are_never_overwritten()
    {
        var id = await OwnAsync("Lantern Hollow", pricePaidCents: 999, priceSource: "manual");

        await Service().ImportAsync(Pages());

        var stored = await _ownerships.GetAsync(id);
        Assert.Equal(999, stored!.PricePaidCents);
        Assert.Equal("manual", stored.PriceSource);
        Assert.Equal(Utc(2026, 8, 24), stored.AcquiredAt);
    }

    [Fact]
    public async Task An_existing_licence_type_is_never_overwritten()
    {
        var id = await OwnAsync("Lantern Hollow", licenseType: "already_known");

        await Service().ImportAsync(Pages());

        Assert.Equal("already_known", (await _ownerships.GetAsync(id))!.LicenseType);
    }

    [Fact]
    public async Task An_unmapped_acquisition_method_leaves_licence_type_null_but_still_dates_the_row()
    {
        var id = await OwnAsync("Tessellate");

        var report = await Service().ImportAsync(Pages());

        Assert.Equal(1, report.LicenseRowsUnmappedAcquisition);

        var stored = await _ownerships.GetAsync(id);
        Assert.Equal(Utc(2023, 2, 2), stored!.AcquiredAt);
        Assert.Null(stored.LicenseType);
    }

    // ── idempotency ──────────────────────────────────────────────────────────

    [Fact]
    public async Task A_second_run_over_the_same_pages_writes_nothing()
    {
        await OwnAsync("Lantern Hollow");
        await OwnAsync("Verdant Circuit Early Access");
        await OwnAsync("Café Nocturne");

        var first = await Service().ImportAsync(Pages());
        Assert.True(first.OwnershipsFilled > 0);

        var second = await Service().ImportAsync(Pages());

        Assert.Equal(0, second.OwnershipsFilled);
        Assert.False(second.WroteAnything);
        Assert.Equal(first.AcquisitionsMatched, second.AcquisitionsMatched);
        Assert.True(second.OwnershipsAlreadyComplete > 0);
    }

    [Fact]
    public async Task A_third_run_still_writes_nothing_and_the_values_are_unchanged()
    {
        var id = await OwnAsync("Lantern Hollow");

        await Service().ImportAsync(Pages());
        var afterFirst = await _ownerships.GetAsync(id);

        await Service().ImportAsync(Pages());
        var third = await Service().ImportAsync(Pages());

        Assert.Equal(0, third.OwnershipsFilled);
        Assert.Equal(afterFirst, await _ownerships.GetAsync(id));
    }

    // ── partial and unusable input ───────────────────────────────────────────

    [Fact]
    public async Task Licences_alone_still_fills_dates_and_types_but_no_price()
    {
        var id = await OwnAsync("Lantern Hollow");

        var report = await Service().ImportAsync(Pages(history: false));

        Assert.Equal(SteamAccountPageParseOutcome.Absent, report.HistoryOutcome);

        var stored = await _ownerships.GetAsync(id);
        Assert.Equal(Utc(2026, 8, 24), stored!.AcquiredAt);
        Assert.Equal(SteamLicenseTypes.SteamStore, stored.LicenseType);
        Assert.Null(stored.PricePaidCents);
    }

    [Fact]
    public async Task History_alone_dates_the_ownership_from_the_purchase()
    {
        var id = await OwnAsync("Lantern Hollow");

        var report = await Service().ImportAsync(Pages(licenses: false));

        Assert.Equal(SteamAccountPageParseOutcome.Absent, report.LicensesOutcome);

        var stored = await _ownerships.GetAsync(id);
        Assert.Equal(Utc(2026, 8, 24), stored!.AcquiredAt);
        Assert.Equal(1349, stored.PricePaidCents);
        Assert.Null(stored.LicenseType);
    }

    [Fact]
    public async Task An_unrecognised_document_is_reported_as_such_and_writes_nothing()
    {
        var id = await OwnAsync("Lantern Hollow");

        var report = await Service().ImportAsync(new SteamAccountPages
        {
            CapturedAt = DateTimeOffset.UtcNow,
            LicensesHtml = SteamAccountPageFixtures.Read(SteamAccountPageFixtures.NotAnAccountPage),
        });

        Assert.Equal(SteamAccountPageParseOutcome.NotRecognized, report.LicensesOutcome);
        Assert.NotNull(report.LicensesFailureReason);
        Assert.Equal(0, report.OwnershipsFilled);
        Assert.Null((await _ownerships.GetAsync(id))!.AcquiredAt);
    }

    [Fact]
    public async Task An_empty_capture_is_a_no_op()
    {
        var report = await Service().ImportAsync(new SteamAccountPages { CapturedAt = DateTimeOffset.UtcNow });

        Assert.Equal(0, report.OwnershipsFilled);
        Assert.False(report.WroteAnything);
    }

    [Fact]
    public async Task Truncation_is_reported_so_the_caller_knows_it_saw_part_of_the_account()
    {
        var report = await Service().ImportAsync(Pages());

        Assert.True(report.LicensesTruncated);
        Assert.Equal(979, report.LicensesReportedTotal);
        Assert.True(report.HistoryTruncated);
    }
}
