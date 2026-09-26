using System.Net;
using System.Text.Json;
using Winnow.Api.Contracts.Protocol;

namespace Winnow.Api.Client;

/// <summary>Reads the backend-owned discovery file; it never starts or stops a backend.</summary>
public static class BackendConnection
{
    public static async Task<BackendDiscovery> ReadAsync(string dataDirectory, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(Path.GetFullPath(dataDirectory), "backend", "endpoint.json");
        // Discovery is atomically replaced on restart. Readers must not lock the old name
        // against replacement while another frontend is starting the new backend.
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
        var connection = await JsonSerializer.DeserializeAsync<BackendDiscovery>(stream,
            new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("The backend discovery file is empty.");
        Validate(connection);
        return connection;
    }

    internal static Uri Validate(BackendDiscovery connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (!Uri.TryCreate(connection.Address, UriKind.Absolute, out var address)
            || address.Scheme != Uri.UriSchemeHttp
            || !IPAddress.TryParse(address.Host.Trim('[', ']'), out var ip) || !IPAddress.IsLoopback(ip)
            || address.UserInfo.Length != 0 || address.AbsolutePath != "/"
            || address.Query.Length != 0 || address.Fragment.Length != 0)
            throw new ArgumentException("The backend address must be an HTTP loopback IP origin.", nameof(connection));
        if (string.IsNullOrWhiteSpace(connection.Token) || connection.Token.Any(char.IsWhiteSpace))
            throw new ArgumentException("A backend bearer token is required.", nameof(connection));
        if (connection.ApiVersion != "1")
            throw new NotSupportedException($"Backend API version '{connection.ApiVersion}' is not supported.");
        return address;
    }
}
