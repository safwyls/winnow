using System.Text.Json.Serialization;

namespace Winnow.Api.Contracts.Actions;

[JsonConverter(typeof(JsonStringEnumConverter<GameActionKind>))]
public enum GameActionKind { Play, Install, Uninstall, Manage, OpenStore }

public sealed record GameActionRequest(Guid OperationId, GameActionKind Action);
