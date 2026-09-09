namespace Winnow.App.ViewModels;

/// <summary>
/// One stop on the library's alphabetic jump spine. Empty sections remain in
/// place but are disabled, so the A-Z map never shifts under the pointer.
/// </summary>
public sealed record AlphabetSectionViewModel(string Label, bool IsAvailable)
{
    public string AutomationName => Label == "#"
        ? "Jump to numbers and symbols"
        : $"Jump to {Label}";

    public string Tooltip => $"{AutomationName}. Drag to scrub the alphabet.";
}
