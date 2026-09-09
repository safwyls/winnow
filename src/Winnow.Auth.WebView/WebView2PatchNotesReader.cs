using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Web.WebView2.Core;
using Winnow.Core.Reading;

namespace Winnow.Auth.WebView;

/// <summary>
/// Opens a game's patch notes in a separate WebView2 window. The surface has
/// no bridge, no host object, no web message channel and no script injection;
/// its only capability is rendering an allowlisted Steam news page.
///
/// <para><b>What this surface is allowed to do, stated as rules.</b></para>
///
/// <list type="number">
/// <item><description>The panel navigates only to origins
/// <see cref="PatchNotesPolicy"/> allows. An off-allowlist address is cancelled
/// and handed to the user's own browser. A non-web scheme is blocked
/// outright.</description></item>
/// <item><description>Sub-frames are stricter: an off-allowlist frame is blocked
/// rather than opened externally, so a third-party embed cannot
/// load.</description></item>
/// <item><description>No script is injected, no host object is defined, and web
/// messages are disabled. Script itself stays on: a storefront news page is an
/// ordinary web page, and with nothing to talk to it has no channel into the
/// host.</description></item>
/// <item><description>Every download is cancelled, every permission request
/// denied, and every external-URI-scheme launch refused.</description></item>
/// <item><description>The profile is in-private, so the session writes nothing
/// of consequence.</description></item>
/// <item><description>One window at a time; a second note navigates the open
/// window and activates it.</description></item>
/// </list>
/// </summary>
public sealed class WebView2PatchNotesReader : IPatchNotesReader
{
    /// <summary>Subdirectory name under the WebView2 profile root.</summary>
    public const string ProfileFolderName = "patch-notes";

    /// <summary>Window size, matching the sign-in browser window.</summary>
    private const double PanelWidth = 1024;
    /// <inheritdoc cref="PanelWidth"/>
    private const double PanelHeight = 820;

