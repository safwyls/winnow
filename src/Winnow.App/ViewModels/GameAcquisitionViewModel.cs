using Winnow.Core.Domain;
using Winnow.Ingest.Steam.AccountPages;

namespace Winnow.App.ViewModels;

/// <summary>
/// The left column's ACQUIRED block. Acquisition is a fact about THIS COPY — the
/// ownership row the user actually holds — rather than about the game, which is
/// why it sits in the object column beside the appid and the path.
///
/// <para>The earliest date across a game's ownership rows is the one that answers
/// when the user got it. An unrecognised licence type says nothing rather than
/// showing a stored token, because the parser writes a vocabulary value only for
/// a method it recognises. The licence words are <see cref="AccountStatsCopy"/>'s,
/// so the account screen and this modal name one thing one way.</para>
///
/// <para><c>price_paid_cents</c> is deliberately never read here. §7 says "never
/// be smug", and "$59.99 · never opened" is the sentence this product must not
/// write. Price belongs to the export and the account stats screen.</para>
/// </summary>
public sealed class GameAcquisitionViewModel
{
    private GameAcquisitionViewModel(DateTime? acquiredAtUtc, string? licenseType)
    {
        HasDate = acquiredAtUtc is not null;
        DateText = acquiredAtUtc is { } at ? UpdateEventViewModel.LocalDateText(at) : string.Empty;
        LicenseText = LicenceLabel(licenseType);
        HasLicence = LicenseText.Length > 0;
    }

    /// <summary>True when at least one ownership row carries an acquisition date.</summary>
    public bool HasDate { get; }

    /// <summary>The earliest acquisition date, formatted for display.</summary>
    public string DateText { get; }

    /// <summary>True when a recognised licence type was found.</summary>
    public bool HasLicence { get; }

    /// <summary>The licence word, from <see cref="AccountStatsCopy"/>. Empty for unrecognised types.</summary>
    public string LicenseText { get; }

    /// <summary>
    /// Builds the block from the ownership rows. Returns null when neither a
    /// date nor a licence exists, which is what makes "nothing rather than a
    /// placeholder" a property of the data.
    /// </summary>
    public static GameAcquisitionViewModel? From(IReadOnlyList<Ownership>? ownerships)
    {
        if (ownerships is not { Count: > 0 })
        {
            return null;
        }

        var acquired = ownerships
            .Select(o => o.AcquiredAt)
            .Where(at => at is not null)
            .Select(at => UpdateEventViewModel.AsUtc(at!.Value))
            .DefaultIfEmpty()
            .Min();

        DateTime? acquiredAt = acquired == default ? null : acquired;

        var licence = ownerships
            .Select(o => o.LicenseType)
            .FirstOrDefault(type => !string.IsNullOrWhiteSpace(type));

        return acquiredAt is null && licence is null
            ? null
            : new GameAcquisitionViewModel(acquiredAt, licence);
    }

    private static string LicenceLabel(string? licenseType) => licenseType switch
    {
        SteamLicenseTypes.SteamStore => AccountStatsCopy.LabelLicenceSteamStore,
        SteamLicenseTypes.Complimentary => AccountStatsCopy.LabelLicenceComplimentary,
        SteamLicenseTypes.Gift => AccountStatsCopy.LabelLicenceGift,
        SteamLicenseTypes.Retail => AccountStatsCopy.LabelLicenceRetail,
        _ => string.Empty,
    };
}
