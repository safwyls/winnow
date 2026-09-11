namespace Winnow.Core.Queries;

/// <summary>The identity and intent revision captured before asking IGDB about a work.</summary>
public sealed record IgdbMappingVersion(long WorkId, long? IgdbId, long Revision);
