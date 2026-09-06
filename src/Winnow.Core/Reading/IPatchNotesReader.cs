namespace Winnow.Core.Reading;

/// <summary>How an <see cref="IPatchNotesReader.Open"/> call ended.</summary>
public enum PatchNotesOutcome
{
    /// <summary>The panel opened (or navigated an already-open panel) to the address.</summary>
    Opened = 0,

    /// <summary>The address did not pass the entry gate and was not opened.</summary>
    NotANotesPage = 1,

    /// <summary>The reader is not usable on this machine (no WebView2 runtime or no window system).</summary>
    Unavailable = 2,
}

/// <summary>
/// Opens a game's patch notes inside an embedded browser panel rather than the
/// system browser. Core-only contract so no view model names a browser type;
/// the same shape as <see cref="Auth.IInteractiveAuthPrompt"/>.
/// </summary>
public interface IPatchNotesReader
{
    /// <summary>
    /// Whether the reader can run right now. False when there is no WebView2
    /// runtime or no Avalonia application; the caller falls back to the system
    /// browser silently.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Opens (or reuses) the patch-notes panel for <paramref name="url"/>.
    /// Returns <see cref="PatchNotesOutcome.Opened"/> on success, or a reason.
    /// </summary>
    PatchNotesOutcome Open(Uri url, string title);
}
