using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Winnow.Tests.Enforcement;

/// <summary>
/// The check that keeps the source-of-truth migration from undoing itself.
///
/// <para>The documents used to hold a conditional precedence chain — the
/// roadmap superseded part of the design doc and amended another part, the
/// spikes overrode both, and README ranked six files "in precedence order" —
/// and several of them carried superseded text next to its correction. An
/// agent had to reconcile all of that per task and reconciled it
/// inconsistently.</para>
///
/// <para>Four rules replaced it, and this asserts them. One document owns each
/// domain; a wrong section is edited rather than amended, with the sentence it
/// used to say appended to the decisions log; cross-references resolve; and
/// README states no rules.</para>
/// </summary>
public sealed class DocumentationConsistencyTests
{
    /// <summary>
    /// The documents that state rules. These are what the migration produced
    /// and what it has to keep true.
    ///
    /// <para><c>docs/spikes/</c> and <c>docs/code-review-*.md</c> are dated lab
    /// records rather than governing documents: a correction inside one, dated
    /// later than the finding it corrects, is the record working as intended.
    /// They are scanned for cross-reference resolution and nothing else.</para>
    /// </summary>
    private static readonly string[] Governing =
    [
        "AGENTS.md",
        "README.md",
        "ROADMAP.md",
        "game-library-design.md",
        "design-system.md",
        "docs/recommendation-engine.md",
        "docs/facet-provenance.md",
    ];

    /// <summary>
    /// Words that mean a document is carrying its own history. They belong in
    /// <c>docs/decisions.md</c>, which exists so that deleting rationale from a
    /// spec is not deleting it from the repository.
    /// </summary>
    private static readonly string[] AmendmentWords =
    [
        "supersede",
        "superseded",
        "supersedes",
        "amended",
        "the original text",
        "as first written",
    ];

    // ── (a) No document carries its own history ─────────────────────────────

    [Fact]
    public void A_governing_document_states_the_current_rule_and_not_its_history()
    {
        var failures = new List<string>();

        foreach (var doc in Governing.Concat(CharterFiles()))
        {
            var text = RepositoryTree.Read(doc);

            foreach (var word in AmendmentWords)
            {
                foreach (Match m in Regex.Matches(text, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase))
                {
                    if (IsTheRuleItself(doc, text, m.Index))
                    {
                        continue;
                    }

                    failures.Add(
                        $"{doc}:{RepositoryTree.LineAt(text, m.Index)} says \"{m.Value}\". "
                        + "Edit the section to the current truth and append what it used to say "
                        + "to docs/decisions.md.");
                }
            }
        }

        Assert.True(failures.Count == 0, Report(failures));
    }

    /// <summary>
    /// The docs-writer charter states the rule, which means quoting the words
    /// the rule forbids. That one paragraph is the exception, and it is
    /// recognised by the sentence it sits in rather than by file name, so a
    /// second use in the same file still fails.
    /// </summary>
    private static bool IsTheRuleItself(string doc, string text, int offset)
    {
        if (doc != ".claude/agents/docs-writer.md")
        {
            return false;
        }

        var start = Math.Max(0, offset - 400);
        return text.AsSpan(start, offset - start).Contains("belong only in that log", StringComparison.Ordinal)
            || text.AsSpan(offset, Math.Min(400, text.Length - offset)).Contains("belong only in that log", StringComparison.Ordinal);
    }

    // ── (b) Every section cross-reference resolves ──────────────────────────

    [Fact]
    public void Every_section_cross_reference_names_a_heading_that_exists()
    {
        // A reference that names the document it points into is checked: an
        // unresolvable one sends a reader to a section that is not there, which
        // is how a rule stops being findable. Bare references are out of scope;
        // TargetOf says why.
        var headings = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var scanned = Governing
            .Concat(CharterFiles())
            .Concat(RepositoryTree.Files("docs/spikes", "*.md"))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var doc in scanned)
        {
            headings[doc] = HeadingNumbers(RepositoryTree.Read(doc));
        }

        var failures = new List<string>();

