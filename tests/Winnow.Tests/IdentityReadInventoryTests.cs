using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The architecture test of TASK-70, and the condition the whole link model
/// was accepted on. The design pass said plainly that the honest cost of
/// resolving identity on read is that every user-facing read of works or
/// ownerships must resolve links or show duplicates, and that the
/// discipline must be enforced by a test rather than by memory. It also
/// said that if that inventory could NOT be held by a test, the automatic
/// correctness of the destructive model would be worth more than
/// reversibility and destruction should be kept. This file is that test,
/// and therefore the reason the recommendation stands.
///
/// <para>It scans the source tree for readers of works and ownerships and
/// asserts that every one of them is on the RESOLVE list or on the DO NOT
/// RESOLVE list, with a stated reason. A reader on neither list fails the
/// test by name, file and line, so the person who added it is told where to
/// make the decision rather than merely told that something is wrong.</para>
///
/// <para>The list also fails when it names a reader that no longer exists,
/// so it cannot rot into a list of places that used to matter.</para>
///
/// <para>A reader is: a SQL statement that selects from or joins
/// <c>works</c> or <c>ownerships</c>, or a call to a read member of
/// <c>IWorkRepository</c> or <c>IOwnershipRepository</c>. Writes are not
/// readers and are deliberately out of scope: this inventory is about the
/// READ model.</para>
/// </summary>
public sealed class IdentityReadInventoryTests
{
    /// <summary>The two lists. Every reader is on exactly one.</summary>
    private enum Policy
    {
        /// <summary>
        /// Presents identity as the user sees it, so a linked pair reads as
        /// one game.
        /// </summary>
        Resolve,

        /// <summary>
        /// Reads rows as stored, deliberately, for the reason the entry
        /// states.
        /// </summary>
        DoNotResolve,
    }

