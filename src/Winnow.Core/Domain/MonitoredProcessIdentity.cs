namespace Winnow.Core.Domain;

/// <summary>OS process identity observed while a sitting was running. Creation time is UTC.</summary>
public sealed record MonitoredProcessIdentity(int ProcessId, DateTime StartedAt, string ProcessName);
