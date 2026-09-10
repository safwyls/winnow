namespace Winnow.Enrich.Igdb.Auth;

/// <summary>Serializes credential persistence with token loading and minting.</summary>
public interface IIgdbCredentialUpdater
{
    /// <summary>
    /// Runs an atomic settings mutation, then forgets runtime credentials and
    /// tokens. A failed mutation leaves the runtime caches unchanged.
    /// </summary>
    Task UpdateCredentialsAsync(Func<Task> update, CancellationToken ct = default);
}
