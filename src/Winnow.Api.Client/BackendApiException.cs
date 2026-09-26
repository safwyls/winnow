using System.Net;

namespace Winnow.Api.Client;

public sealed class BackendApiException(HttpStatusCode statusCode, string responseBody)
    : HttpRequestException($"The backend returned HTTP {(int)statusCode}.", null, statusCode)
{
    public string ResponseBody { get; } = responseBody;
}
