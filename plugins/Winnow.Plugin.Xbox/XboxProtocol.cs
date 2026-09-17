using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Winnow.PluginSdk;

namespace Winnow.Plugin.Xbox;

internal static class XboxProtocol
{
    internal const int MaxBytes = 2 * 1024 * 1024;
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 32 };
    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    internal static string? Text(JsonElement value, string name)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;
    internal static JsonElement Field(JsonElement value, string name)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var field) ? field : default;
    internal static IEnumerable<JsonElement> Rows(JsonElement value)
        => value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];
    internal static int Number(JsonElement value, string name)
        => Field(value, name) is { ValueKind: JsonValueKind.Number } field && field.TryGetInt32(out var result) ? result : 0;
    internal static DateTimeOffset? Date(JsonElement value, string name)
        => DateTimeOffset.TryParse(Text(value, name), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) && date.Year >= 1970 ? date : null;
    internal static bool Token(string? value) => !string.IsNullOrEmpty(value) && value.Length <= 32768 && !value.Any(c => char.IsControl(c) || char.IsWhiteSpace(c));
    internal static bool TitleId(string? value) => value is { Length: > 0 and <= 10 } && value.All(char.IsAsciiDigit) && uint.TryParse(value, out var id) && id > 0;
    internal static bool Xuid(string? value) => value is { Length: > 0 and <= 20 } && value.All(char.IsAsciiDigit) && ulong.TryParse(value, out var id) && id > 0;
    internal static bool Pfn(string? value) => value is { Length: > 0 and <= 255 } && value.Contains('_') && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');
    internal static bool StoreId(string? value) => value is { Length: 12 } && value.All(char.IsAsciiLetterOrDigit);
    internal static string? Clean(string? value, int maximum = 1024) => value is { Length: > 0 } && value.Length <= maximum && !value.Any(c => char.IsControl(c) && c is not '\r' and not '\n' and not '\t') ? value.Trim() : null;
    internal static JsonDocument Parse(byte[] body)
    {
        if (body.Length > MaxBytes) throw new InvalidDataException();
        return JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 32 });
    }
    internal static PluginHttpRequest Form(string url, IReadOnlyDictionary<string, string> fields) => new(url)
    {
        Method = "POST", ContentType = "application/x-www-form-urlencoded",
        Body = Encoding.UTF8.GetBytes(string.Join('&', fields.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value))))
    };
    internal static PluginHttpRequest Post(string url, object body, IReadOnlyDictionary<string, string>? headers = null) => new(url)
    {
        Method = "POST", ContentType = "application/json", Body = JsonSerializer.SerializeToUtf8Bytes(body, new JsonSerializerOptions { MaxDepth = 32 }),
        Headers = headers ?? new Dictionary<string, string>()
    };
    internal static bool SoftFailure(Exception error) => error is HttpRequestException or IOException or JsonException or InvalidOperationException or FormatException or NotSupportedException or ArgumentException;
}
