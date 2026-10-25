using Winnow.Backend;
using Xunit;

namespace Winnow.Update.Tests;

public sealed class InstallationBoundaryTests
{
    [Theory]
    [InlineData("")]
    [InlineData("backend")]
    [InlineData("resources/backend")]
    public void BackendLeaseUsesOnlyRecognizedReleaseLayouts(string relative)
    {
        using var fixture = new DirectoryFixture();
        File.WriteAllText(Path.Combine(fixture.Root, "release-info.json"), "{}");
        var directory = Path.GetFullPath(Path.Combine(fixture.Root, relative));
        Directory.CreateDirectory(directory);
        Assert.Equal(fixture.Root, BackendInstallationLease.FindInstallationDirectory(directory));
    }

    [Theory]
    [InlineData("bin/debug")]
    [InlineData("resources/other")]
    [InlineData("nested/resources/backend")]
    public void BackendLeaseDoesNotCaptureAnUnrelatedAncestorManifest(string relative)
    {
        using var fixture = new DirectoryFixture();
        File.WriteAllText(Path.Combine(fixture.Root, "release-info.json"), "{}");
        var directory = Path.GetFullPath(Path.Combine(fixture.Root, relative));
        Directory.CreateDirectory(directory);
        Assert.Equal(directory, BackendInstallationLease.FindInstallationDirectory(directory));
    }

    [Fact]
    public void SharedInstalledPolicyPreservesTheOriginalPathAndRestartMatrix()
    {
        using var fixture = new DirectoryFixture();
        var executable = Path.Combine(fixture.Root, "Winnow.exe");
        File.WriteAllText(Path.Combine(fixture.Root, "unins000.exe"), "fixture");
        Assert.True(WindowsInstallPolicy.MatchesInstallation(executable, fixture.Root));
        Assert.False(WindowsInstallPolicy.MatchesInstallation(Path.Combine(fixture.Root, "portable", "Winnow.exe"), fixture.Root));
        Assert.False(WindowsInstallPolicy.MatchesInstallation(Path.Combine(fixture.Root, "dotnet.exe"), fixture.Root));
        File.Delete(Path.Combine(fixture.Root, "unins000.exe"));
        Assert.False(WindowsInstallPolicy.MatchesInstallation(executable, fixture.Root));
        Assert.False(WindowsInstallPolicy.MatchesInstallation(null, null));
        Assert.False(WindowsInstallPolicy.MatchesInstallation("", ""));
        Assert.False(WindowsInstallPolicy.MatchesInstallation("Winnow.exe", "relative"));
        var selected = Path.Combine(Path.GetTempPath(), "Winnow selected library");
        Assert.Equal(new[] { "--data-dir", selected, "--no-sync" }, WindowsInstallPolicy.RestartArguments(selected,
            ["--seed-sample", "--epic-login", "--data-dir", "wrong", "--no-sync", "--unknown"]));
    }

    private sealed class DirectoryFixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "winnow-installation-policy-" + Guid.NewGuid().ToString("N"));
        internal DirectoryFixture() => Directory.CreateDirectory(Root);
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
