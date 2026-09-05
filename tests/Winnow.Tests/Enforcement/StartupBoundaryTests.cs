using System.Text.RegularExpressions;
using Xunit;

namespace Winnow.Tests.Enforcement;

/// <summary>
/// TASK-22 (F36) and TASK-84 (N02), as a standing rule rather than a fix that
/// held once.
///
/// <para>Every path that runs before the user has a window is a path with no
/// handler above it, and Winnow is a <c>WinExe</c>: an exception escaping one
/// of them ends the process with nothing written anywhere a user can look.
/// Each boundary below is a <c>try</c> somebody added on purpose and a later
/// refactor would not notice removing, which is exactly the shape a source
/// rule holds and a unit test cannot.</para>
/// </summary>
public sealed class StartupBoundaryTests
{
    /// <summary>
    /// A lifecycle override that is <c>async void</c> has no caller to return a
    /// fault to: the framework raised it and awaits nothing, so an exception
    /// resumes on the dispatcher with nowhere to go and takes the process down.
    /// It is the one method shape in the app where an unguarded body is a crash
    /// by construction, so every one of them carries its own boundary.
    /// </summary>
    [Fact]
    public void Every_async_void_lifecycle_override_carries_an_error_boundary()
    {
        var failures = new List<string>();
        var found = 0;

        foreach (var file in RepositoryTree.Files("src/Winnow.App", "*.cs"))
        {
            var text = RepositoryTree.Read(file);

            foreach (Match m in Regex.Matches(
                text,
                @"protected\s+override\s+async\s+void\s+(?<name>\w+)\s*\([^)]*\)"))
            {
                found++;
                if (!HasCatchInBody(text, m.Index + m.Length))
                {
                    failures.Add(
                        $"{file}:{RepositoryTree.LineAt(text, m.Index)} {m.Groups["name"].Value}");
                }
            }
        }

        // A source rule that matches nothing passes for the wrong reason, and
        // would go on passing after a rename broke the pattern. MainWindow's
        // OnOpened is the one the rule was written for, so the scan has to keep
        // finding at least it.
        Assert.True(
            found > 0,
            "The scan found no async void lifecycle overrides at all, so it is asserting nothing. "
            + "MainWindow.OnOpened is one; the pattern has stopped matching.");

        Assert.True(
            failures.Count == 0,
            "An async void lifecycle override has no caller to fault to, so an exception from one "
            + "is an unhandled dispatcher exception and a dead process. Each of these needs a "
            + "try/catch around its body, as MainWindow.OnOpened has: "
            + string.Join(", ", failures));
    }

    /// <summary>
    /// The synchronous startup spine, which runs before Avalonia has raised
    /// anything at all: migrations, hosted services, framework initialization,
    /// the theme read and the composition root. Named individually because
    /// there are exactly three of them and each one is a decision.
    /// </summary>
    [Theory]
    [InlineData("src/Winnow.App/Program.cs", "StartupFailure.Report")]
    [InlineData("src/Winnow.App/Views/MainWindow.axaml.cs", "LogStartupLoadFailure")]
    [InlineData("src/Winnow.App/App.axaml.cs", "theme.LoadAsync")]
    public void The_startup_spine_is_bounded(string file, string marker)
    {
        var text = RepositoryTree.Read(file);

        Assert.True(
            text.Contains(marker, StringComparison.Ordinal),
            $"{file} no longer carries its startup error boundary ({marker}). Startup runs with no "
            + "handler above it in a WinExe: without this, a failure here is a process that dies "
            + "showing nothing.");
    }

    /// <summary>
    /// Whether the method body starting at <paramref name="afterSignature"/>
    /// contains a <c>catch</c>. Brace-counted rather than regexed over the whole
    /// file, so a boundary in the NEXT method does not answer for this one.
    /// </summary>
    private static bool HasCatchInBody(string text, int afterSignature)
    {
        var open = text.IndexOf('{', afterSignature);
        if (open < 0)
        {
            return false;
        }

        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '{')
            {
                depth++;
            }
            else if (text[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return text.AsSpan(open, i - open).Contains("catch", StringComparison.Ordinal);
                }
            }
        }

        return false;
    }
}
