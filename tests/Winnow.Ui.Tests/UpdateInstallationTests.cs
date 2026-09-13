using System.Text.Json;
using Winnow.App.Services;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class UpdateInstallationTests
{
    [Theory]
    [InlineData("ID=ubuntu\nVERSION_ID=\"24.04\"", true)]
    [InlineData("ID=ubuntu\nVERSION_ID=22.04", false)]
    [InlineData("ID=debian\nVERSION_ID=24.04", false)]
    [InlineData(null, false)]
    public void LinuxAutomationUsesTheTestedDistribution(string? release, bool supported) =>
        Assert.Equal(supported, UpdateInstallation.IsSupportedUbuntu(release));

    [Theory]
    [InlineData("win-x64", "Winnow.exe", "Winnow.Update.Helper.exe")]
    [InlineData("linux-x64", "Winnow", "Winnow.Update.Helper")]
    public void PortableRequiresMatchingManifestAppHostAndHelper(string runtime, string executable, string helper)
    {
        var directory = Path.Combine(Path.GetTempPath(), "winnow-portable-detection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "update-helper"));
        try
        {
            var app = Path.Combine(directory, executable);
            File.WriteAllText(Path.Combine(directory, "release-info.json"), JsonSerializer.Serialize(new { runtime, version = "1.0.0" }));
            Assert.False(UpdateInstallation.IsPortable(directory, app, runtime, "ID=ubuntu\nVERSION_ID=24.04"));
            File.WriteAllText(Path.Combine(directory, "update-helper", helper), "fixture");
            Assert.True(UpdateInstallation.IsPortable(directory, app, runtime, "ID=ubuntu\nVERSION_ID=24.04"));
            Assert.False(UpdateInstallation.IsPortable(directory, Path.Combine(directory, "dotnet.exe"), runtime, "ID=ubuntu\nVERSION_ID=24.04"));
            File.WriteAllText(Path.Combine(directory, "unins000.exe"), "fixture");
            Assert.False(UpdateInstallation.IsPortable(directory, app, runtime, "ID=ubuntu\nVERSION_ID=24.04"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void ManagedLinuxNeverUsesArchiveReplacement()
    {
        Assert.True(UpdateInstallation.IsManagedLinux("/opt/winnow/"));
        Assert.True(UpdateInstallation.IsManagedLinux("/usr/lib/winnow/"));
        var directory = Path.Combine(Path.GetTempPath(), "winnow-managed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "package-managed"), "deb");
            Assert.True(UpdateInstallation.IsManagedLinux(directory));
        }
        finally { Directory.Delete(directory, true); }
    }
}
