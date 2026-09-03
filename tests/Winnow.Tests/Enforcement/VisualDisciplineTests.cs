using System.Text.RegularExpressions;
using Xunit;

namespace Winnow.Tests.Enforcement;

/// <summary>
/// The two visual rules that are stated as absolutes and that a markup scan
/// can hold: <c>Flare</c> marks unread updates and nothing else, and every
/// number is set in the mono face with tabular figures.
///
/// <para>Most of the design system is not assertable this way — a claim about
/// what a window <em>reads as</em> is not a number — and the parts that reduce
/// to numbers are already walked by <c>ThemeContrastTests</c> and
/// <c>FloatingLayoutTests</c>. These two are the exceptions, because both are
/// stated as "only" and an "only" is exactly what drifts.</para>
/// </summary>
public sealed class VisualDisciplineTests
{
    // ── Flare is the rarest colour in the app ───────────────────────────────

    /// <summary>
    /// What a <c>Flare</c> brush may be attached to. Each is the unread signal
    /// in one of its three forms: the badge on a tile or card, the pip beside
    /// the bucket that counts them, and the marks on the detail view's gap
    /// rail, which are the same fact plotted in time.
    /// </summary>
    private static readonly string[] UnreadBindings =
    [
        "IsUnread",         // the detail view's per-update pip
        "HasUnread",        // the feed card and the roster row
        "ShowsFlarePip",    // the rail's Patched since bucket
        "MarkBrush",        // the gap rail's marks
        "Unread update",    // the tooltip on a pip with no binding of its own
        "Patched since",    // the same, on the bucket
    ];

    [Fact]
    public void Flare_is_attached_to_the_unread_signal_and_to_nothing_else()
    {
        // The instant Flare becomes a generic accent the badge stops meaning
        // anything, and the product's whole encoding goes with it. This is the
        // one design rule that is both absolute and mechanically checkable.
        var failures = new List<string>();

        foreach (var file in RepositoryTree.Files("src/Winnow.App", "*.axaml"))
        {
            if (file == "src/Winnow.App/Themes/tokens.axaml")
            {
                continue;   // where the brushes are declared, not used
            }

            var text = RepositoryTree.Read(file);

            foreach (Match m in Regex.Matches(text, @"\{StaticResource Flare(?:Soft|Glow)?\}"))
            {
                // The element a brush is set on is a few lines either side of
                // the setter, so the window is the element rather than the line.
                var from = Math.Max(0, m.Index - 700);
                var window = text.Substring(from, Math.Min(1400, text.Length - from));

                if (UnreadBindings.Any(b => window.Contains(b, StringComparison.Ordinal)))
                {
                    continue;
                }

                failures.Add(
                    $"{file}:{RepositoryTree.LineAt(text, m.Index)} paints {m.Value} on an "
                    + "element with no unread binding near it.");
            }
        }

        Assert.True(
            failures.Count == 0,
            "Flare marks unread updates and the bucket that counts them, and nothing else "
            + "(design-system.md §2). If a new surface legitimately carries the unread signal, "
            + "add its binding to UnreadBindings with the reason.\n"
            + string.Join("\n", failures));
    }

    [Fact]
    public void There_is_no_filter_group_for_games_with_updates()
    {
        // A "has updates" group would be a second door onto the rail's
        // Patched since bucket, and a second door needs a second marker — of
        // which there is exactly one, and it is spent.
        var facetKinds = RepositoryTree.Files("src/Winnow.Core", "*.cs")
            .Concat(RepositoryTree.Files("src/Winnow.Data", "*.cs"))
            .Where(f => f.Contains("Facet", StringComparison.Ordinal));

        foreach (var file in facetKinds)
        {
            var text = RepositoryTree.Read(file);

            Assert.DoesNotContain("HasUpdates", text, StringComparison.Ordinal);
            Assert.DoesNotContain("has_updates", text, StringComparison.Ordinal);
        }
    }

    // ── Every number is mono, with tabular figures ──────────────────────────

    [Theory]
    [InlineData("data")]
    [InlineData("data-s")]
    public void The_numeric_text_style_sets_the_mono_face_and_tabular_figures(string styleClass)
    {
        // A playtime column whose digits do not align vertically is unreadable
        // at scan speed, which is the whole reason the app carries a third
        // family. The rule is stated as non-optional in the design system, and
        // the two styles are where it is either true or not.
        var tokens = RepositoryTree.Read("src/Winnow.App/Themes/tokens.axaml");

        var style = Regex.Match(
            tokens,
            $@"<Style\s+Selector=""[^""]*\.{Regex.Escape(styleClass)}"">(?<body>.*?)</Style>",
            RegexOptions.Singleline);

        Assert.True(style.Success, $"tokens.axaml declares no style for the .{styleClass} class.");

        var body = style.Groups["body"].Value;

        Assert.True(
            body.Contains("StaticResource DataFont", StringComparison.Ordinal),
            $"The .{styleClass} style does not set FontFamily to DataFont (IBM Plex Mono).");

        Assert.True(
            Regex.IsMatch(body, @"FontFeatures""\s+Value=""\+?tnum"""),
            $"The .{styleClass} style does not set FontFeatures to tnum.");
    }

    [Fact]
    public void The_mono_face_resolves_to_a_bundled_font()
    {
        // There is no system-font fallback anywhere in the app: a variable TTF
        // renders at its default light instance because Avalonia 11 cannot set
        // fvar axes, so the static cuts are bundled and the styles must reach
        // them by resource URI rather than by family name.
        var tokens = RepositoryTree.Read("src/Winnow.App/Themes/tokens.axaml");

        foreach (var key in new[] { "DisplayFont", "BodyFont", "DataFont" })
        {
            var declared = Regex.Match(tokens, $@"<FontFamily x:Key=""{key}"">(?<uri>[^<]+)</FontFamily>");

            Assert.True(declared.Success, $"tokens.axaml declares no {key}.");
            Assert.StartsWith("avares://Winnow/Assets/Fonts#", declared.Groups["uri"].Value, StringComparison.Ordinal);
        }
    }
}
