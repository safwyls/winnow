namespace Winnow.Api.Contracts.Protocol;

public sealed record BackendDiscovery(string Address, string Token, string Epoch, int ProcessId, string ApiVersion)
{
    public override string ToString() => $"BackendDiscovery({Address}, token redacted, epoch {Epoch})";
}
public sealed record BackendEvent(string Epoch, long Sequence, string Kind, string? Resource, DateTimeOffset OccurredAt)
{
    public string Cursor => $"{Epoch}:{Sequence}";
}
public sealed record BackendHealth(string ApiVersion, string Epoch, long Sequence);
public sealed record BackendCapabilities(string ApiVersion, string[] Capabilities);
