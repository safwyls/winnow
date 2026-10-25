using Winnow.App.Services;

namespace Winnow.Api.Contracts.Connections;

public sealed record BackendOperation(string Id, string Kind, string State, string Message,
    DateTimeOffset UpdatedAt, MetadataSyncResult? MetadataResult = null, PluginInstallResult? PluginResult = null);
public sealed record StartMetadataSync(string OperationId);
public sealed record StartPluginInstall(string OperationId, PluginInstallRequest Request);
public sealed record BackendProgress(int Total, int Remaining);
public sealed record BackendDiagnostics(string ApiVersion, int ProcessId, string RuntimeVersion,
    DateTimeOffset StartedAt, IReadOnlyList<Winnow.Monitor.SessionWatcherFailure> SessionFailures);
