using System.Text.Json.Serialization;

namespace Winnow.Update;

public enum UpdatePhase { Staged, BackupComplete, Replacing, Installed, MigrationStarted, Ready, RecoveryRequired, Restoring, Restored }

public sealed record UpdateJournal
{
    public string TransactionId { get; init; } = Guid.NewGuid().ToString("N");
    public required string InstallationDirectory { get; init; }
    public required string DataDirectory { get; init; }
    public required string ExecutableName { get; init; }
    public required string Version { get; init; }
    public required string Runtime { get; init; }
    public string[] RestartArguments { get; init; } = [];
    public UpdatePhase Phase { get; set; }
    public bool DatabaseExisted { get; set; }
    public bool MigrationMayHaveStarted { get; set; }
    public string? Failure { get; set; }
    public Dictionary<string, string> PayloadHashes { get; init; } = [];
    public bool DatabaseRestoreStarted { get; set; }
    public bool BackupCompleted { get; set; }
}

[JsonSerializable(typeof(UpdateJournal))]
internal partial class UpdateJsonContext : JsonSerializerContext;
