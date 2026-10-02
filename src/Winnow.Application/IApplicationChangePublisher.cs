namespace Winnow.Application;

/// <summary>Publish only after commit. Implementations must not block writers on clients.</summary>
public interface IApplicationChangePublisher
{
    void Publish(string kind, string? resource = null);
}

public sealed class ApplicationConflictException(string message) : InvalidOperationException(message);
public sealed class ApplicationNotFoundException(string message) : InvalidOperationException(message);
