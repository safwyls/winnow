namespace Winnow.App.Services;

/// <summary>
/// One report from the enrichment pass to the rail's fetch status field.
/// <see cref="Remaining"/> reaching zero is what "done" means; there is no
/// separate completion signal.
/// </summary>
public readonly record struct EnrichmentProgress(int Total, int Remaining);
