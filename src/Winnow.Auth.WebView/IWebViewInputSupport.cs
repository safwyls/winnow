using Avalonia.Controls;

namespace Winnow.Auth.WebView;

/// <summary>Optional host-owned input chrome, constructed before a native browser is attached.</summary>
public interface IWebViewInputSupport
{
    Control Wrap(Window window, Control content, WebView2Host? browser = null, bool reading = false);
}
