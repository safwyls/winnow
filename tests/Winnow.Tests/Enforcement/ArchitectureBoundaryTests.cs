using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace Winnow.Tests.Enforcement;

/// <summary>
/// The module boundaries in <c>game-library-design.md</c> §5.1, asserted on the
/// assemblies the build produced.
///
/// <para>Every rule here is stated in the spec as a "must not", and every one
/// of them is invisible until something has already gone wrong: a reference
/// added to a project file compiles, and the design it breaks is only in a
/// document. These are the ones a reference graph can hold.</para>
/// </summary>
public sealed class ArchitectureBoundaryTests
{
    // ── The core has no dependencies ────────────────────────────────────────

    [Fact]
    public void Winnow_Core_references_nothing_but_the_framework()
    {
        // Core holds the domain records, the repository interfaces and the
        // ingest contract, and every other module depends on it. A dependency
        // added here is a dependency added everywhere, which is why the rule is
        // "BCL only" rather than a list of acceptable packages.
        var offenders = AssemblyFacts.ReferencedAssemblies("Winnow.Core")
            .Where(IsNotFramework)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "Winnow.Core performs no IO and references only the BCL. It now references: "
            + string.Join(", ", offenders));
    }

    // ── The recommender reads through Core and nothing else ─────────────────

    [Fact]
    public void Winnow_Recommend_references_only_Winnow_Core()
    {
        // The scoring module reads through repository interfaces and returns
        // scored results. It never touches ingest, never writes identity, and
        // never calls the UI; a project reference is how each of those would
        // start.
        var offenders = AssemblyFacts.ReferencedAssemblies("Winnow.Recommend")
            .Where(a => a.StartsWith("Winnow", StringComparison.Ordinal))
            .Where(a => a != "Winnow.Core")
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "Winnow.Recommend references Winnow.Core and no other Winnow module. It now also "
            + "references: " + string.Join(", ", offenders));
    }

    [Fact]
    public void The_embedded_sign_in_host_references_only_Winnow_Core()
    {
        // Auth.WebView hosts a browser. It carries no domain logic, so it has
        // no business reaching a repository implementation or an ingest reader.
        var offenders = AssemblyFacts.ReferencedAssemblies("Winnow.Auth.WebView")
            .Where(a => a.StartsWith("Winnow", StringComparison.Ordinal))
            .Where(a => a != "Winnow.Core")
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "Winnow.Auth.WebView references Winnow.Core and no other Winnow module. It now also "
            + "references: " + string.Join(", ", offenders));
    }

    // ── Ingest emits candidates; it does not write identity ─────────────────

    /// <summary>The ingest assemblies, whichever of them the build produced.</summary>
    private static IEnumerable<string> IngestAssemblies =>
        AssemblyFacts.WinnowAssemblies.Where(a => a.StartsWith("Winnow.Ingest.", StringComparison.Ordinal));

    [Fact]
    public void An_ingest_module_references_no_repository_that_writes_identity()
    {
        // Ingest reads a source and emits normalised CandidateOwnership
        // records. Mapping a candidate onto a Work or a Release is the
        // resolver's job, and an ingest module that wrote one directly would
        // bypass the merge queue entirely — which is the failure the whole
        // four-layer model exists to prevent.
        string[] identityWriters =
        [
            "IWorkRepository",
            "IReleaseRepository",
            "IIdentityLinkRepository",
            "WorkRepository",
            "ReleaseRepository",
        ];

        var failures = new List<string>();

        foreach (var assembly in IngestAssemblies)
        {
            var reached = AssemblyFacts.ReferencedTypes(assembly)
                .Where(t => identityWriters.Any(w => t.EndsWith("." + w, StringComparison.Ordinal) || t == w))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            failures.AddRange(reached.Select(t => $"{assembly} references {t}"));
        }

        Assert.True(
            failures.Count == 0,
            "An ingest module emits CandidateOwnership and writes no works or releases "
            + "(game-library-design.md §5.1).\n" + string.Join("\n", failures));
    }

    [Fact]
    public void Every_ingest_module_emits_the_candidate_contract()
    {
        // The other half of the same rule: a module under Winnow.Ingest that
        // does not produce candidates is doing something else, and the boundary
        // table has no row for it.
        foreach (var assembly in IngestAssemblies)
        {
            var touchesTheContract = AssemblyFacts.ReferencedTypes(assembly)
                .Concat(AssemblyFacts.DeclaredTypes(assembly))
                .Any(t => t.Contains("CandidateOwnership", StringComparison.Ordinal));

            Assert.True(
                touchesTheContract,
                $"{assembly} is an ingest module and does not mention CandidateOwnership.");
        }
    }

    // ── Nothing writes to a store's files ───────────────────────────────────

    [Fact]
    public void No_ingest_module_writes_to_a_store_owned_file()
    {
        // Winnow is read-only against every Steam, Epic and GOG file. Steam
        // Cloud can overwrite a local edit with a server-side version, and a
        // launcher database opened read-write leaves -wal and -shm sidecars in
        // the store's own directory.
        //
        // Scanned in source rather than in metadata because the rule is about
        // WHICH file is written, and only the call site says that. A metadata
        // scan reports the assembly and cannot tell the temp copy the GOG
        // reader makes from the database it copied.
        string[] writes =
        [
            "File.Create", "File.CreateText", "File.WriteAllText", "File.WriteAllBytes",
            "File.WriteAllLines", "File.AppendAllText", "File.AppendAllLines", "File.AppendText",
            "File.Delete", "File.Move", "File.Replace",
            "Directory.Delete", "Directory.Move",
            "FileMode.Create", "FileMode.Append", "FileMode.Truncate", "FileMode.OpenOrCreate",
            "FileAccess.Write", "FileAccess.ReadWrite",
        ];

        // The one place an ingest module writes, and it writes to Winnow's own
        // temp directory: galaxy-2.0.db is a WAL database, so `immutable=1`
        // silently returns stale rows and `mode=ro` writes sidecars into GOG's
        // directory. Copying first is the documented way to read it, and the
        // copy has to be cleaned up.
        string[] allowedFiles =
        [
            "src/Winnow.Ingest.Gog/GalaxyDatabaseSnapshot.cs",
        ];

        var failures = new List<string>();

        foreach (var file in RepositoryTree.Files("src", "*.cs")
                     .Where(f => f.StartsWith("src/Winnow.Ingest.", StringComparison.Ordinal))
                     .Where(f => !allowedFiles.Contains(f, StringComparer.Ordinal)))
        {
            var text = RepositoryTree.Read(file);

            foreach (var call in writes)
            {
                foreach (Match m in Regex.Matches(text, $@"(?<![\w.]){Regex.Escape(call)}\b"))
                {
                    if (IsInAComment(text, m.Index))
                    {
                        continue;
                    }

                    failures.Add($"{file}:{RepositoryTree.LineAt(text, m.Index)} uses {call}");
                }
            }
        }

        Assert.True(
            failures.Count == 0,
            "An ingest module is read-only against every store-owned file "
            + "(game-library-design.md §4.1, §4.8). A path that genuinely needs to write writes "
            + "to Winnow's own directory, and its file joins the allowlist with the reason.\n"
            + string.Join("\n", failures));
    }

    // ── VDF is parsed by the library, never by hand ─────────────────────────

    [Fact]
    public void Steam_ingest_parses_key_values_with_ValveKeyValue()
    {
        var referenced = AssemblyFacts.ReferencedAssemblies("Winnow.Ingest.Steam");

        Assert.Contains("ValveKeyValue", referenced);
    }

    [Fact]
    public void No_module_declares_a_hand_rolled_key_value_parser()
    {
        // Both text and binary KeyValues appear in Steam's config tree, and a
        // hand-rolled parser breaks on the binary variants. The failure is not
        // an exception; it is a silently wrong read of somebody's library.
        var failures = new List<string>();

        foreach (var assembly in AssemblyFacts.WinnowAssemblies)
        {
            var suspects = AssemblyFacts.DeclaredTypes(assembly)
                .Where(t => Regex.IsMatch(t, @"(VdfParser|VdfReader|KeyValueParser|KeyValuesParser|AcfParser)$"))
                .ToArray();

            failures.AddRange(suspects.Select(t => $"{assembly} declares {t}"));
        }

        Assert.True(
            failures.Count == 0,
            "VDF, ACF and KeyValues are parsed with ValveKeyValue and never by hand "
            + "(game-library-design.md §4.1).\n" + string.Join("\n", failures));
    }

    // ── The UI reads the database and raises commands ───────────────────────

    [Fact]
    public void No_view_or_view_model_names_an_ingest_or_enrichment_type()
    {
        // The UI reads the database and raises commands. It never calls an
        // ingest or enrichment component directly, because a view model that
        // can start a network fetch is a view model that can block first paint,
        // and first paint is the app's one hard latency promise.
        //
        // The composition root is where those services are constructed and
        // registered, so it is the one place in Winnow.App that names them.
        var failures = new List<string>();

        foreach (var file in RepositoryTree.Files("src/Winnow.App", "*.cs")
                     .Where(f => f.Contains("/Views/", StringComparison.Ordinal)
                              || f.Contains("/ViewModels/", StringComparison.Ordinal)))
        {
            var text = RepositoryTree.Read(file);

            foreach (Match m in Regex.Matches(text, @"\bWinnow\.(?:Ingest|Enrich)\.[A-Za-z0-9_.]+"))
            {
                if (IsInAComment(text, m.Index)
                    || KnownCrossings.Any(k => m.Value.StartsWith(k.Namespace, StringComparison.Ordinal)))
                {
                    continue;
                }

                failures.Add($"{file}:{RepositoryTree.LineAt(text, m.Index)} names {m.Value}");
            }
        }

        Assert.True(
            failures.Count == 0,
            "A view or view model reads the database and raises commands; it does not name an "
            + "ingest or enrichment type (game-library-design.md §5.1).\n"
            + string.Join("\n", failures));
    }

    [Fact]
    public void No_view_model_holds_an_ingest_or_enrichment_dependency()
    {
        // The same rule read off the compiled signatures, which a source scan
        // cannot be talked out of by a using directive: whatever a view model
        // is handed, and whatever it stores, has to appear in its metadata.
        var app = typeof(Winnow.App.ViewModels.LibraryViewModel).Assembly;
        var failures = new List<string>();

        foreach (var type in app.GetTypes())
        {
            var ns = type.Namespace ?? string.Empty;
            if (!ns.Contains(".ViewModels", StringComparison.Ordinal)
                && !ns.Contains(".Views", StringComparison.Ordinal))
            {
                continue;
            }

            var signatureTypes = type
                .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .SelectMany(c => c.GetParameters().Select(p => p.ParameterType))
                .Concat(type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Select(f => f.FieldType))
                .Concat(type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Select(pr => pr.PropertyType));

            foreach (var used in signatureTypes.SelectMany(Unwrap).Distinct())
            {
                var name = used.FullName ?? used.Name;

                var crossesTheLine =
                    name.StartsWith("Winnow.Ingest.", StringComparison.Ordinal)
                    || name.StartsWith("Winnow.Enrich.", StringComparison.Ordinal);

                if (crossesTheLine
                    && !KnownCrossings.Any(k => name.StartsWith(k.Namespace, StringComparison.Ordinal)))
                {
                    failures.Add($"{type.FullName} holds {name}");
                }
            }
        }

        Assert.True(
            failures.Count == 0,
            "A view model is handed repositories and read models, never an ingest reader or an "
            + "enrichment client (game-library-design.md §5.1). Cover leases are how art "
            + "reaches a tile and are not this rule.\n"
            + string.Join("\n", failures.Distinct()));
    }

    /// <summary>
    /// Two namespaces a view model reads that sit in an ingest or enrichment
    /// assembly, with the reason each is not the thing §5.1 forbids.
    ///
    /// <para>Neither is a reader or a client: one is a parse result and one is
    /// a set of credential interfaces the composition root wires. They are
    /// listed rather than waved past, because both would sit more honestly in
    /// <c>Winnow.Core</c> and a list is how that stays visible.</para>
    /// </summary>
    private static readonly (string Namespace, string Why)[] KnownCrossings =
    [
        ("Winnow.Ingest.Steam.AccountPages",
         "AccountStats and its row records are the shape of a parsed page, not a reader. "
         + "The account stats screen renders them."),

        ("Winnow.Enrich.SteamWeb.Credentials",
         "The credential provider interfaces. The Stores screen's job is managing the "
         + "connection, so it reads the state of one; it fetches nothing."),
    ];

    /// <summary>A type and, for a generic, every argument inside it.</summary>
    private static IEnumerable<Type> Unwrap(Type type)
    {
        yield return type;

        if (!type.IsGenericType)
        {
            yield break;
        }

        foreach (var argument in type.GetGenericArguments().SelectMany(Unwrap))
        {
            yield return argument;
        }
    }

    /// <summary>
    /// Whether an offset falls inside a line comment or an XML doc comment.
    /// Both of these rules are stated in the comments around the code that
    /// obeys them, so a scan that counted those would report the explanation as
    /// the violation.
    /// </summary>
    private static bool IsInAComment(string text, int offset)
    {
        var lineStart = text.LastIndexOf('\n', Math.Max(0, offset - 1)) + 1;
        var before = text[lineStart..offset];

        return before.Contains("//", StringComparison.Ordinal)
            || before.TrimStart().StartsWith('*')
            || before.Contains("<!--", StringComparison.Ordinal);
    }

    private static bool IsNotFramework(string assembly)
        => !assembly.StartsWith("System", StringComparison.Ordinal)
        && assembly != "netstandard"
        && assembly != "mscorlib";
}
