using System.Reflection;

namespace Winnow.Tests.Enforcement;

/// <summary>
/// Locates the checked-out repository from inside the test assembly.
///
/// <para>Walking up from the test binary does not work here: the documented
/// way to build while the app is running is an artifacts path outside the
/// repository, so the assembly does not sit under the tree it is scanning.
/// MSBuild stamps the root in as assembly metadata, which is the only thing
/// that knows where it is at run time. See <c>Winnow.Tests.csproj</c>.</para>
/// </summary>
internal static class RepositoryTree
{
    private static readonly Lazy<string> RootPath = new(() =>
    {
        var root = typeof(RepositoryTree).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "RepositoryRoot")?.Value;

        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            throw new InvalidOperationException(
                "The test assembly carries no usable RepositoryRoot metadata, so the "
                + "repository cannot be scanned. See Winnow.Tests.csproj.");
        }

        return root;
    });

    /// <summary>The repository root.</summary>
    internal static string Root => RootPath.Value;

    /// <summary>An absolute path to <paramref name="relative"/> under the root.</summary>
    internal static string Path(string relative)
        => System.IO.Path.Combine(Root, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));

    /// <summary>
    /// Every file under <paramref name="relative"/> matching
    /// <paramref name="pattern"/>, excluding build output and staged publish trees. Paths come back
    /// repository-relative with forward slashes, so a failure message reads the
    /// same on any machine.
    /// </summary>
    internal static IReadOnlyList<string> Files(string relative, string pattern)
    {
        var start = Path(relative);
        if (!Directory.Exists(start))
        {
            return [];
        }

        var files = new List<string>();
        var directories = new Stack<string>();
        directories.Push(start);
        while (directories.TryPop(out var directory))
        {
            if (ExcludedDirectory(Relative(directory))) continue;
            files.AddRange(Directory.EnumerateFiles(directory, pattern).Select(Relative));
            foreach (var child in Directory.EnumerateDirectories(directory))
                directories.Push(child);
        }
        return [.. files.OrderBy(p => p, StringComparer.Ordinal)];
    }

    private static bool ExcludedDirectory(string relative)
    {
        // Prune generated trees before traversal: isolated backend data may deny
        // directory access, and dependencies/published copies are not source.
        if (relative.Split('/').Any(part => part is "bin" or "obj")) return true;
        string[] generatedRoots =
        [
            ".git", ".tmp", "artifacts", "TestResults", "ci-evidence", "gdpr",
            "src/Winnow.Electron/node_modules", "src/Winnow.Electron/out",
            "src/Winnow.Electron/release", "src/Winnow.Electron/coverage", "src/Winnow.Electron/.staging",
        ];
        return generatedRoots.Any(root => relative == root || relative.StartsWith(root + "/", StringComparison.Ordinal));
    }

    /// <summary>Reads a repository-relative path.</summary>
    internal static string Read(string relative) => File.ReadAllText(Path(relative));

    /// <summary>Turns an absolute path into a repository-relative one.</summary>
    internal static string Relative(string absolute)
        => System.IO.Path.GetRelativePath(Root, absolute).Replace('\\', '/');

    /// <summary>
    /// The line number a character offset falls on, 1-based, so a failure can
    /// name <c>file:line</c> rather than an offset nobody can navigate to.
    /// </summary>
    internal static int LineAt(string text, int offset)
        => text.AsSpan(0, offset).Count('\n') + 1;
}
