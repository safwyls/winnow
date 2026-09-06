using Winnow.App.Services;
using Xunit;

namespace Winnow.Tests;

public sealed class ConsoleAuthPromptTests
{
    [Fact]
    public void A_null_WinExe_standard_handle_is_not_mistaken_for_redirection()
    {
        // WinExe without a parent terminal: .NET reports this shape as
        // IsOutputRedirected, but the actual Windows handle is NULL. The
        // console helper must attach rather than write to that void.
        Assert.False(ConsoleAuthPrompt.StandardHandleIsUsable(IntPtr.Zero));
        Assert.False(ConsoleAuthPrompt.StandardHandleIsUsable(new IntPtr(-1)));
    }
}
