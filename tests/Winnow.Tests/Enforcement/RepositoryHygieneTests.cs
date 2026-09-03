using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Winnow.Tests.Enforcement;

/// <summary>
/// Rules about the shape of the repository itself: the one token file, the
/// deliberate uses of the word "hoard", the build properties every project
/// inherits, and the sanitisation of the captured fixtures.
///
/// <para>Each of these is a rule a document states and nothing else could
/// catch. A second token file, a search-and-replaced noun, a dropped
/// <c>TreatWarningsAsErrors</c> and a real account id in a fixture all fail
/// silently otherwise.</para>
/// </summary>
public sealed class RepositoryHygieneTests
{
    // ── One token file ──────────────────────────────────────────────────────

    [Fact]
    public void Exactly_one_tokens_axaml_exists_and_it_is_the_one_that_compiles()
    {
        // There were two for a long time: a copy at the repository root
        // described as "the design RECORD", and the one the app builds. They
        // diverged, the root file fell 28 keys behind, and every agent that
        // read the wrong one read a stale value. Only the compiling copy
        // exists now.
        var found = RepositoryTree.Files("", "tokens.axaml");

        Assert.True(
            found is ["src/Winnow.App/Themes/tokens.axaml"],
            "Exactly one tokens.axaml may exist, at src/Winnow.App/Themes/. Found: "
            + (found.Count == 0 ? "none" : string.Join(", ", found)));
    }

    // ── The word that is not the product ────────────────────────────────────

    /// <summary>
    /// The four places the common noun is used on purpose. The premise of the
    /// app is <em>winnowing a hoard</em>, so the noun is load-bearing and a
    /// search-and-replace over these is a regression. Quoted here at the
    /// fragment that identifies each, so the test fails if one is edited away.
    /// </summary>
    private static readonly (string File, string Fragment)[] DeliberateHoardSites =
    [
        ("design-system.md", "a library about your own hoard"),
        ("design-system.md", "what a\nhoard of them looks like"),
        ("design-system.md", "look like the whole hoard"),
        ("src/Winnow.App/Views/ActionBarView.axaml", "look like the whole hoard"),
    ];

    [Fact]
    public void The_deliberate_uses_of_hoard_are_still_there()
    {
        var missing = DeliberateHoardSites
            .Where(site => !Normalise(RepositoryTree.Read(site.File))
                .Contains(Normalise(site.Fragment), StringComparison.Ordinal))
            .Select(site => $"{site.File} no longer contains \"{site.Fragment.Replace("\n", " ")}\"")
            .ToArray();

        Assert.True(
            missing.Length == 0,
            "The common noun \"hoard\" is deliberate in these places and a search-and-replace "
            + "over them is a regression. AGENTS.md lists them.\n" + string.Join("\n", missing));
    }

