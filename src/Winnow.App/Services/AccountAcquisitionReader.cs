using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;

namespace Winnow.App.Services;

public interface IAccountAcquisitionReader
{
    /// <summary>Projects acquisition evidence for the same account scope as the library.</summary>
    Task<IReadOnlyList<Ownership>> ProjectAsync(IReadOnlyList<Ownership> ownerships, CancellationToken ct = default);
}

public sealed class AccountAcquisitionReader(IAccountAcquisitionRepository observations,
    ISettingsRepository settings) : IAccountAcquisitionReader
{
    public async Task<IReadOnlyList<Ownership>> ProjectAsync(IReadOnlyList<Ownership> ownerships,
        CancellationToken ct = default)
    {
        var ownOnly = AccountScope.IsOwnOnly(await settings.GetAsync(AccountScope.SettingKey, ct).ConfigureAwait(false));
        var account = ownOnly
            ? SteamOwnedAccount.Clean(await settings.GetAsync(SteamOwnedAccount.RefSettingKey, ct).ConfigureAwait(false))
            : null;
        var evidence = await observations.GetAsync(ownerships.Select(o => o.Id).ToArray(), ct).ConfigureAwait(false);
        var byOwnership = evidence.ToLookup(e => e.OwnershipId);
        return ownerships.Select(o => Project(o, byOwnership[o.Id], account)).ToArray();
    }

    internal static Ownership Project(Ownership ownership, IEnumerable<OwnershipAcquisitionObservation> observations,
        string? account)
    {
        if (ownership.Store != ExternalIdProviders.Steam) return ownership;
        var rows = observations.Where(o => account is null || o.AccountRef == account).ToList();
        // Shared ownership fields predate account provenance. They remain useful
        // in aggregate mode but cannot supply a named account's acquisition.
        if (account is null)
            rows.Add(new() { OwnershipId = ownership.Id, AcquiredAt = ownership.AcquiredAt,
                LicenseType = ownership.LicenseType, PricePaidCents = ownership.PricePaidCents,
                PriceSource = ownership.PriceSource, Source = "legacy", CapturedAt = DateTime.MinValue });
        var prices = rows.Where(r => r.PricePaidCents is not null).Select(r => (r.PricePaidCents, r.PriceSource)).Distinct().ToArray();
        var licenses = rows.Select(r => r.LicenseType).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToArray();
        return ownership with
        {
            AcquiredAt = rows.Select(r => r.AcquiredAt).Min(),
            LicenseType = licenses.Length == 1 ? licenses[0] : null,
            PricePaidCents = prices.Length == 1 ? prices[0].PricePaidCents : null,
            PriceSource = prices.Length == 1 ? prices[0].PriceSource : null,
        };
    }
}
