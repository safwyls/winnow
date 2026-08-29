using System.Globalization;
using System.Text.Json;
using Avalonia.Media;

namespace Winnow.App.Themes;

/// <summary>
/// Reads a theme file and writes one. Pure string-in, theme-or-diagnostics-out;
/// nothing here opens a file or throws. Failures come back as
/// <see cref="ThemeDiagnostic"/> instances for the Appearance screen.
/// </summary>
public static class ThemeJson
{
    /// <summary>
    /// The version this build reads; an unknown version is refused rather than
    /// best-guessed so a moved field cannot silently produce the wrong theme.
    /// </summary>
    public const int SchemaVersion = 1;

    /// <summary>The largest theme file this will look at. A complete export of
    /// the most override-heavy built-in is under four kilobytes, so this is
    /// three orders of magnitude of headroom and still a bound.</summary>
    public const int MaxFileBytes = 256 * 1024;

    /// <summary>Guards against a document that is mostly nesting. The format is
    /// three levels deep at its worst (<c>root › seeds › value</c>).</summary>
    private const int MaxDepth = 16;

    private const int MaxNameLength = 48;

    private const int MaxReasonLength = 400;

    private const int MaxIdLength = 48;

    /// <summary>Every scalar the format accepts, with its valid range. One
    /// table used for validation, unknown-key diagnostics, and export.</summary>
    private static readonly (string Key, double Min, double Max, bool Structure)[] Scalars =
    [
        ("elevation", 0.005, 0.30, true),
        ("wellDepth", 0.05, 1.00, true),
        ("edge", 1.05, 21.0, true),
        ("dimValue", 0.10, 1.00, true),
        ("dimChroma", 0.00, 3.00, true),
        ("voltInkContrast", 3.00, 21.0, true),
        ("faintValue", 0.05, 1.00, true),
        ("faintChroma", 0.00, 3.00, true),
        ("chromeInk", 0.05, 1.00, false),
        ("groundInk", 0.05, 1.00, false),
        ("dimLift", 0.50, 2.00, false),
        ("faintLift", 0.50, 2.00, false),
    ];

    /// <summary>
    /// Parses one theme file's text.
    /// </summary>
    /// <param name="fileName">The bare file name, for the diagnostics. Never
    /// opened, never resolved.</param>
    /// <param name="text">The file's contents.</param>
    /// <returns>The theme, or <c>null</c> when a diagnostic of severity
    /// <see cref="ThemeSeverity.Error"/> was raised. Warnings never suppress the
    /// theme — it is the author's theme and they may want what they wrote.</returns>
    public static (WinnowTheme? Theme, IReadOnlyList<ThemeDiagnostic> Diagnostics) Parse(
        string fileName, string text)
    {
        var log = new List<ThemeDiagnostic>();

        void Error(string field, string message)
            => log.Add(new ThemeDiagnostic(ThemeSeverity.Error, fileName, field, message));

        void Warn(string field, string message)
            => log.Add(new ThemeDiagnostic(ThemeSeverity.Warning, fileName, field, message));

        if (text.Length == 0)
        {
            Error(string.Empty, "the file is empty.");
            return (null, log);
        }

        ThemeDocument? doc;
        try
        {
            using var json = JsonDocument.Parse(
                text,
                new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                    MaxDepth = MaxDepth,
                });

            if (json.RootElement.ValueKind != JsonValueKind.Object)
            {
                Error(
                    string.Empty,
                    $"the file's top level is a JSON {Describe(json.RootElement.ValueKind)}; a theme is an object, so it has to start with {{.");
                return (null, log);
            }

            doc = json.RootElement.Deserialize(ThemeJsonContext.Default.ThemeDocument);
        }
        catch (JsonException ex)
        {
            Error(Path(ex), Explain(ex));
            return (null, log);
        }

        if (doc is null)
        {
            Error(string.Empty, "the file parsed as JSON null; a theme is an object.");
            return (null, log);
        }

