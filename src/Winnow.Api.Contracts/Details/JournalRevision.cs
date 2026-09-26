using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Winnow.Api.Contracts.Details;

/// <summary>Identifies exactly the saved journal content an editor observed.</summary>
public static class JournalRevision
{
    public static string For(long sessionId, string? note, int? rating)
        => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new JournalResponse(sessionId, note, rating, ""), JournalRevisionJson.Default.JournalResponse)));
}

[JsonSerializable(typeof(JournalResponse))]
internal sealed partial class JournalRevisionJson : JsonSerializerContext;
