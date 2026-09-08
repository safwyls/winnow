using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// TASK-152.5. The window's open sequence used to ask for two reads that
/// something else had already paid for: the merge queue, whose rail row carries
/// no count and whose screen costs a full library snapshot plus an expansion
/// scan over every work, and the feed, which every completed library load
/// already invalidates. Both are held here as source shape rather than as
/// behaviour, because what is being asserted is the ABSENCE of a call — and a
/// view model handed a fake repository cannot tell you that the view stopped
/// asking.
/// </summary>
public sealed class StartupReadsOnceTests
{
    private const string WindowCode = "src/Winnow.App/Views/MainWindow.axaml.cs";

    [Fact]
    public void The_window_does_not_build_the_merge_screen_on_open()
    {
        var body = LoadOnOpen();

        // The pane may still be OPENED from here — the --open-queue capture
        // flag does exactly that, through the rail's own command, and the shell
        // then loads the screen because it became visible. What must not come
        // back is a load taken while the pane is hidden.
        Assert.DoesNotMatch(@"MergeQueue[\s\S]{0,120}?LoadCommand", body);
    }

    [Fact]
    public void The_window_scores_the_feed_only_when_no_library_load_will()
    {
        var body = LoadOnOpen();

        // The call may stay, but only behind the one case with nothing to ride
        // on: a shell with no library view model raises no TilesChanged.
        var feedLoad = body.IndexOf("Feed is { } feed", StringComparison.Ordinal);
        Assert.True(feedLoad >= 0, "LoadOnOpenAsync no longer mentions the feed at all.");
        Assert.Contains("_library is null", body[..feedLoad], StringComparison.Ordinal);
    }

    /// <summary>The body of LoadOnOpenAsync, as source text.</summary>
    private static string LoadOnOpen()
    {
        var source = File.ReadAllText(Path(WindowCode));

        var start = source.IndexOf("private async Task LoadOnOpenAsync(", StringComparison.Ordinal);
        Assert.True(start >= 0, "MainWindow.axaml.cs has no LoadOnOpenAsync.");

        // The method's closing brace is the first one back at the class's
        // indentation; every brace inside the method sits deeper.
        var end = Regex.Match(source[start..], @"\r?\n    \}");
        Assert.True(end.Success, "LoadOnOpenAsync does not close where a method closes.");

        return source[start..(start + end.Index + end.Length)];
    }

    private static string Path(string relativePath)
    {
        var root = typeof(StartupReadsOnceTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "RepositoryRoot")?.Value;

        Assert.False(
            string.IsNullOrWhiteSpace(root),
            "The test assembly carries no RepositoryRoot metadata, so the source cannot be read.");

        var path = System.IO.Path.Combine(
            root!, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"The source was not found at '{path}'.");
        return path;
    }
}
