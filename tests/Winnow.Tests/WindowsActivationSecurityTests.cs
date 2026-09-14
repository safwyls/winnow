using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Winnow.App.Services;
using Xunit;

namespace Winnow.Tests;

public sealed class WindowsActivationSecurityTests
{
    [Fact]
    public void Activation_objects_belong_to_user_and_exclude_other_accounts_and_network_logons()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var identity = WindowsIdentity.GetCurrent();
        using var pipe = WindowsActivationSecurity.CreatePipe("winnow-security-" + Guid.NewGuid().ToString("N"));
        using var mutex = WindowsActivationSecurity.CreateMutex("Local\\winnow-security-" + Guid.NewGuid().ToString("N"), out var created);
        Assert.True(created);
        try
        {
            foreach (var security in new NativeObjectSecurity[] { pipe.GetAccessControl(), mutex.GetAccessControl() })
            {
                Assert.Equal(identity.User, security.GetOwner(typeof(SecurityIdentifier)));
                Assert.True(security.AreAccessRulesProtected);
                var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<AccessRule>().ToArray();
                Assert.Equal(2, rules.Length);
                Assert.Equal(AccessControlType.Deny, rules[0].AccessControlType);
                Assert.Equal(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), rules[0].IdentityReference);
                Assert.Equal(AccessControlType.Allow, rules[1].AccessControlType);
                Assert.Equal(identity.User, rules[1].IdentityReference);
            }
        }
        finally { mutex.ReleaseMutex(); }
    }
}
