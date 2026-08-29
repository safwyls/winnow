using System.Text.Json;
using System.Text.Json.Serialization;

namespace Winnow.App.Themes;

/// <summary>
/// The shape of a <c>*.json</c> theme file, as System.Text.Json sees it.
/// Read and write are separate types; every member is nullable so
/// <see cref="ThemeJson"/> can distinguish absent from wrong.
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
/// Source-generated JSON contract. Unmapped top-level fields are disallowed
/// (typo detection); map keys are validated in <see cref="ThemeJson"/>.
/// Comments and trailing commas are allowed on read.
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
