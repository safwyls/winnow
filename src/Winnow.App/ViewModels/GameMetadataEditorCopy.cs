using Winnow.App.Services;
using Winnow.Core.Queries;
using Winnow.Core.Domain;

namespace Winnow.App.ViewModels;

/// <summary>
/// User-facing copy for the details modal's per-field metadata editor.
/// The disclosure opens from an "Edit details" link in the action band
/// and the field rows draw in the right column's rest band, beside the
/// IGDB reassignment control.
/// </summary>
public static class GameMetadataEditorCopy
{
    /// <summary>Face of the disclosure control, at rest.</summary>
    public const string OpenLabel = "Edit details";

    /// <summary>Face of the disclosure control while the editor is open.</summary>
    public const string CloseLabel = "Close";

    /// <summary>Tooltip on the disclosure control.</summary>
    public const string OpenTooltip = "Edit each field by hand";

    /// <summary>One line at the top of the editor: each field tracks its
    /// own source, so a single edit leaves every other field alone.</summary>
    public const string Intro =
        "Each field tracks its own source, so editing one leaves the rest alone.";

    // ── Field labels and watermarks ────────────────────────────────────

    /// <summary>Display label for a field, keyed on the column name from
    /// <see cref="WorkFields"/>.</summary>
    public static string LabelFor(string field) => field switch
    {
        WorkFields.Name => "Name",
        WorkFields.FirstReleaseYear => "Release year",
        WorkFields.Summary => "About",
        WorkFields.CoverUrl => "Cover art",
        WorkFields.Publisher => "Publisher",
        WorkFields.BackgroundUrl => "Background art",
        _ => field,
    };

    /// <summary>Watermark inside a field's text box. Art fields take a URL,
    /// so their watermark says so.</summary>
    public static string WatermarkFor(string field) => field switch
    {
        WorkFields.Name => "Title",
        WorkFields.FirstReleaseYear => "Four-digit year",
        WorkFields.Summary => "Description",
        WorkFields.CoverUrl => "Image URL",
        WorkFields.Publisher => "Publisher name",
        WorkFields.BackgroundUrl => "Image URL",
        _ => string.Empty,
    };

    /// <summary>Accessible name for a field's text box.</summary>
    public static string FieldAutomationName(string label) => $"{label} field";

    // ── Per-field save ─────────────────────────────────────────────────

    /// <summary>Face of the per-field save button. It saves this field
    /// only.</summary>
    public const string SaveLabel = "Save";

    /// <summary>Accessible name and tooltip for a field's save button.</summary>
    public static string SaveAutomationName(string label) => $"Save {label}";

    // ── Reset to automatic ─────────────────────────────────────────────

    /// <summary>Face of the control that hands one field back to automatic.
    /// Drawn only when the user owns that field. Distinct from
    /// <see cref="GameIgdbMatchCopy.ClearLabel"/>, which drops the IGDB
    /// pin and appears in the same modal.</summary>
    public const string ResetLabel = "Auto";

    /// <summary>Tooltip on the reset control.</summary>
    public const string ResetTooltip = "Hand this field back to automatic";

    /// <summary>Accessible name for a field's reset control.</summary>
    public static string ResetAutomationName(string label) =>
        $"Return {label} to automatic";

    // ── Source badge ───────────────────────────────────────────────────

    /// <summary>Badge on each row, drawn as an outlined chip in the same
    /// idiom as the store chip. One arm per source name, plus the null arm
    /// for the on-automatic state.</summary>
    public static string SourceLabelFor(string? source) => source switch
    {
        FieldSources.User => "YOU",
        FieldSources.Igdb => "IGDB",
        FieldSources.Steam => "STEAM",
        FieldSources.Epic => "EPIC",
        FieldSources.Gog => "GOG",
        null => "AUTO",
        _ => source.ToUpperInvariant(),
    };

    /// <summary>Tooltip for the source badge, one sentence per source.
    /// The user arm states that enrichment will leave the field alone;
    /// the null arm states that nothing has claimed it and enrichment
    /// will fill it.</summary>
    public static string SourceTooltipFor(string? source) => source switch
    {
        FieldSources.User => "Set by you. Enrichment will not change this field.",
        FieldSources.Igdb => "Filled from IGDB metadata.",
        FieldSources.Steam => "Filled from Steam.",
        FieldSources.Epic => "Filled from Epic.",
        FieldSources.Gog => "Filled from GOG.",
        null => "No source has claimed this field. Enrichment will fill it when it can.",
        _ => $"Filled from {source}.",
    };

