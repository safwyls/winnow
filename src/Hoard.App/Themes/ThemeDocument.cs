using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hoard.App.Themes;

/// <summary>
/// The shape of a <c>*.json</c> theme file, as System.Text.Json sees it.
///
/// <para><b>Read and write are two types on purpose.</b> The reader has to
/// survive anything on disk, so its numbers and colours arrive as loose maps
/// that <see cref="ThemeJson"/> validates key by key and reports on by name.
/// The writer has to produce a file a person will edit, so it is ordered,
/// omits what it has nothing to say about, and never emits a map with a
/// nullable value in it. Sharing one type between the two would mean the export
/// carried the reader's tolerances into a file we are holding up as an
/// example.</para>
///
/// <para><b>Every member is nullable and nothing here validates.</b> "Absent",
/// "present and wrong" and "present and right" are three different diagnostics,
/// and a DTO that defaulted a missing field could not tell the first two apart.
/// <see cref="ThemeJson"/> owns all three answers.</para>
/// </summary>
internal sealed class ThemeDocument
{
    /// <summary>Which version of this format the file is written to. Required,
    /// checked before anything else is read, and refused rather than
    /// best-guessed when it is not one this build knows — see
    /// <see cref="ThemeJson.SchemaVersion"/>.</summary>
    public int? SchemaVersion { get; set; }

    public string? Id { get; set; }

    public string? Name { get; set; }

    public string? Reason { get; set; }

    /// <summary>The eight colours that are the theme. Required.</summary>
    public Dictionary<string, string>? Seeds { get; set; }

    /// <summary>Proportions for the neutral ramp and the inks. Optional; every
    /// missing one takes the house value.</summary>
    public Dictionary<string, double>? Structure { get; set; }

    /// <summary>The four numbers §14.3's ink compensation is built from.
    /// Optional.</summary>
    public Dictionary<string, double>? Translucency { get; set; }

    /// <summary>What the theme asks the rest of the Appearance screen to be set
    /// to when it is picked. Optional, and never binding.</summary>
    public ThemeDefaultsDocument? Defaults { get; set; }

    /// <summary>Derived colours the author would rather state outright.
    /// Optional, and the reason the format can express a hand-tuned theme at
    /// all.</summary>
    public Dictionary<string, string>? Overrides { get; set; }
}

/// <summary>The <c>defaults</c> block. Strings rather than enums so an unknown
/// value is a diagnostic naming the value, not a deserialisation failure naming
/// a type nobody outside this assembly has heard of.</summary>
internal sealed class ThemeDefaultsDocument
{
    public int? Transparency { get; set; }

    public string? Backdrop { get; set; }

    /// <summary>How far the transparency reaches: <c>chrome</c> or
    /// <c>chrome-and-wall</c>. Named for what the Appearance screen calls the
    /// setting rather than for the boolean it stores.</summary>
    public string? Reach { get; set; }

    public string? Layout { get; set; }
}

/// <summary>The exported template. Ordered as an author reads it: what the file
/// is, then what the theme is, then how it is proportioned, then what it asks
/// for, then the exceptions.</summary>
internal sealed class ThemeExportDocument
{
    public int SchemaVersion { get; set; }

    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public Dictionary<string, string> Seeds { get; set; } = [];

    public Dictionary<string, double> Structure { get; set; } = [];

    public Dictionary<string, double> Translucency { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThemeDefaultsDocument? Defaults { get; set; }

    public Dictionary<string, string> Overrides { get; set; } = [];
}

/// <summary>
/// The source-generated contract, matching the codebase's other two
/// (<c>LibraryFilterJson</c>, <c>SoftMatchJsonContext</c>).
///
/// <para><b><see cref="JsonUnmappedMemberHandling.Disallow"/> is deliberate and
/// it is the one place this format is strict.</b> Inside a version, a top-level
/// field this build does not recognise is a typo — <c>"strucutre"</c> — and the
/// alternative to refusing is a theme that silently ignores a whole block the
/// author is watching for an effect from. Forward compatibility is
/// <c>schemaVersion</c>'s job, not silence's. The KEYS inside the maps are
/// handled the other way round, in <see cref="ThemeJson"/>: an unknown override
/// name is a warning and the rest of the theme still loads, because a map's
/// keys are content rather than structure.</para>
///
/// <para>Comments and trailing commas are allowed on read so the example file
/// shipped into the themes folder can explain itself in place, which is the
/// only documentation an author is guaranteed to find.</para>
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = false,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    WriteIndented = true)]
[JsonSerializable(typeof(ThemeDocument))]
[JsonSerializable(typeof(ThemeExportDocument))]
internal sealed partial class ThemeJsonContext : JsonSerializerContext;
