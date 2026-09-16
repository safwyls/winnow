namespace Winnow.App.Themes;

/// <summary>Font family names and a shared size multiplier, portable with a theme.</summary>
public sealed record ThemeTypography
{
    public const int MinSizePercent = 80;
    public const int MaxSizePercent = 120;
    public static ThemeTypography Default { get; } = new();

    public string HeadingFont { get; init; } = "Bricolage Grotesque";
    public string InterfaceFont { get; init; } = "Plus Jakarta Sans";
    public string DataFont { get; init; } = "IBM Plex Mono";
    public int SizePercent { get; init; } = 100;

    public bool IsValid() => IsValidFamily(HeadingFont) && IsValidFamily(InterfaceFont)
        && IsValidFamily(DataFont) && SizePercent is >= MinSizePercent and <= MaxSizePercent;

    // Family names cannot introduce Avalonia font asset URIs or fallback lists.
    public static bool IsValidFamily(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= 128
            && value == value.Trim()
            && !value.Any(c => char.IsControl(c) || c is ':' or '/' or '\\' or '#' or ',' or ';');
}