    [Fact]
    public void Hoard_appears_nowhere_else_except_the_compatibility_shims()
    {
        // Everything hyphenated or possessive was the product and is renamed.
        // What is left is the deliberate noun, the three shims that carry an
        // install predating the rename, and the records that explain them.
        // The shims exist to carry the legacy name, so the name is their
        // subject matter rather than a leftover. Each is listed with what it
        // does; deleting one breaks an install that predates the rename.
        string[] allowedFiles =
        [
            "AGENTS.md",                                          // states the rule, so it quotes the sites
            "docs/decisions.md",                                  // holds the rename's history
            "design-system.md",                                   // three deliberate uses of the noun
            "src/Winnow.App/Views/ActionBarView.axaml",           // the fourth
            "src/Winnow.App/Services/WinnowDataLocation.cs",      // moves %LOCALAPPDATA%\Hoard, renames hoard.db
            "src/Winnow.Data/DatabaseInitializer.cs",             // re-points the Hoard.Data.Migrations journal
            "src/Winnow.Data/SqliteDatabaseCheck.cs",             // the hoard.db sidecar rename
        ];

        // Legacy identifiers: the names of things that already exist on a
        // pre-rename install and therefore cannot be renamed away.
        string[] allowedFragments =
        [
            "Hoard.Data",                // the DbUp journal's old resource prefix
            "hoard.db",                  // the old database and its sidecars
            @"LOCALAPPDATA%\Hoard",      // the old data directory
            "Hoard folder",
            "Hoard build",
            "LegacyDefaultId",           // the appearance.theme = hoard alias
            "appearance.theme = hoard",
            "\"hoard\"",
            "renamed from Hoard",
            "Renamed from Hoard",
            "called Hoard",
            "Hoard to Winnow",
            "Upgrading from Hoard",
        ];

        var failures = new List<string>();
        var scan = RepositoryTree.Files("src", "*.cs")
            .Concat(RepositoryTree.Files("src", "*.axaml"))
            .Concat(RepositoryTree.Files("", "*.md").Where(f => !f.StartsWith("docs/plans/", StringComparison.Ordinal)
                                                             && !f.StartsWith("backlog/", StringComparison.Ordinal)
                                                             && !f.StartsWith("docs/spikes/", StringComparison.Ordinal)
                                                             && f != "docs/code-review-2026-08-28.md"
                                                             && f != "docs/stabilization-2026-08-28.md"));

        foreach (var file in scan)
        {
            if (allowedFiles.Contains(file, StringComparer.Ordinal))
            {
                continue;
            }

            var text = RepositoryTree.Read(file);

            foreach (Match m in Regex.Matches(text, @"\bhoard\b", RegexOptions.IgnoreCase))
            {
                var window = text.Substring(
                    Math.Max(0, m.Index - 40),
                    Math.Min(80, text.Length - Math.Max(0, m.Index - 40)));

                if (allowedFragments.Any(a => window.Contains(a, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                failures.Add($"{file}:{RepositoryTree.LineAt(text, m.Index)} — {window.Trim()}");
            }
        }

        Assert.True(
            failures.Count == 0,
            "\"hoard\" outside its deliberate uses and the three compatibility shims:\n"
            + string.Join("\n", failures));
    }

    // ── The properties every project inherits ───────────────────────────────

    [Theory]
    [InlineData("Nullable", "enable")]
    [InlineData("ImplicitUsings", "enable")]
    [InlineData("TreatWarningsAsErrors", "true")]
    public void Directory_build_props_still_sets(string property, string value)
    {
        // The enforcement here is the compiler; this only asserts it is still
        // switched on. Dropping TreatWarningsAsErrors is a one-line change that
        // silently lowers the bar for every project at once.
        var props = RepositoryTree.Read("Directory.Build.props");

        Assert.True(
            Regex.IsMatch(props, $@"<{property}>\s*{Regex.Escape(value)}\s*</{property}>", RegexOptions.IgnoreCase),
            $"Directory.Build.props no longer sets <{property}>{value}</{property}>.");
    }

    // ── Captured fixtures carry no real account id ──────────────────────────

    /// <summary>
    /// The SteamID64 values a fixture may contain, each declared in a fixture
    /// README. Anything else in that shape is an unsanitised capture.
    /// </summary>
    private static readonly string[] KnownFakeSteamIds =
    [
        "76561197972611406",  // tests/fixtures/steam/README.md — the fake LastOwner
        "76561197971376839",  // tests/fixtures/steam-web/README.md — the fake account
        "76561197960265728",  // the universe base constant, not an account
    ];

    [Fact]
    public void No_fixture_carries_an_unsanitised_steam_account_id()
    {
        // The fixtures are real captures from a live install, which is what
        // makes the parser tests worth anything and what makes sanitising them
        // mandatory. A SteamID64 is the identifying value.
        var failures = new List<string>();

        foreach (var file in RepositoryTree.Files("tests/fixtures", "*"))
        {
            string text;
            try
            {
                text = RepositoryTree.Read(file);
            }
            catch (IOException)
            {
                continue;   // a binary capture; nothing to read as text
            }

            foreach (Match m in Regex.Matches(text, @"(?<!\d)7656119\d{10}(?!\d)"))
            {
                if (KnownFakeSteamIds.Contains(m.Value, StringComparer.Ordinal))
                {
                    continue;
                }

                failures.Add(
                    $"{file}:{RepositoryTree.LineAt(text, m.Index)} contains {m.Value}. "
                    + "Sanitize it, and declare the replacement in the fixture README.");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    private static string Normalise(string s)
        => Regex.Replace(s.Replace("\r\n", "\n"), @"\s+", " ");
}