        // ── schemaVersion, before anything else is believed ─────────────────
        if (doc.SchemaVersion is not { } version)
        {
            Error(
                "schemaVersion",
                $"missing. Every theme file has to say which version of the format it is written to; this build reads {SchemaVersion}.");
            return (null, log);
        }

        if (version != SchemaVersion)
        {
            Error(
                "schemaVersion",
                version > SchemaVersion
                    ? $"this file is written to version {version} and this build of Winnow reads version {SchemaVersion}. It is refused rather than read as best it can be, because a field that moved between versions would load at its default and give you a theme you did not write. Update Winnow, or set schemaVersion to {SchemaVersion} and check the fields against the example file."
                    : $"version {version} is not a version of this format. The first one is {SchemaVersion}.");
            return (null, log);
        }

        // ── Identity ────────────────────────────────────────────────────────
        var id = Clean(doc.Id);
        if (id.Length == 0)
        {
            Error("id", "missing. An id is what the setting stores, so it has to survive a rename of the theme; lower case, digits and hyphens.");
        }
        else if (!IsWellFormedId(id))
        {
            Error(
                "id",
                $"\"{id}\" is not a usable id. Expected lower-case letters, digits and hyphens, starting with a letter or digit, at most {MaxIdLength} characters - it is stored in a settings row and shows up in no user-visible place.");
            id = string.Empty;
        }
        else if (WinnowThemes.All.Any(t => string.Equals(t.Id, id, StringComparison.Ordinal)))
        {
            Error(
                "id",
                $"\"{id}\" is the id of a built-in theme, so this file could never be selected. Pick another one.");
            id = string.Empty;
        }

        var name = Clean(doc.Name);
        if (name.Length == 0)
        {
            Error("name", "missing. This is what the Appearance screen calls the theme.");
        }
        else if (name.Length > MaxNameLength)
        {
            Warn("name", $"longer than {MaxNameLength} characters, so the theme card will clip it.");
            name = name[..MaxNameLength];
        }

        var reason = Clean(doc.Reason);
        if (reason.Length == 0)
        {
            Error(
                "reason",
                "missing. Every theme card carries one sentence saying what the theme is for, written for the person choosing - the card looks broken without it.");
        }
        else if (reason.Length > MaxReasonLength)
        {
            Warn("reason", $"longer than {MaxReasonLength} characters; the card will clip it.");
            reason = reason[..MaxReasonLength];
        }

        // ── Seeds ───────────────────────────────────────────────────────────
        var seeds = ReadSeeds(doc.Seeds, Error, Warn);

        // ── Proportions ─────────────────────────────────────────────────────
        var shape = ReadShape(doc.Structure, doc.Translucency, Warn);

        // ── Overrides ───────────────────────────────────────────────────────
        var overrides = ReadOverrides(doc.Overrides, Error, Warn);

        // ── The theme's own opening position ────────────────────────────────
        var defaults = ReadDefaults(doc.Defaults, Warn);

        if (log.Any(d => d.IsError) || seeds is null)
        {
            return (null, log);
        }

        var theme = ThemeDerivation.Compose(
            id, name, reason, seeds, shape, overrides, defaults, fileName);

