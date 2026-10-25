namespace Winnow.App.Services;
/// <summary>
/// The one place a URI leaves this application, behind an interface so the
/// launch path can be tested without a window, a shell, or a store client.
///
/// <para>An interface rather than a static call because M3b's whole test story
/// depends on it: the brief for this milestone forbids actually starting a game,
/// and "did Winnow hand the right URI to the OS, declare the right intent, and
/// recover from a refusal" is exactly what needs proving. A fake dispatcher
/// answers all three without a 60GB download.</para>
/// </summary>
public interface IUriDispatcher
{
    /// <summary>
    /// Hands the URI to the operating system's own handler. Returns false when
    /// the platform declined it; never throws.
    /// </summary>
    Task<bool> OpenAsync(Uri uri);
}
