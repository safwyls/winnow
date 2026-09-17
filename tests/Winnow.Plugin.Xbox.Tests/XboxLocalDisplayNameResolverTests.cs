using Winnow.Plugin.Xbox;
using Xunit;

namespace Winnow.Plugin.Xbox.Tests;

public sealed class XboxLocalDisplayNameResolverTests
{
    private const string Family = "Microsoft.MicrosoftSolitaireCollection_8wekyb3d8bbwe";
    private const string FullName = "Microsoft.MicrosoftSolitaireCollection_4.26.7290.0_x64__8wekyb3d8bbwe";

    [Theory]
    [InlineData("ms-resource:AppName", "resources/AppName")]
    [InlineData("ms-resource:/ManifestResources/AppName", "ManifestResources/AppName")]
    [InlineData("ms-resource:///resources/AppName", "resources/AppName")]
    [InlineData("ms-resource://Microsoft.MicrosoftSolitaireCollection/resources/AppName", "resources/AppName")]
    public void ManifestResourceReferencesResolveOnlyWithinTheRegisteredPackage(string resource, string qualified)
    {
        var references = XboxLocalDisplayNameResolver.BuildIndirectStrings(FullName, Family, resource);
        Assert.Equal("@{" + FullName + "?ms-resource://Microsoft.MicrosoftSolitaireCollection/" + qualified + "}", references[0]);
    }

    [Theory]
    [InlineData("@C:\\test.dll,-1")]
    [InlineData("ms-resource://OtherApp/resources/AppName")]
    [InlineData("ms-resource:AppName}@other")]
    [InlineData("ms-resource:../AppName")]
    [InlineData("ms-resource:AppName?extra")]
    [InlineData("ms-resource:AppName\r\n")]
    public void RejectsArbitraryFileAndPackageResourceReferences(string resource)
        => Assert.Empty(XboxLocalDisplayNameResolver.BuildIndirectStrings(FullName, Family, resource));

    [Theory]
    [InlineData("OtherApp_1.0.0.0_x64__8wekyb3d8bbwe")]
    [InlineData("C:\\resources.pri")]
    [InlineData("Microsoft.MicrosoftSolitaireCollection_4.26.7290.0_x64__differentid00")]
    public void FullPackageIdentityMustMatchEnumeratedFamily(string fullName)
        => Assert.Empty(XboxLocalDisplayNameResolver.BuildIndirectStrings(fullName, Family, "ms-resource:AppName"));
}