    // ── Status fields (words, never a spinner) ─────────────────────────

    /// <summary>Status while the work's fields are being read.</summary>
    public const string LoadingStatus = "Loading fields…";

    /// <summary>Status while one text field is being written.</summary>
    public const string SavingStatus = "Saving…";

    /// <summary>Status while one field is being handed back to automatic.</summary>
    public const string ResettingStatus = "Clearing…";

    /// <summary>Status while art is being fetched from a URL and copied
    /// into the cover cache.</summary>
    public const string UrlStatus = "Fetching image…";

    /// <summary>Status while a chosen local file is being copied into the
    /// cover cache.</summary>
    public const string FileStatus = "Copying image…";

    // ── Confirmations and failures ─────────────────────────────────────

    /// <summary>Shown when the editor could not read the work. Amber.</summary>
    public const string LoadFailedText = "Couldn't load this game's fields.";

    /// <summary>Confirmation under one field after it saved, naming
    /// the field by its display label.</summary>
    public static string SavedNote(string label) => $"{label} saved.";

    /// <summary>Confirmation under one field after it was handed back
    /// to automatic.</summary>
    public static string ResetNote(string label) => $"{label} returned to automatic.";

    // ── Art file picker ────────────────────────────────────────────────

    /// <summary>Face of the art rows' file-picker button.</summary>
    public const string ChooseFileLabel = "Choose file";

    /// <summary>Tooltip on the file-picker button.</summary>
    public const string ChooseFileTooltip = "Pick an image from this machine";

    /// <summary>Accessible name for a field's file-picker button.</summary>
    public static string ChooseFileAutomationName(string label) =>
        $"Choose file for {label}";

    /// <summary>Title bar of the OS file dialog.</summary>
    public static string FilePickerTitle(string label) => $"Choose {label}";

    /// <summary>File-type group name in the OS file dialog's filter,
    /// covering JPG, JPEG, PNG, WebP, GIF and BMP.</summary>
    public const string ImageFileTypeLabel = "Images";

    /// <summary>Shown in the art row's empty preview box when the field
    /// holds no art.</summary>
    public const string NoArtText = "No image set";

    // ── Refusal sentences ──────────────────────────────────────────────
    //
    // Every non-Applied arm carries its own distinct sentence. A test
    // asserts they are all different, because each one tells the user a
    // different thing to do. Each is Amber; the controls stay on screen
    // for a retry.

    /// <summary>The sentence for each text-field refusal. Every non-Applied
    /// outcome lands here with its own distinct sentence.</summary>
    public static string ProblemFor(WorkFieldEditOutcome outcome) => outcome switch
    {
        WorkFieldEditOutcome.UnknownField =>
            "That field is not editable. Nothing changed.",
        WorkFieldEditOutcome.WorkNotFound =>
            "This game is no longer in your library. Nothing changed.",
        WorkFieldEditOutcome.InvalidValue =>
            "That value was not accepted. A release year must be a whole number between 1900 and 2200, and a name cannot be blank. Nothing changed.",
        _ => "The change could not be saved.",
    };

    /// <summary>The sentence for each art-field refusal. Every non-Applied
    /// outcome lands here with its own distinct sentence.</summary>
    public static string ProblemFor(WorkArtEditOutcome outcome) => outcome switch
    {
        WorkArtEditOutcome.UnknownField =>
            "That field does not hold art.",
        WorkArtEditOutcome.WorkNotFound =>
            "That game has been removed from your library.",
        WorkArtEditOutcome.FileNotFound =>
            "That file no longer exists.",
        WorkArtEditOutcome.Unreadable =>
            "The file is there but could not be read.",
        WorkArtEditOutcome.TooLarge =>
            "That image is larger than Winnow will store.",
        WorkArtEditOutcome.NotAnImage =>
            "That file is not a recognized image. Accepted formats are JPG, PNG, WebP, GIF and BMP.",
        WorkArtEditOutcome.BadUrl =>
            "Not a usable web address. Enter a URL or choose a file.",
        WorkArtEditOutcome.DownloadFailed =>
            "The address is valid but the image could not be fetched.",
        WorkArtEditOutcome.Failed =>
            "Something went wrong. The art was not changed.",
        _ => "The art could not be saved.",
    };
}