    // ── The inventory ───────────────────────────────────────────────────────
    //
    // Keyed by file and enclosing member. Adding a reader means adding a
    // line here and stating which list it is on and why. The reason is the
    // point of the entry: a future reader meets it at the moment they are
    // deciding.
    private static readonly Entry[] Inventory =
    [
        // ── RESOLVE ────────────────────────────────────────────────────────
        new("src/Winnow.Data/Repositories/LibraryQueryRepository.cs", "QueryAsync", Policy.Resolve,
            "The chokepoint. One LEFT JOIN over live same_game links, in the same pass as demo "
            + "consolidation, and every surface it feeds inherits it: the grid, the rail bucket "
            + "counts, All Games, the filter options, list counts, the recommender, the feed and "
            + "the account-visibility count."),

        new("src/Winnow.App/ViewModels/LibraryViewModel.cs", "LoadAsync", Policy.Resolve,
            "The display title and cover. The row keeps its OWN work for everything enrichment "
            + "reads; the user is shown the primary's name and art, so both store entries of one "
            + "game read as one game while the grid is still one tile per ownership."),

        new("src/Winnow.Recommend/RecommendationEngine.cs", "AssemblePoolAsync", Policy.Resolve,
            "Feed suppression. Verdicts are stored per release and widened to the RESOLVED work, "
            + "so dismissing the Steam entry of a linked game suppresses its Epic entry instead of "
            + "offering the same game twice under two badges. The bought-twice signal is keyed the "
            + "same way, which is what the destructive merge used to give it."),

        new("src/Winnow.App/ViewModels/MergeQueueViewModel.cs", "DescribeAsync", Policy.Resolve,
            "Renders the members MergeGrouping produced, and a member IS a resolved work: the "
            + "grouping resolves both ends of every proposal and drops the ones that resolve to "
            + "one work before a card exists."),

        new("src/Winnow.App/ViewModels/MergeQueueViewModel.cs", "DescribeWorkAsync", Policy.Resolve,
            "The same read at the grain of one member. The id it is handed has already been "
            + "resolved by MergeGrouping."),

        // ── DO NOT RESOLVE ─────────────────────────────────────────────────
        new("src/Winnow.Data/Repositories/LibraryQueryRepository.cs", "GetFacetTargetsAsync",
            Policy.DoNotResolve,
            "An enrichment target. Every work still needs enriching on its own ids, and resolving "
            + "here would starve the child of the enrichment whose igdb_id is what fills the "
            + "group."),

        new("src/Winnow.Data/Repositories/WorkRepository.cs", "GetAllAsync", Policy.DoNotResolve,
            "The unresolved catalogue every enrichment pass walks. Resolving it would hide the "
            + "child from the passes that are supposed to reach it."),

        new("src/Winnow.Data/Repositories/WorkRepository.cs", "GetAsync", Policy.DoNotResolve,
            "One row by id, exactly as stored. A caller asking for work 12 is asking about work "
            + "12."),

        new("src/Winnow.Data/Repositories/WorkRepository.cs", "GetEnrichmentTargetsAsync",
            Policy.DoNotResolve,
            "An enrichment target, for GetFacetTargetsAsync's reason."),

        new("src/Winnow.Data/Repositories/WorkRepository.cs", "GetProvisionalNameTargetsAsync",
            Policy.DoNotResolve,
            "A provisional name is a fact about one work's own row, and the pass that clears it "
            + "writes back to that row."),

        new("src/Winnow.Data/Repositories/WorkRepository.cs", "ApplyEnrichmentAsync",
            Policy.DoNotResolve,
            "Reads back the row it is about to write. Resolving would write the parent's row with "
            + "the child's metadata."),

        new("src/Winnow.Data/Repositories/ReleaseRepository.cs", "GetIdentitiesAsync",
            Policy.DoNotResolve,
            "The release-to-work map the resolvers are themselves built on. Resolving here would "
            + "make resolution circular."),

        new("src/Winnow.Data/Repositories/OwnershipRepository.cs", "GetAllAsync",
            Policy.DoNotResolve,
            "Ownership is per store by the four-layer model. A link folds works and never "
            + "ownerships, so this read is the same read it always was."),

        new("src/Winnow.Data/Repositories/OwnershipRepository.cs", "GetAsync", Policy.DoNotResolve,
            "One ownership row by id."),

        new("src/Winnow.Data/Repositories/OwnershipRepository.cs", "GetByReleaseAsync",
            Policy.DoNotResolve,
            "Ownerships of one release. The question is about a release, and releases are not what "
            + "a link folds."),

        new("src/Winnow.Data/Repositories/OwnershipAccountRepository.cs", "GetAccountRefsAsync",
            Policy.DoNotResolve,
            "Which accounts played THIS ownership. Whose copy a game is cannot be answered about a "
            + "group."),

        new("src/Winnow.Data/Repositories/FeedFeedbackRepository.cs", "GetEndorsementsAsync",
            Policy.DoNotResolve,
            "An endorsement records a launch that actually happened on one entry. The widening "
            + "that makes a dismissal cover the group is applied in the recommender, over rows "
            + "the chokepoint already resolved."),

        new("src/Winnow.Data/Repositories/LibraryHistoryStatsRepository.cs", "GetAsync",
            Policy.DoNotResolve,
            "Acquisitions are events. Buying the same game on two stores is two purchases, and a "
            + "year in review that folded them would be reporting something that did not happen."),

        new("src/Winnow.Enrich.Updates/Storage/PollCandidateSource.cs", "GetEligibleAsync",
            Policy.DoNotResolve,
            "A poll target. A Steam build push is not an Epic build push, so both entries stay "
            + "eligible on their own ids."),

        new("src/Winnow.Data/Repositories/IdentityLinkRepository.cs", "AssertWorksExistAsync",
            Policy.DoNotResolve,
            "The link machinery itself. Resolving inside the thing that defines resolution would "
            + "be circular."),

        new("src/Winnow.Data/Repositories/MergeExecutionRepository.cs", "BuildPlanAsync",
            Policy.DoNotResolve,
            "The destructive executor, reachable only from history until TASK-70.7 retires it. It "
            + "moves stored rows and must see them exactly as stored."),

        new("src/Winnow.Data/Repositories/MergeExecutionRepository.cs", "UnifyWorksAsync",
            Policy.DoNotResolve, "The destructive executor. See BuildPlanAsync."),

        new("src/Winnow.Data/Repositories/MergeExecutionRepository.cs", "FoldOwnershipAsync",
            Policy.DoNotResolve, "The destructive executor. See BuildPlanAsync."),

        new("src/Winnow.Data/Repositories/MergeExecutionRepository.cs", "FoldOwnershipsAsync",
            Policy.DoNotResolve, "The destructive executor. See BuildPlanAsync."),

        new("src/Winnow.Data/Repositories/MergeExecutionRepository.cs", "SummariseAsync",
            Policy.DoNotResolve, "The destructive executor. See BuildPlanAsync."),

        new("src/Winnow.Data/Repositories/MergeUndoRepository.cs", "LoadLogAsync",
            Policy.DoNotResolve,
            "Recovers rows a destructive merge deleted, from the journal. Resolution has nothing "
            + "to say about a row that is not there."),

        new("src/Winnow.App/ViewModels/MergeQueueViewModel.cs", "BuildLinkHistoryAsync",
            Policy.DoNotResolve,
            "The link history names each work by ITS OWN name. Resolving would print the parent's "
            + "name on both sides of every row, which is the one place that would make the history "
            + "unreadable."),

        new("src/Winnow.App/Services/EnrichmentSyncService.cs", "EnrichAsync", Policy.DoNotResolve,
            "Enrichment targets the row's own ids. See GetFacetTargetsAsync."),

        new("src/Winnow.App/Services/SampleDataSeeder.cs", "SeedLibraryAsync", Policy.DoNotResolve,
            "Seeds demo rows. A seeder writes the rows resolution is later computed over."),

        new("src/Winnow.App/Services/SteamAccountPageImportService.cs", "BuildIndexAsync",
            Policy.DoNotResolve,
            "Ingest, matching store entries to ownership rows one for one."),

        new("src/Winnow.App/Services/SteamPlaytimeBackfillService.cs", "OwnershipAsync",
            Policy.DoNotResolve,
            "Backfills play records against one ownership. Playtime is measured per store entry, "
            + "and the composite is derived on read."),

        new("src/Winnow.App/Services/SteamPlaytimeBackfillService.cs", "SteamAccountsAsync",
            Policy.DoNotResolve, "Account membership per ownership. See OwnershipAsync."),

        new("src/Winnow.Monitor/GameExecutableIndexBuilder.cs", "BuildAsync", Policy.DoNotResolve,
            "Maps install paths to ownerships so a running process can be attributed. A process "
            + "belongs to the copy that launched it."),

        new("src/Winnow.Resolve/ExternalIdResolver.cs", "PromoteProvisionalNameAsync",
            Policy.DoNotResolve,
            "Names the row's own work from the store entry that was just read."),
    ];

