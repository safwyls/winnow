using Winnow.Core.Ingest;

namespace Winnow.Ingest.Epic;

/// <summary>
/// The three parts Epic's launcher URL needs: <c>namespace%3AcatalogItemId%3AappName</c>.
/// Read from the local files at ingest and persisted so the UI can build an action
/// without a network call.
/// </summary>
/// <param name="CatalogItemId">
/// The entitlement id — the same value <see cref="CandidateOwnership.ProviderId"/>
/// carries for this reader.
/// </param>
/// <param name="CatalogNamespace">
/// 32-hex, or a short word such as <c>fn</c> or <c>catnip</c>.
/// <b>Not unique on its own</b>; qualifies the item id within Epic's composite key.
/// </param>
/// <param name="AppName">
/// Epic's per-artifact release id. <b>A codename, never a title</b> — Fez's is
/// <c>"Bluebird"</c>.
/// </param>
public sealed record EpicLaunchTriple(
    string CatalogItemId,
    string CatalogNamespace,
    string AppName);

/// <summary>
/// What one Epic filesystem pass produces. Two outputs from one pass because the
/// catalog file is decoded once and both answers fall out of the same walk.
/// </summary>
/// <param name="Candidates">Ownership candidates, one per owned base game.</param>
/// <param name="LaunchTriples">
/// One per candidate whose namespace and AppName were both found. A partial triple
/// builds a URL the launcher cannot resolve, so those are dropped at the source.
/// </param>
public readonly record struct EpicScanResult(
    IReadOnlyList<CandidateOwnership> Candidates,
    IReadOnlyList<EpicLaunchTriple> LaunchTriples);
