using System.Security.AccessControl;
using System.Security.Principal;
using Xunit;

namespace Winnow.Backend.Tests;

public sealed class BackendOwnershipTests
{
    [Fact]
    public void OwnerCanSecureModifyOnlyDataDirectoryWithoutTakeOwnershipPermission()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "winnow-backend-tests", Guid.NewGuid().ToString("N"));
        var discovery = Directory.CreateDirectory(Path.Combine(root, "backend"));
        try
        {
            using var current = WindowsIdentity.GetCurrent();
            var sid = current.User!;
            // Elevated runners can create directories owned by Administrators.
            // This contract concerns a user-owned directory without WRITE_OWNER.
            var owned = discovery.GetAccessControl();
            owned.SetOwner(sid);
            discovery.SetAccessControl(owned);
            Assert.Equal(sid, discovery.GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier)));
            var permissions = new DirectorySecurity();
            permissions.SetAccessRuleProtection(true, false);
            permissions.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.Modify,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            discovery.SetAccessControl(permissions);
            using (var ownership = new BackendOwnership(root))
            {
                var secured = discovery.GetAccessControl();
                Assert.Equal(sid, secured.GetOwner(typeof(SecurityIdentifier)));
                Assert.True(secured.AreAccessRulesProtected);
                var rule = Assert.Single(secured.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>());
                Assert.Equal(sid, rule.IdentityReference);
                Assert.Equal(FileSystemRights.FullControl, rule.FileSystemRights);
            }
        }
        finally { Directory.Delete(root, true); }
    }
}