    /// <summary>
    /// Every reader is classified, and an unclassified one fails by name.
    /// </summary>
    [Fact]
    public void Every_reader_of_works_or_ownerships_is_on_the_resolve_or_the_do_not_resolve_list()
    {
        var sites = Scan(SourceRoot);

        Assert.NotEmpty(sites);

        var known = Inventory
            .Select(e => (e.File, e.Member))
            .ToHashSet();

        var unclassified = sites
            .Where(s => !known.Contains((s.File, s.Member)))
            .ToList();

        Assert.True(unclassified.Count == 0, Explain(unclassified));
    }

    /// <summary>
    /// The list cannot rot into places that used to matter.
    /// </summary>
    [Fact]
    public void The_inventory_names_no_reader_that_no_longer_exists()
    {
        var sites = Scan(SourceRoot)
            .Select(s => (s.File, s.Member))
            .ToHashSet();

        var stale = Inventory
            .Where(e => !sites.Contains((e.File, e.Member)))
            .Select(e => $"{e.File} :: {e.Member}")
            .ToList();

        Assert.True(
            stale.Count == 0,
            "The identity read inventory names readers that no longer exist. Remove them, or "
            + "restore the reader:\n  " + string.Join("\n  ", stale));
    }

    /// <summary>
    /// The negative control, and the thing that makes the two tests above
    /// worth having. It proves the scanner actually catches a new reader
    /// instead of merely agreeing with a list that happens to match today,
    /// and it proves the failure NAMES the call site.
    /// </summary>
    [Fact]
    public void A_new_reader_on_neither_list_is_caught_and_named()
    {
        var sandbox = Path.Combine(Path.GetTempPath(), "winnow-readscan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(sandbox, "Winnow.Somewhere"));
        try
        {
            var file = Path.Combine(sandbox, "Winnow.Somewhere", "NewSurface.cs");
            File.WriteAllText(file, """
                namespace Winnow.Somewhere;

                public sealed class NewSurface
                {
                    public string TotalsAsync()
                        => "SELECT COUNT(*) FROM ownerships o JOIN works w ON w.id = o.id;";
                }
                """);

            var sites = Scan(sandbox);

            var site = Assert.Single(sites);
            Assert.Equal("TotalsAsync", site.Member);
            Assert.EndsWith("NewSurface.cs", site.File, StringComparison.Ordinal);

            // The message has to say WHERE, not just that something is wrong.
            var message = Explain([site]);
            Assert.Contains("NewSurface.cs", message, StringComparison.Ordinal);
            Assert.Contains("TotalsAsync", message, StringComparison.Ordinal);
            Assert.Contains("(6)", message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(sandbox, recursive: true);
        }
    }

    /// <summary>
    /// A repository call is caught as well as a SQL string, because the
    /// surface the design worried about is a view model reading works
    /// directly rather than a new query.
    /// </summary>
    [Fact]
    public void A_new_repository_read_is_caught_as_well_as_a_new_query()
    {
        var sandbox = Path.Combine(Path.GetTempPath(), "winnow-readscan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(sandbox, "Winnow.Somewhere"));
        try
        {
            File.WriteAllText(Path.Combine(sandbox, "Winnow.Somewhere", "NewPanel.cs"), """
                namespace Winnow.Somewhere;

                public sealed class NewPanel
                {
                    private readonly IWorkRepository _works;

                    public async Task LoadAsync()
                    {
                        var all = await _works.GetAllAsync();
                    }
                }
                """);

            var site = Assert.Single(Scan(sandbox));
            Assert.Equal("LoadAsync", site.Member);
        }
        finally
        {
            Directory.Delete(sandbox, recursive: true);
        }
    }

    // ── The scanner ─────────────────────────────────────────────────────────

    /// <summary>
    /// A SQL read of works or ownerships. FROM or JOIN only: an INSERT or an
    /// UPDATE is a write and is not what this inventory is about.
    /// </summary>
    private static readonly Regex SqlRead = new(
        @"\b(?:FROM|JOIN)\s+(works|ownerships)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// An identifier declared as one of the two repositories, as a field, a
    /// parameter or a property.
    /// </summary>
    private static readonly Regex RepositoryDeclaration = new(
        @"\bI(?:Work|Ownership)Repository\??\s+(_?[A-Za-z]\w*)",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// A read member. Insert, Update and Delete are writes and are out of
    /// scope by the same rule the SQL pattern applies.
    /// </summary>
    private static readonly Regex ReadCall = new(
        @"\b(?:Get|Find|Count|List|Any|Exists)\w*\s*\(",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// A member declaration, used to name the call site. The nearest one
    /// above the match is the member the reader is in.
    /// </summary>
    private static readonly Regex MemberDeclaration = new(
        @"^\s+(?:\[[^\]]*\]\s*)?(?:public|private|internal|protected)[^;=]*?\b(\w+)\s*(?:<[^>()]*>)?\s*\(",
        RegexOptions.CultureInvariant);

    private static string SourceRoot
    {
        get
        {
            var root = typeof(IdentityReadInventoryTests).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "RepositoryRoot")?.Value;

            Assert.False(
                string.IsNullOrWhiteSpace(root),
                "The test assembly carries no RepositoryRoot metadata, so the inventory cannot be "
                + "checked. See Winnow.Tests.csproj.");

            var source = Path.Combine(root!, "src");
            Assert.True(
                Directory.Exists(source),
                $"The source tree was not found at '{source}', so the inventory cannot be checked.");

            return source;
        }
    }

    private static IReadOnlyList<Site> Scan(string sourceRoot)
    {
        var sites = new Dictionary<(string File, string Member), List<Hit>>();

        foreach (var path in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(Path.GetDirectoryName(sourceRoot)!, path)
                .Replace('\\', '/');

            if (relative.Contains("/bin/", StringComparison.Ordinal)
                || relative.Contains("/obj/", StringComparison.Ordinal))
            {
                continue;
            }

            var lines = File.ReadAllLines(path);

            // The identifiers this file reaches works or ownerships through.
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in lines)
            {
                foreach (Match declaration in RepositoryDeclaration.Matches(line))
                {
                    names.Add(declaration.Groups[1].Value);
                }
            }

            for (var i = 0; i < lines.Length; i++)
            {
                var what = Reader(lines[i], names);
                if (what is null)
                {
                    continue;
                }

                var key = (relative, MemberAt(lines, i));
                if (!sites.TryGetValue(key, out var hits))
                {
                    sites[key] = hits = [];
                }

                hits.Add(new Hit(i + 1, what, lines[i].Trim()));
            }
        }

        return sites
            .Select(pair => new Site(pair.Key.File, pair.Key.Member, pair.Value))
            .OrderBy(s => s.File, StringComparer.Ordinal)
            .ThenBy(s => s.Member, StringComparer.Ordinal)
            .ToList();
    }

    private static string? Reader(string line, HashSet<string> names)
    {
        if (SqlRead.Match(line) is { Success: true } sql)
        {
            return "SQL over " + sql.Groups[1].Value.ToLowerInvariant();
        }

        if (!ReadCall.IsMatch(line))
        {
            return null;
        }

        foreach (var name in names)
        {
            if (Regex.IsMatch(line, @"(?<![\w.])" + Regex.Escape(name) + @"\s*\."))
            {
                return "read through " + name;
            }
        }

        return null;
    }

    private static string MemberAt(string[] lines, int index)
    {
        for (var i = index; i >= 0; i--)
        {
            if (MemberDeclaration.Match(lines[i]) is { Success: true } member)
            {
                return member.Groups[1].Value;
            }
        }

        return "(file)";
    }

    private static string Explain(IReadOnlyList<Site> sites)
    {
        var message = new StringBuilder();
        message.AppendLine(
            "These read works or ownerships and are on neither the RESOLVE list nor the "
            + "DO NOT RESOLVE list. Decide which, and add the reason, in "
            + "IdentityReadInventoryTests.Inventory:");

        foreach (var site in sites)
        {
            foreach (var hit in site.Hits)
            {
                message.AppendLine(
                    $"  {site.File}({hit.Line}) :: {site.Member} — {hit.What} — {hit.Text}");
            }
        }

        return message.ToString();
    }

    private sealed record Entry(string File, string Member, Policy Policy, string Reason);

    private sealed record Site(string File, string Member, IReadOnlyList<Hit> Hits);

    private sealed record Hit(int Line, string What, string Text);
}
