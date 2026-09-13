using System.Text;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Winnow.Core.Ingest;

namespace Winnow.Ingest.Steam.AccountPages;

/// <summary>
/// The saved-file route: takes user-picked paths, reads them as UTF-8 (Steam
/// serves UTF-8 and browsers preserve it on save), identifies which page each
/// one is by its content, and assembles them into a
/// <see cref="SteamAccountPages"/> with <see cref="SteamAccountPageSource.SavedFile"/>.
///
/// <para>Which page a file is comes from what is inside it, never from its
/// filename, because the user saved these under whatever their browser suggested.
/// A BOM is honoured if present. A size ceiling prevents a mistaken pick from
/// pulling an arbitrary file into memory. No UI here; that is a separate
/// package.</para>
///
/// <para>The contract and its result types live in Winnow.Core so the import
/// screen can offer this route without naming an ingest type (§5.1).</para>
/// </summary>
public sealed partial class SteamAccountPageFileLoader : ISteamAccountPageFileLoader
{
    private const long MaxFileBytes = 64L * 1024 * 1024;

    private readonly ILogger<SteamAccountPageFileLoader> _logger;
    private readonly TimeProvider _clock;

    public SteamAccountPageFileLoader(
        ILogger<SteamAccountPageFileLoader>? logger = null,
        TimeProvider? clock = null)
    {
        _logger = logger ?? NullLogger<SteamAccountPageFileLoader>.Instance;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>
    /// Reads each path, identifies it, and assembles the pages. Per-file outcomes
    /// so one bad path does not fail the rest. Licence documents combine;
    /// purchase history still takes the first successfully identified file.
    /// </summary>
    public async Task<SteamAccountPageLoadResult> LoadAsync(
        IEnumerable<string> paths, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var pages = new SteamAccountPages
        {
            CapturedAt = _clock.GetUtcNow(),
            Source = SteamAccountPageSource.SavedFile,
        };

        var files = new List<SteamAccountPageFile>();
        var licenseHashes = new HashSet<string>(StringComparer.Ordinal);
        string? accountMarker = null;

        foreach (var path in paths)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var (html, failure) = await TryReadAsync(path, ct).ConfigureAwait(false);
            if (failure is not null)
            {
                files.Add(failure);
                continue;
            }

            // Which page a file is comes from what is inside it, never from what
            // it is called: the user saved these under whatever name their
            // browser offered.
            var kind = SteamAccountPageReader.Identify(html);
            if (kind is null)
            {
                files.Add(new SteamAccountPageFile(
                    path,
                    SteamAccountPageFileOutcome.NotRecognized,
                    null,
                    "neither a licenses table nor a wallet history table"));
                _logger.LogWarning("Saved page {Path} is not a Steam account page Winnow can read", path);
                continue;
            }

            var marker = ReadAccountMarker(html!);
            if (marker == string.Empty || (marker is not null && accountMarker is not null && marker != accountMarker))
            {
                files.Add(new(path, SteamAccountPageFileOutcome.AccountMismatch, kind,
                    "the saved pages identify different accounts"));
                continue;
            }

            if ((kind == SteamAccountPageKind.PurchaseHistory && pages.HasHistory)
                || (kind == SteamAccountPageKind.Licenses
                    && !licenseHashes.Add(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(html!))))))
            {
                files.Add(new SteamAccountPageFile(
                    path,
                    SteamAccountPageFileOutcome.Duplicate,
                    kind,
                    "this licence document or a purchase-history file was already loaded"));
                continue;
            }

            accountMarker ??= marker;
            pages = kind == SteamAccountPageKind.Licenses && pages.HasLicenses
                ? pages with { AdditionalLicensesHtml = [.. pages.AdditionalLicensesHtml, html!] }
                : pages.With(kind.Value, html);
            files.Add(new SteamAccountPageFile(path, SteamAccountPageFileOutcome.Loaded, kind, null));
        }

        return new SteamAccountPageLoadResult(pages with
        {
            HasFailedSavedInputs = files.Any(f => f.Outcome is not (SteamAccountPageFileOutcome.Loaded or SteamAccountPageFileOutcome.Duplicate)),
        }, files);
    }

    [GeneratedRegex("(?:var\\s+)?g_steamID\\s*=\\s*['\\\"]([0-9]{17})['\\\"]", RegexOptions.CultureInvariant)]
    private static partial Regex AccountMarkerRegex { get; }

    private static string? ReadAccountMarker(string html)
    {
        // A saved marker can veto a mixed-account batch, but cannot authenticate
        // an account or assign these files to the currently connected account.
        var document = new HtmlParser().ParseDocument(html);
        var markers = document.QuerySelectorAll("script").SelectMany(s => AccountMarkerRegex.Matches(s.TextContent))
            .Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToArray();
        return markers.Length switch { 0 => null, 1 => markers[0], _ => string.Empty };
    }

    private async Task<(string? Html, SteamAccountPageFile? Failure)> TryReadAsync(
        string path, CancellationToken ct)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return (null, new SteamAccountPageFile(
                    path, SteamAccountPageFileOutcome.NotFound, null, "no such file"));
            }

            if (info.Length > MaxFileBytes)
            {
                return (null, new SteamAccountPageFile(
                    path, SteamAccountPageFileOutcome.Unreadable, null, "file is larger than the import limit"));
            }

            // Steam serves these as UTF-8 and browsers preserve that on save.
            // detectEncodingFromByteOrderMarks still honours a BOM if one is there.
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var html = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            return (html, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            _logger.LogWarning(ex, "Could not read saved Steam account page {Path}", path);
            return (null, new SteamAccountPageFile(
                path, SteamAccountPageFileOutcome.Unreadable, null, ex.GetType().Name));
        }
    }
}
