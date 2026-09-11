namespace Winnow.Core.Domain;

/// <summary>A single source/account attempt. Its revision fences later completion.</summary>
public sealed record OwnershipInventoryAttempt(string Store, string AccountRef, string Source, long Revision);

/// <summary>Source identifiers whose complete inventory can establish absence.</summary>
public static class OwnershipInventorySources
{
    public const string SteamOwnedGames = "steam_web_api";
}
