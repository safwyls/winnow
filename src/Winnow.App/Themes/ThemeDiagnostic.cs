namespace Winnow.App.Themes;

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
/// One thing wrong with one theme file. Names the file, the field (as a
/// document path like <c>seeds.ground</c>), and what was expected.
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