        foreach (var doc in scanned)
        {
            var text = RepositoryTree.Read(doc);

            foreach (Match m in Regex.Matches(text, @"§(?<n>\d+(?:\.\d+)*)"))
            {
                var target = TargetOf(text, m.Index);
                if (target is null || !headings.TryGetValue(target, out var available))
                {
                    // A reference into a file this test does not govern. Not a
                    // failure: it is out of scope, not broken.
                    continue;
                }

                // §12.3 into a document whose §12 is a numbered list resolves
                // to §12: it names a place inside a section that exists.
                var section = m.Groups["n"].Value;
                if (Prefixes(section).Any(available.Contains))
                {
                    continue;
                }

                failures.Add(
                    $"{doc}:{RepositoryTree.LineAt(text, m.Index)} points at §{section} "
                    + $"in {target}, which has no such heading.");
            }
        }

        Assert.True(failures.Count == 0, Report(failures));
    }

    /// <summary>A section number and every section it sits inside.</summary>
    private static IEnumerable<string> Prefixes(string section)
    {
        var parts = section.Split('.');
        for (var take = parts.Length; take >= 1; take--)
        {
            yield return string.Join('.', parts.Take(take));
        }
    }

    /// <summary>
    /// Which document a section number refers to, when the reference says so in
    /// place: <c>`other.md` §4.1</c>, or the same across a line break.
    ///
    /// <para>Only qualified references are checked, and that is deliberate. A
    /// bare <c>§6.1</c> means the build spec through most of this corpus and
    /// means this file inside the design system, so a test that guessed which
    /// would be either noisy or vacuous. What this catches is the regression
    /// that actually happens: renumbering a document while another one cites it
    /// by name.</para>
    /// </summary>
    private static string? TargetOf(string text, int offset)
    {
        // The mention has to be adjacent, not merely nearby: only whitespace,
        // punctuation and a connective may sit between the filename and the
        // section number. A window measured in characters picks up the wrong
        // document whenever a paragraph names two, which is common.
        var from = Math.Max(0, offset - 160);
        var run = Regex.Match(
            text[from..offset],
            @"(?<file>[A-Za-z0-9._-]+\.md)(?<gap>[`)\]\s§\d.,]*(?:and|or|to)?[`\s§\d.,]*)$");

        return run.Success ? Resolve(run.Groups["file"].Value) : null;
    }

    /// <summary>
    /// Maps a filename as a document writes it onto a repository-relative path.
    /// Returns null for a file outside the governed set.
    /// </summary>
    private static string? Resolve(string named)
    {
        var candidates = Governing
            .Concat(CharterFiles())
            .Concat(RepositoryTree.Files("docs/spikes", "*.md"));

        return candidates.FirstOrDefault(
            c => c.Equals(named, StringComparison.OrdinalIgnoreCase)
              || c.EndsWith("/" + named, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Every numbered heading in a document, plus every prefix of one. A
    /// document with a `### 4.1` has a §4 whether or not it also writes
    /// `## 4`, because a reference to the parent is still meaningful.
    /// </summary>
    private static HashSet<string> HeadingNumbers(string text)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match m in Regex.Matches(text, @"^#{1,6}\s+(?<n>\d+(?:\.\d+)*)\.?\s", RegexOptions.Multiline))
        {
            var parts = m.Groups["n"].Value.Split('.');
            for (var take = 1; take <= parts.Length; take++)
            {
                found.Add(string.Join('.', parts.Take(take)));
            }
        }

        return found;
    }

    // ── (c) README describes and links; it states no rules ──────────────────

    [Fact]
    public void Readme_states_no_rules()
    {
        // README is the most-read document in the repository and the least
        // maintained, which is how it went on teaching a bucket definition
        // that had been reverted. It describes and links; every normative
        // sentence belongs to a domain document.
        //
        // "Never played" is the shipped name of a bucket, from the copy table,
        // and is not this rule's business.
        var text = RepositoryTree.Read("README.md");
        var failures = new List<string>();

        foreach (var forbidden in new[] { "must not", "non-negotiable", @"\bnever\b" })
        {
            foreach (Match m in Regex.Matches(text, forbidden, RegexOptions.IgnoreCase))
            {
                if (text.AsSpan(m.Index).StartsWith("Never played", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                failures.Add(
                    $"README.md:{RepositoryTree.LineAt(text, m.Index)} says \"{m.Value}\". "
                    + "README describes and links; move the rule to the document that owns it.");
            }
        }

        Assert.True(failures.Count == 0, Report(failures));
    }

    // ── (d) No document claims rank over another ────────────────────────────

    [Fact]
    public void No_document_claims_to_outrank_another()
    {
        // One document owns each domain, and AGENTS.md's table is the whole of
        // the routing. A document that says it is read first, or that it wins,
        // rebuilds the conditional chain this replaced.
        // Phrases that only ever rank documents.
        string[] always =
        [
            "authority document",
            "authority order",
            "in precedence order, ",
            "this document wins",
        ];

        // Words that rank plenty of other things legitimately — a bucket
        // outranks another bucket, Application.Resources outranks
        // Application.Styles — and are a problem only when the thing on either
        // side is a document.
        string[] onlyAboutDocuments = ["outranks", "supersedes", "is read before"];

        var failures = new List<string>();

        foreach (var doc in Governing.Concat(CharterFiles()).Concat(RepositoryTree.Files("docs/spikes", "*.md")))
        {
            var text = RepositoryTree.Read(doc);

            foreach (var claim in always)
            {
                foreach (Match m in Regex.Matches(text, Regex.Escape(claim), RegexOptions.IgnoreCase))
                {
                    failures.Add(Rank(doc, text, m.Index, claim));
                }
            }

            foreach (var claim in onlyAboutDocuments)
            {
                foreach (Match m in Regex.Matches(text, $@"{Regex.Escape(claim)}", RegexOptions.IgnoreCase))
                {
                    var from = Math.Max(0, m.Index - 90);
                    var window = text[from..Math.Min(text.Length, m.Index + 90)];

                    if (Regex.IsMatch(window, @"[A-Za-z0-9._-]+\.md") && !IsDenial(window))
                    {
                        failures.Add(Rank(doc, text, m.Index, claim));
                    }
                }
            }
        }

        Assert.True(failures.Count == 0, Report(failures));
    }

    // ── (e) The decisions log exists, and nothing sends an agent to it ───────

    [Fact]
    public void The_decisions_log_is_write_only_for_agents()
    {
        // The log holds the reasoning removed from the specs. It binds nothing,
        // so no document may send a reader to it for a rule. Naming it as a
        // place reasoning lives is fine; "see decisions.md" for an instruction
        // is not.
        Assert.True(
            File.Exists(RepositoryTree.Path("docs/decisions.md")),
            "docs/decisions.md is missing. It is where rationale removed from a spec goes.");

        var failures = new List<string>();

        foreach (var doc in Governing.Concat(CharterFiles()))
        {
            var text = RepositoryTree.Read(doc);

            foreach (Match m in Regex.Matches(
                text,
                @"(?:see|read|per|according to|refer to)\s+`?docs/decisions\.md",
                RegexOptions.IgnoreCase))
            {
                failures.Add(
                    $"{doc}:{RepositoryTree.LineAt(text, m.Index)} sends a reader to the decisions "
                    + "log. Nothing in it binds; state the rule here instead.");
            }
        }

        Assert.True(failures.Count == 0, Report(failures));
    }

    private static string Rank(string doc, string text, int offset, string claim)
        => $"{doc}:{RepositoryTree.LineAt(text, offset)} says \"{claim}\". "
         + "AGENTS.md names one owner per domain; no document ranks another.";

    /// <summary>
    /// A sentence saying that no document outranks another is the rule, not a
    /// breach of it.
    /// </summary>
    private static bool IsDenial(string window)
        => window.Contains("none of them", StringComparison.OrdinalIgnoreCase)
        || window.Contains("no document", StringComparison.OrdinalIgnoreCase)
        || window.Contains("nor does", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> CharterFiles()
        => RepositoryTree.Files(".claude/agents", "*.md");

    private static string Report(IReadOnlyCollection<string> failures)
    {
        var sb = new StringBuilder()
            .Append(failures.Count)
            .AppendLine(failures.Count == 1 ? " failure:" : " failures:");

        foreach (var f in failures)
        {
            sb.Append("  · ").AppendLine(f);
        }

        return sb.ToString();
    }
}
