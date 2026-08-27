namespace Hoard.App.Themes;

/// <summary>How badly a theme file is wrong.</summary>
public enum ThemeSeverity
{
    /// <summary>The theme does not load. Something the engine cannot guess at:
    /// a missing seed, an unreadable colour, a schema version this build cannot
    /// read.</summary>
    Error,

    /// <summary>The theme loads and is used. Something an author almost
    /// certainly did not mean — Flare spent twice, a metadata ink under §8's
    /// floor, a field this build does not know.</summary>
    Warning,
}

/// <summary>
/// One thing wrong with one theme file, said the way an author can act on it.
///
/// <para><b>Validation here is a tool, not a gate.</b> A theme that fails is
/// skipped and the app keeps the theme it had; a theme that merely worries us
/// loads anyway. What neither may do is fail silently — an author working
/// against a silent fallback has no way to tell a typo from a taste they
/// disagree with. So every diagnostic names the FILE, the FIELD and what was
/// expected, and the Appearance screen prints them.</para>
///
/// <para><see cref="Field"/> is a path into the document — <c>seeds.ground</c>,
/// <c>structure.edge</c>, <c>overrides.Line</c> — rather than a token name, so
/// it points at the line an author has to edit rather than at the concept the
/// line produces. It is empty for a diagnostic about the file as a whole.</para>
/// </summary>
/// <param name="Severity">Whether the theme still loads.</param>
/// <param name="File">The file's name, not its full path: the folder is stated
/// once on the screen and repeating it on every line buries the part that
/// differs.</param>
/// <param name="Field">Where in the document, as a dotted path. May be empty.</param>
/// <param name="Message">What is wrong and what was expected, in one sentence.</param>
public sealed record ThemeDiagnostic(
    ThemeSeverity Severity,
    string File,
    string Field,
    string Message)
{
    /// <summary>What the Appearance screen prints: the location, then the
    /// sentence. One line, because a list of these is scanned rather than
    /// read.</summary>
    public string Line => string.IsNullOrEmpty(Field)
        ? $"{File} - {Message}"
        : $"{File} › {Field} - {Message}";

    public bool IsError => Severity == ThemeSeverity.Error;
}
