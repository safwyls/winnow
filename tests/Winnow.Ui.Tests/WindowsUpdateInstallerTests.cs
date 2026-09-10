using Winnow.App.Services;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class WindowsUpdateInstallerTests
{
    [Fact]
    public void RestartPreservesSelectedLibraryAndNoSyncWithoutRepeatingSeedOrLogin()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Winnow selected library");
        Assert.Equal(new[] { "--data-dir", directory, "--no-sync" },
            WindowsUpdateInstaller.RestartArguments(directory,
                new[] { "--seed-sample", "--epic-login", "--data-dir", "wrong", "--no-sync", "--unknown" }));
    }

    [Fact]
    public void PortableExecutableCannotClaimARegisteredInstallation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winnow-install-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "unins000.exe"), "fixture");
            Assert.True(WindowsUpdateInstaller.MatchesInstallation(Path.Combine(directory, "Winnow.exe"), directory));
            Assert.False(WindowsUpdateInstaller.MatchesInstallation(Path.Combine(directory, "portable", "Winnow.exe"), directory));
            Assert.False(WindowsUpdateInstaller.MatchesInstallation(Path.Combine(directory, "dotnet.exe"), directory));
            File.Delete(Path.Combine(directory, "unins000.exe"));
            Assert.False(WindowsUpdateInstaller.MatchesInstallation(Path.Combine(directory, "Winnow.exe"), directory));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("Winnow.exe", "relative")]
    public void MissingInstallRegistrationIsUnsupported(string? executable, string? directory) =>
        Assert.False(WindowsUpdateInstaller.MatchesInstallation(executable, directory));
}
