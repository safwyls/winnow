using System.Text.Json.Serialization;
using Winnow.Enrich.Igdb.Model;

namespace Winnow.Enrich.Igdb;

internal sealed record IgdbEditionPayload(
    [property: JsonRequired] int Version,
    [property: JsonRequired] int SourceId,
    [property: JsonRequired] string Uid,
    [property: JsonRequired] IgdbEditionMatchStatus Status,
    [property: JsonRequired] long? GameId,
    [property: JsonRequired] long? ParentId,
    [property: JsonRequired] string? Title);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(IgdbEditionPayload))]
internal sealed partial class IgdbEditionJson : JsonSerializerContext;
