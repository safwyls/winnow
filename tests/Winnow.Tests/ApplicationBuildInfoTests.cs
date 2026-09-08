using Winnow.App.Services;
using Winnow.App.ViewModels;
using Xunit;

namespace Winnow.Tests;

public sealed class ApplicationBuildInfoTests
{
    [Theory]
    [InlineData("0.1.0-beta.1+abc123", "0.1.0-beta.1", "abc123")]
    [InlineData("0.1.0-ci.42+abc123", "0.1.0-ci.42", "abc123")]
    [InlineData("0.1.0-dev", "0.1.0-dev", "Unavailable")]
    [InlineData("1.2.3", "1.2.3", "Unavailable")]
    [InlineData(null, "Unknown", "Unavailable")]
    public void Preserves_release_identity_and_handles_builds_without_git(string? input, string version, string commit)
    {
        var info = ApplicationBuildInfo.FromInformationalVersion(input);
        Assert.Equal(version, info.Version);
        Assert.Equal(commit, info.Commit);
    }

    [Fact]
    public void Settings_reads_the_application_assembly_identity()
    {
        var settings = new ApplicationSettingsViewModel();
        Assert.Equal(ApplicationBuildInfo.Current.Version, settings.ApplicationVersion);
        Assert.Equal(ApplicationBuildInfo.Current.Commit, settings.BuildCommit);
        Assert.NotEqual("Unknown", settings.ApplicationVersion);
    }
}
