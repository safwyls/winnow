namespace Winnow.Enrich.SteamWeb.Credentials;

public enum SteamSessionHealth
{
    /// <summary>No session is stored. The ordinary state of a fresh install and of every key-only user.</summary>
    NotSignedIn,

    /// <summary>The access token is good and the session is stored encrypted. Nothing to say and nothing to do. A token-only session — one with no refresh token — stays here for its whole life and then goes straight to <see cref="Expired"/>, because there is no renewal for it to be due.</summary>
    Live,

    /// <summary>The access token has expired or is about to, and a refresh token that should be able to replace it is held. Reached only by a session that actually has a refresh token: reporting it for one that does not would name a remedy nothing can apply.</summary>
    RenewalDue,

    /// <summary>Renewal has been attempted and failed. Surfaced promptly, with one-click re-sign-in, per the legibility condition.</summary>
    RenewalFailing,

    /// <summary>The access token is dead and no usable refresh token remains. Only a fresh sign-in recovers this; an API key is unaffected.</summary>
    Expired,

    /// <summary>The session works, but this host cannot encrypt it, so it was never written. It lasts until the process exits. A refusal, never a plaintext fallback.</summary>
    NotPersisted,
}