        log.AddRange(ThemeAudit.Inspect(theme, fileName));
        return (theme, log);
    }

    /// <summary>
    /// Exports a theme as a template: seeds, fitted proportions, and only
    /// those colours the derivation cannot reproduce.
    /// </summary>
    public static string Export(WinnowTheme theme)
    {
        var shape = ThemeDerivation.Fit(theme);
        var residual = ThemeDerivation.ResidualOverrides(theme, shape);
        var seeds = ThemeDerivation.SeedsOf(theme);

        var document = new ThemeExportDocument
        {
            SchemaVersion = SchemaVersion,
            Id = theme.IsUserTheme ? theme.Id : theme.Id + "-copy",
            Name = theme.IsUserTheme ? theme.Name : theme.Name + " (copy)",
            Reason = theme.Reason,
            Seeds = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ground"] = Hex(seeds.Ground),
                ["surface"] = Hex(seeds.Surface),
                ["text"] = Hex(seeds.Text),
                ["flare"] = Hex(seeds.Flare),
                ["volt"] = Hex(seeds.Volt),
                ["amber"] = Hex(seeds.Amber),
                ["azure"] = Hex(seeds.Azure),
                ["danger"] = Hex(seeds.Danger),
            },
            Structure = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["elevation"] = shape.Elevation,
                ["wellDepth"] = shape.WellDepth,
                ["edge"] = shape.Edge,
                ["dimValue"] = shape.DimValue,
                ["dimChroma"] = shape.DimChroma,
                ["voltInkContrast"] = shape.VoltInkContrast,
                ["faintValue"] = shape.FaintValue,
                ["faintChroma"] = shape.FaintChroma,
            },
            Translucency = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["chromeInk"] = shape.ChromeInk,
                ["groundInk"] = shape.GroundInk,
                ["dimLift"] = shape.DimLift,
                ["faintLift"] = shape.FaintLift,
            },
            Defaults = ExportDefaults(theme.Defaults),
            Overrides = ThemeDerivation.DerivedFields
                .Where(residual.ContainsKey)
                .ToDictionary(f => f, f => Hex(residual[f]), StringComparer.Ordinal),
        };

        return JsonSerializer.Serialize(document, ThemeJsonContext.Default.ThemeExportDocument);
    }

    /// <summary>A colour as the format writes it: six upper-case hex digits.
    /// The theme record's fields are all opaque, and the alpha every token needs
    /// is applied by <c>WinnowTheme.Tokens</c> from the transparency setting - so
    /// there is nothing here for an eight-digit form to say.</summary>
    public static string Hex(Color c) =>
        string.Create(CultureInfo.InvariantCulture, $"#{c.R:X2}{c.G:X2}{c.B:X2}");

    /// <summary>
    /// Parses a six-digit hex colour strictly. No CSS names, no eight-digit
    /// alpha form, and failures return a diagnostic rather than throwing.
    /// </summary>
    public static bool TryParseColour(string? value, out Color colour, out string problem)
    {
        colour = default;
        problem = string.Empty;

        var text = value?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            problem = "empty; expected a colour like \"#1D3437\".";
            return false;
        }

        if (text[0] != '#')
        {
            problem = $"\"{Truncate(text)}\" is not a colour. Expected six hex digits behind a #, like \"#1D3437\" - colour names are not read here.";
            return false;
        }

        var digits = text[1..];
        if (digits.Length == 8)
        {
            problem = $"\"{Truncate(text)}\" carries an alpha. Every colour in a theme is opaque; the transparency slider applies alpha to the tokens that take it, so an alpha written here would be dropped. Use the last six digits.";
            return false;
        }

        if (digits.Length is not (3 or 6))
        {
            problem = $"\"{Truncate(text)}\" is {digits.Length} hex digits; expected 6, or 3 for the short form.";
            return false;
        }

        foreach (var ch in digits)
        {
            if (!Uri.IsHexDigit(ch))
            {
                problem = $"\"{Truncate(text)}\" has \"{ch}\" in it, which is not a hex digit.";
                return false;
            }
        }

        if (digits.Length == 3)
        {
            digits = string.Concat(digits[0], digits[0], digits[1], digits[1], digits[2], digits[2]);
        }

        colour = Color.FromRgb(
            byte.Parse(digits[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(digits[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(digits[4..], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        return true;
    }

    private static ThemeSeeds? ReadSeeds(
        Dictionary<string, string>? seeds,
        Action<string, string> error,
        Action<string, string> warn)
    {
        if (seeds is null || seeds.Count == 0)
        {
            error(
                "seeds",
                $"missing. A theme is these eight colours: {string.Join(", ", ThemeDerivation.SeedFields)}. Everything else is derived from them and can be corrected one field at a time in \"overrides\".");
            return null;
        }

        foreach (var key in seeds.Keys)
        {
            if (!ThemeDerivation.SeedFields.Contains(key, StringComparer.Ordinal))
            {
                warn(
                    $"seeds.{key}",
                    ThemeDerivation.DerivedFields.Contains(key, StringComparer.OrdinalIgnoreCase)
                        ? $"\"{key}\" is a derived colour, not a seed, so it is ignored here. Move it to \"overrides\" - the names there are capitalised, as \"{Canonical(key)}\"."
                        : $"\"{key}\" is not one of the eight seeds ({string.Join(", ", ThemeDerivation.SeedFields)}), so it is ignored.");
            }
        }

        var parsed = new Dictionary<string, Color>(StringComparer.Ordinal);
        foreach (var field in ThemeDerivation.SeedFields)
        {
            if (!seeds.TryGetValue(field, out var raw))
            {
                error($"seeds.{field}", "missing. All eight seeds are required; nothing else in the theme can be derived without this one.");
                continue;
            }

            if (TryParseColour(raw, out var colour, out var problem))
            {
                parsed[field] = colour;
            }
            else
            {
                error($"seeds.{field}", problem);
            }
        }

        return parsed.Count == ThemeDerivation.SeedFields.Count
            ? new ThemeSeeds
            {
                Ground = parsed["ground"],
                Surface = parsed["surface"],
                Text = parsed["text"],
                Flare = parsed["flare"],
                Volt = parsed["volt"],
                Amber = parsed["amber"],
                Azure = parsed["azure"],
                Danger = parsed["danger"],
            }
            : null;
    }

    private static ThemeShape ReadShape(
        Dictionary<string, double>? structure,
        Dictionary<string, double>? translucency,
        Action<string, string> warn)
    {
        var values = new Dictionary<string, double>(StringComparer.Ordinal);

        Read(structure, "structure", true);
        Read(translucency, "translucency", false);

        var d = ThemeShape.Default;
        return new ThemeShape
        {
            Elevation = Get("elevation", d.Elevation),
            WellDepth = Get("wellDepth", d.WellDepth),
            Edge = Get("edge", d.Edge),
            DimValue = Get("dimValue", d.DimValue),
            DimChroma = Get("dimChroma", d.DimChroma),
            VoltInkContrast = Get("voltInkContrast", d.VoltInkContrast),
            FaintValue = Get("faintValue", d.FaintValue),
            FaintChroma = Get("faintChroma", d.FaintChroma),
            ChromeInk = Get("chromeInk", d.ChromeInk),
            GroundInk = Get("groundInk", d.GroundInk),
            DimLift = Get("dimLift", d.DimLift),
            FaintLift = Get("faintLift", d.FaintLift),
        };

        double Get(string key, double fallback) => values.TryGetValue(key, out var v) ? v : fallback;

        void Read(Dictionary<string, double>? block, string blockName, bool structural)
        {
            if (block is null)
            {
                return;
            }

            foreach (var (key, value) in block)
            {
                var known = Scalars.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.Ordinal));
                if (known.Key is null)
                {
                    var elsewhere = Scalars.FirstOrDefault(
                        s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));
                    warn(
                        $"{blockName}.{key}",
                        elsewhere.Key is null
                            ? $"not a field this build reads, so it is ignored. \"{blockName}\" takes: {string.Join(", ", Scalars.Where(s => s.Structure == structural).Select(s => s.Key))}."
                            : $"belongs in \"{(elsewhere.Structure ? "structure" : "translucency")}\", spelled \"{elsewhere.Key}\". Ignored here.");
                    continue;
                }

                if (known.Structure != structural)
                {
                    warn(
                        $"{blockName}.{key}",
                        $"belongs in \"{(known.Structure ? "structure" : "translucency")}\". Read anyway, but move it - the two blocks are separated so the four numbers §14.3's ink compensation is made of stay together.");
                }

                if (double.IsNaN(value) || double.IsInfinity(value))
                {
                    warn($"{blockName}.{key}", "is not a finite number; ignored.");
                    continue;
                }

                if (value < known.Min || value > known.Max)
                {
                    var clamped = Math.Clamp(value, known.Min, known.Max);
                    warn(
                        $"{blockName}.{key}",
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"{value:0.###} is outside {known.Min:0.###} to {known.Max:0.###}; clamped to {clamped:0.###}."));
                    values[known.Key] = clamped;
                    continue;
                }

                values[known.Key] = value;
            }
        }
    }

    private static Dictionary<string, Color> ReadOverrides(
        Dictionary<string, string>? overrides,
        Action<string, string> error,
        Action<string, string> warn)
    {
        var parsed = new Dictionary<string, Color>(StringComparer.Ordinal);
        if (overrides is null)
        {
            return parsed;
        }

        foreach (var (key, raw) in overrides)
        {
            if (!ThemeDerivation.DerivedFields.Contains(key, StringComparer.Ordinal))
            {
                // A seed named here is the one confusable mistake worth being
                // firm about: it looks like it worked, and the theme would come
                // out built on a different colour from the one the author is
                // reading in their own file.
                var seed = ThemeDerivation.SeedFields.FirstOrDefault(
                    s => string.Equals(s, key, StringComparison.OrdinalIgnoreCase));
                if (seed is not null)
                {
                    error(
                        $"overrides.{key}",
                        $"\"{key}\" is a seed, not a derived colour. Set it in \"seeds\" as \"{seed}\"; an override here would be read by nothing.");
                    continue;
                }

                var near = ThemeDerivation.DerivedFields.FirstOrDefault(
                    f => string.Equals(f, key, StringComparison.OrdinalIgnoreCase));
                warn(
                    $"overrides.{key}",
                    near is null
                        ? $"\"{key}\" is not a colour this build has, so it is ignored. The overridable ones are: {string.Join(", ", ThemeDerivation.DerivedFields)}."
                        : $"spelled \"{near}\" - the names are case-sensitive. Ignored.");
                continue;
            }

            if (TryParseColour(raw, out var colour, out var problem))
            {
                parsed[key] = colour;
            }
            else
            {
                error($"overrides.{key}", problem);
            }
        }

        return parsed;
    }

    private static ThemeAppearanceDefaults? ReadDefaults(
        ThemeDefaultsDocument? defaults, Action<string, string> warn)
    {
        if (defaults is null)
        {
            return null;
        }

        int? transparency = null;
        if (defaults.Transparency is { } percent)
        {
            if (percent is < 0 or > 100)
            {
                var clamped = Math.Clamp(percent, 0, 100);
                warn("defaults.transparency", $"{percent} is outside 0 to 100; clamped to {clamped}.");
                transparency = clamped;
            }
            else
            {
                transparency = percent;
            }
        }

        WinnowBackdrop? backdrop = null;
        if (Clean(defaults.Backdrop) is { Length: > 0 } backdropId)
        {
            if (backdropId is "acrylic" or "mica")
            {
                backdrop = WinnowBackdrops.ById(backdropId);
            }
            else
            {
                warn(
                    "defaults.backdrop",
                    $"\"{backdropId}\" is not a backdrop. Expected \"acrylic\" or \"mica\" - \"none\" is what the platform reports when it refused both, never something to ask for. Ignored.");
            }
        }

        bool? wall = null;
        if (Clean(defaults.Reach) is { Length: > 0 } reach)
        {
            wall = reach switch
            {
                "chrome" => false,
                "chrome-and-wall" => true,
                _ => null,
            };

            if (wall is null)
            {
                warn(
                    "defaults.reach",
                    $"\"{reach}\" is not a reach. Expected \"chrome\" or \"chrome-and-wall\". Ignored.");
            }
        }

        WinnowLayout? layout = null;
        if (Clean(defaults.Layout) is { Length: > 0 } layoutId)
        {
            if (layoutId is "flush" or "floating")
            {
                layout = WinnowLayouts.ById(layoutId);
            }
            else
            {
                warn(
                    "defaults.layout",
                    $"\"{layoutId}\" is not a layout. Expected \"flush\" or \"floating\". Ignored.");
            }
        }

        var result = new ThemeAppearanceDefaults
        {
            Transparency = transparency,
            Backdrop = backdrop,
            WallTranslucent = wall,
            Layout = layout,
        };

        return result.IsEmpty ? null : result;
    }

    private static ThemeDefaultsDocument? ExportDefaults(ThemeAppearanceDefaults? defaults)
    {
        if (defaults is null || defaults.IsEmpty)
        {
            return null;
        }

        return new ThemeDefaultsDocument
        {
            Transparency = defaults.Transparency,
            Backdrop = defaults.Backdrop is { } b ? WinnowBackdrops.Id(b) : null,
            Reach = defaults.WallTranslucent switch
            {
                true => "chrome-and-wall",
                false => "chrome",
                null => null,
            },
            Layout = defaults.Layout is { } l ? WinnowLayouts.Id(l) : null,
        };
    }

    /// <summary>The capitalised form of a derived field, for the "you put this
    /// in the wrong block" diagnostics.</summary>
    private static string Canonical(string key)
        => ThemeDerivation.DerivedFields.FirstOrDefault(
            f => string.Equals(f, key, StringComparison.OrdinalIgnoreCase)) ?? key;

    /// <summary>
    /// Trims, and drops control characters.
    ///
    /// <para>Not sanitising for safety — the strings go into a TextBlock and
    /// nothing interprets them — but for legibility: a stray newline inside a
    /// theme's name silently makes the card two lines tall and there would be
    /// nothing on screen to explain why.</para>
    /// </summary>
    private static string Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.Any(char.IsControl)
            ? new string([.. trimmed.Where(c => !char.IsControl(c))]).Trim()
            : trimmed;
    }

    private static bool IsWellFormedId(string id)
    {
        if (id.Length is 0 or > MaxIdLength)
        {
            return false;
        }

        if (!char.IsAsciiLetterLower(id[0]) && !char.IsAsciiDigit(id[0]))
        {
            return false;
        }

        return id.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-');
    }

    /// <summary>The document path System.Text.Json reports, as the format's own
    /// field name: <c>$.seeds.ground</c> is <c>seeds.ground</c> to an author
    /// looking at their file.</summary>
    private static string Path(JsonException ex)
    {
        var path = ex.Path ?? string.Empty;
        return path.StartsWith("$.", StringComparison.Ordinal) ? path[2..]
            : path == "$" ? string.Empty
            : path;
    }

    /// <summary>
    /// A JSON exception said the way an author can act on it.
    ///
    /// <para>The framework's own message for an unmapped member names a .NET
    /// type nobody outside this assembly has heard of, and its message for a
    /// type mismatch quotes a CLR type name. Both are rewritten; the line and
    /// position it reports are kept, because those are the useful half.</para>
    /// </summary>
    private static string Explain(JsonException ex)
    {
        var where = ex.LineNumber is { } line
            ? string.Create(CultureInfo.InvariantCulture, $" (line {line + 1})")
            : string.Empty;

        var message = ex.Message ?? string.Empty;

        if (message.Contains("could not be mapped", StringComparison.Ordinal))
        {
            return $"not a field this build reads{where}. A theme file holds: schemaVersion, id, name, reason, seeds, structure, translucency, defaults, overrides.";
        }

        if (message.Contains("could not be converted", StringComparison.Ordinal)
            || message.Contains("Cannot get the value", StringComparison.Ordinal))
        {
            return $"the wrong kind of value{where}. Colours are strings like \"#1D3437\", proportions are plain numbers, schemaVersion is a whole number.";
        }

        return $"the file is not valid JSON{where}. {message}";
    }

    private static string Describe(JsonValueKind kind) => kind switch
    {
        JsonValueKind.Array => "array",
        JsonValueKind.String => "string",
        JsonValueKind.Number => "number",
        JsonValueKind.True or JsonValueKind.False => "boolean",
        JsonValueKind.Null => "null",
        _ => "value",
    };

    private static string Truncate(string value)
        => value.Length <= 24 ? value : value[..24] + "...";
}