    private const string TitlePrefix = "Patch notes";
    private const string ExternalLabel = "Open in browser";
    private const string CouldNotStart = "The embedded browser could not start.";

    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);

    private readonly string _profileFolder;
    private readonly ILogger _log;

    private Window? _window;
    private CoreWebView2? _browser;
    private PatchNotesPolicy? _policy;
    private TextBlock? _address;
    private TextBlock? _problem;

    private readonly IWebViewInputSupport? _input;
    public WebView2PatchNotesReader(string profileRoot, ILogger<WebView2PatchNotesReader>? log = null, IWebViewInputSupport? input = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileRoot);

        _profileFolder = Path.Combine(profileRoot, ProfileFolderName);
        _log = log ?? NullLogger<WebView2PatchNotesReader>.Instance;
        _input = input;
    }

    /// <summary>
    /// False when there is no WebView2 runtime or no Avalonia application. The
    /// reader is registered unconditionally and answers for itself at use time.
    /// </summary>
    public bool IsAvailable => WebView2Runtime.IsAvailable && Application.Current is not null;

    /// <summary>
    /// Opens (or navigates an already-open) patch-notes window to
    /// <paramref name="url"/>. Posts to the UI thread and returns immediately.
    /// </summary>
    public PatchNotesOutcome Open(Uri url, string title)
    {
        ArgumentNullException.ThrowIfNull(url);

        if (!IsAvailable)
        {
            return PatchNotesOutcome.Unavailable;
        }

        if (PatchNotesPolicy.For(url) is not { } policy)
        {
            return PatchNotesOutcome.NotANotesPage;
        }

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                Show(policy, title);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                // An exception inside a posted action is unhandled — it reaches
                // the dispatcher loop and takes the application down. The window
                // is a convenience; the user can still open the address in their
                // own browser.
                _log.LogWarning(
                    "The patch-notes window could not be shown ({ExceptionType}).", ex.GetType().Name);
            }
        });

        return PatchNotesOutcome.Opened;
    }

    /// <summary>
    /// Shows or reuses the patch-notes window. A second note navigates the open
    /// window rather than opening a second one.
    /// </summary>
    private void Show(PatchNotesPolicy policy, string title)
    {
        _policy = policy;

        // One window at a time: reuse rather than replace.
        if (_window is { } existing)
        {
            existing.Title = WindowTitle(title);
            SetAddress(policy.Start);
            SetProblem(null);
            _browser?.Navigate(policy.Start.ToString());
            existing.Activate();
            return;
        }

        // In-private: the news page is read-only content, and the user must not
        // accumulate a browsing session from incidental page views.
        var host = new WebView2Host(_profileFolder, inPrivate: true);
        var window = BuildWindow(title, host);
        if (_input is not null && window.Content is Control content) { window.Content = null; window.Content = _input.Wrap(window, content, host, reading: true); }

        window.Closed += (_, _) =>
        {
            _window = null;
            _browser = null;
            _policy = null;
            _address = null;
            _problem = null;
        };

        _window = window;
        SetAddress(policy.Start);

        if ((Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow is { } owner)
        {
            window.Show(owner);
        }
        else
        {
            window.Show();
        }

        _ = AttachAsync(host, window);
    }

    /// <summary>
    /// Waits for the WebView2 environment to be ready (30-second ceiling),
    /// then hardens and arms the browser. Shows an Amber failure line if the
    /// browser does not start; the window stays dismissable.
    /// </summary>
    private async Task AttachAsync(WebView2Host host, Window window)
    {
        try
        {
            var controller = await host.Ready.WaitAsync(StartTimeout);

            if (!ReferenceEquals(_window, window))
            {
                return;
            }

            var browser = controller.CoreWebView2;
            _browser = browser;

            Harden(browser);
            Arm(browser, window);

            if (_policy is { } policy)
            {
                browser.Navigate(policy.Start.ToString());
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            _log.LogWarning(
                "The patch-notes panel could not start its browser ({ExceptionType}).", ex.GetType().Name);

            if (ReferenceEquals(_window, window))
            {
                SetProblem(CouldNotStart);
            }
        }
    }

    /// <summary>
    /// Locks down every capability the news page does not need. Script stays
    /// on: a storefront page is an ordinary web page, and with no bridge, no
    /// host object and no web message channel it has nothing to talk to.
    /// </summary>
    private static void Harden(CoreWebView2 browser)
    {
        var settings = browser.Settings;

        settings.AreHostObjectsAllowed = false;
        settings.IsWebMessageEnabled = false;
        settings.AreDevToolsEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreDefaultScriptDialogsEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsSwipeNavigationEnabled = false;
    }

    /// <summary>
    /// Wires the navigation, popup, frame, download and permission gates to
    /// the current policy, and connects the address bar and the
    /// <c>window.close()</c> handler.
    /// </summary>
    private void Arm(CoreWebView2 browser, Window window)
    {
        browser.NavigationStarting += (_, e) =>
        {
            Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri);

            switch (_policy?.ClassifyNavigation(uri) ?? PatchNotesNavigation.Block)
            {
                case PatchNotesNavigation.Allow:
                    SetProblem(null);
                    return;

                case PatchNotesNavigation.OpenExternally:
                    e.Cancel = true;
                    OpenExternally(uri!);
                    return;

                default:
                    e.Cancel = true;
                    _log.LogWarning(
                        "Refused to send the patch-notes panel to {Origin}.",
                        Winnow.Core.Auth.AuthFlowPolicy.OriginOf(uri) ?? "a non-web address");
                    return;
            }
        };

        // Frames are stricter than the top-level gate: an off-allowlist frame
        // is blocked rather than opened externally, so a third-party embedded
        // video or tracker cannot load. A deliberate cost.
        browser.FrameNavigationStarting += (_, e) =>
        {
            Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri);

            if ((_policy?.ClassifyFrame(uri) ?? PatchNotesNavigation.Block) != PatchNotesNavigation.Allow)
            {
                e.Cancel = true;
            }
        };

        browser.NewWindowRequested += (sender, e) =>
        {
            e.Handled = true;

            Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri);

            switch (_policy?.ClassifyPopup(uri) ?? PatchNotesNavigation.Block)
            {
                case PatchNotesNavigation.Allow:
                    ((CoreWebView2)sender!).Navigate(uri!.ToString());
                    return;

                case PatchNotesNavigation.OpenExternally:
                    OpenExternally(uri!);
                    return;

                default:
                    return;
            }
        };

        browser.DownloadStarting += (_, e) => e.Cancel = true;

        browser.PermissionRequested += (_, e) =>
        {
            e.State = CoreWebView2PermissionState.Deny;
            e.Handled = true;
        };

        browser.LaunchingExternalUriScheme += (_, e) => e.Cancel = true;

        browser.SourceChanged += (sender, _) =>
        {
            Uri.TryCreate(((CoreWebView2)sender!).Source, UriKind.Absolute, out var uri);
            SetAddress(uri);
        };

        browser.WindowCloseRequested += (_, _) => window.Close();
    }

    /// <summary>
    /// Builds the patch-notes window: a system title bar, a strip with the
    /// current host and an "open in browser" button, and the WebView2 host
    /// filling the rest. Non-modal, owned by the main window, dismissed by
    /// Escape or the close button.
    /// </summary>
    private Window BuildWindow(string title, WebView2Host host)
    {
        _address = new TextBlock
        {
            Classes = { "data-s" },
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        _problem = new TextBlock
        {
            Classes = { "body", "notes-problem" },
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };

        var external = new Button
        {
            Classes = { "act", "quiet" },
            Content = ExternalLabel,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        external.Click += (_, _) =>
        {
            if (Uri.TryCreate(_browser?.Source, UriKind.Absolute, out var current))
            {
                OpenExternally(current);
                return;
            }

            if (_policy is { } policy)
            {
                OpenExternally(policy.Start);
            }
        };

        var strip = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
        };

        var read = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
        };

        read.Children.Add(_address);
        read.Children.Add(_problem);

        strip.Children.Add(read);
        Grid.SetColumn(external, 1);
        strip.Children.Add(external);

        var bar = new Border
        {
            Classes = { "notes-bar" },
            Child = strip,
        };

        DockPanel.SetDock(bar, Dock.Top);

        var root = new DockPanel { Classes = { "notes" }, LastChildFill = true };
        root.Children.Add(bar);
        root.Children.Add(host);

        var window = new Window
        {
            Title = WindowTitle(title),
            Classes = { "notes" },
            Icon = HostIcon(),
            Width = PanelWidth,
            Height = PanelHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = true,
            Content = root,
        };

        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                window.Close();
                e.Handled = true;
            }
        };

        return window;
    }

    private static string WindowTitle(string title)
        => string.IsNullOrWhiteSpace(title) ? TitlePrefix : TitlePrefix + " — " + title;

    private static WindowIcon? HostIcon()
        => (Application.Current?.ApplicationLifetime
                as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.Icon;

    private void SetAddress(Uri? uri)
    {
        if (_address is not null)
        {
            _address.Text = uri?.Host ?? string.Empty;
        }
    }

    private void SetProblem(string? text)
    {
        if (_problem is null)
        {
            return;
        }

        _problem.Text = text ?? string.Empty;
        _problem.IsVisible = text is not null;
    }

    /// <summary>Hands <paramref name="uri"/> to the OS shell (the user's own browser).</summary>
    private void OpenExternally(Uri uri)
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
            or InvalidOperationException
            or PlatformNotSupportedException
            or FileNotFoundException)
        {
            _log.LogDebug("Could not open a link in the default browser ({ExceptionType}).", ex.GetType().Name);
        }
    }
}
