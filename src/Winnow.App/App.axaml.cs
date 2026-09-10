using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Winnow.App;

public partial class App : Application
{
    private MainWindow? _mainWindow;
    private ApplicationSettingsViewModel? _applicationSettings;
    private TrayIcon? _trayIcon;
    private bool _backgroundStart;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        _trayIcon = TrayIcon.GetIcons(this)?.SingleOrDefault();

        // tokens.axaml carries its text styles as a keyed resource (a
        // ResourceDictionary cannot hold an unkeyed Styles block); promote
        // them to app-level styles so the class selectors apply.
        if (Resources.TryGetResource("TextStyles", null, out var resource)
            && resource is Avalonia.Styling.Styles textStyles)
        {
            Styles.Add(textStyles);
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // View models come from the generic host's container (§5).
            var services = Program.AppHost?.Services
                ?? throw new InvalidOperationException("Host not started before Avalonia.");

            // The palette is applied BEFORE the window exists, and synchronously.
            // It is two rows out of the settings table, and the alternative is a
            // window that paints the default theme and then swaps — which on a
            // grid of six hundred covers is a visible flash, not a subtle one.
            var theme = services.GetRequiredService<ThemeService>();
            ApplyStartupTheme(theme);

            _applicationSettings = services.GetRequiredService<ApplicationSettingsViewModel>();
            ApplyStartupApplicationSettings(_applicationSettings);

            _backgroundStart = Environment.GetCommandLineArgs()
                .Contains("--background", StringComparer.Ordinal);

            _mainWindow = new MainWindow
            {
                DataContext = services.GetRequiredService<MainWindowViewModel>(),
                StartHidden = _backgroundStart,
            };
            _mainWindow.TrayStateChanged += (_, _) => UpdateTrayVisibility();
            _applicationSettings.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ApplicationSettingsViewModel.TrayIconWanted))
                {
                    UpdateTrayVisibility();
                }
            };

            desktop.MainWindow = _mainWindow;
            UpdateTrayVisibility();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnTrayClicked(object? sender, EventArgs e) => RestoreMainWindow();

    private void OnOpenFromTray(object? sender, EventArgs e) => RestoreMainWindow();

    private void OnExitFromTray(object? sender, EventArgs e)
    {
        if (_trayIcon is not null)
        {
            _trayIcon.IsVisible = false;
        }
        _mainWindow?.ExitFromTray();
    }

    internal void ExitForUpdate()
    {
        if (_trayIcon is not null) _trayIcon.IsVisible = false;
        _mainWindow?.ExitFromTray();
    }

    private void RestoreMainWindow()
    {
        _backgroundStart = false;
        _mainWindow?.RestoreFromTray();
        UpdateTrayVisibility();
    }

    private void UpdateTrayVisibility()
    {
        if (_trayIcon is not null)
        {
            _trayIcon.IsVisible = _backgroundStart
                || _mainWindow?.IsHiddenInTray == true
                || _applicationSettings?.TrayIconWanted == true;
        }
    }

    private static void ApplyStartupApplicationSettings(ApplicationSettingsViewModel settings)
    {
        try
        {
            // OnFrameworkInitializationCompleted owns the UI thread. Start the
            // asynchronous repository read on the pool so its continuation is
            // not waiting for the dispatcher we are synchronously holding.
            Task.Run(() => settings.LoadAsync()).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Program.AppHost?.Services.GetService<ILoggerFactory>()
                ?.CreateLogger(typeof(App).FullName!)
                .LogWarning(ex, "The stored application behavior could not be read; using defaults.");
        }
    }

    /// <summary>
    /// Reads the stored theme, or takes the debug capture flags instead.
    ///
    /// <para><c>--theme=&lt;id&gt;</c> and <c>--transparency=&lt;0-100&gt;</c>
    /// override both preferences FOR THE SESSION ONLY and write nothing. That is
    /// the whole point of them: every theme and every position on the slider has
    /// to be reviewable in a screenshot, and driving the settings screen by an
    /// injected drag is not reliable enough to trust — but neither is leaving a
    /// preference behind in somebody's real library to get a picture.</para>
    ///
    /// <para><c>--transparent</c> is kept as the old spelling and means the far
    /// end of the slider. <c>--backdrop=acrylic|mica</c>,
    /// <c>--wall=on|off</c> and <c>--layout=flush|floating</c> cover the other
    /// three decisions, on the same terms: session only, no write.</para>
    /// </summary>
    private static void ApplyStartupTheme(ThemeService theme)
    {
#if DEBUG
        var args = Environment.GetCommandLineArgs();
        var requested = args.FirstOrDefault(a => a.StartsWith("--theme=", StringComparison.Ordinal));
        var amount = args.FirstOrDefault(a => a.StartsWith("--transparency=", StringComparison.Ordinal));
        var material = args.FirstOrDefault(a => a.StartsWith("--backdrop=", StringComparison.Ordinal));
        var wall = args.FirstOrDefault(a => a.StartsWith("--wall=", StringComparison.Ordinal));
        var arrangement = args.FirstOrDefault(a => a.StartsWith("--layout=", StringComparison.Ordinal));
        var transparent = args.Contains("--transparent");

        if (requested is not null || amount is not null || transparent
            || material is not null || wall is not null || arrangement is not null)
        {
            var percent = transparent ? 100 : 0;
            if (amount is not null
                && int.TryParse(
                    amount["--transparency=".Length..],
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var parsed))
            {
                percent = parsed;
            }

            // Absent means "don't touch it". An overridden session never reads
            // the settings table, so what stays is the built-in default —
            // acrylic, and a solid wall — which is the state a capture that did
            // not ask about either one should be looking at.
            Themes.WinnowBackdrop? backdrop = material is null
                ? null
                : Themes.WinnowBackdrops.ById(material["--backdrop=".Length..]);

            bool? wallTranslucent = wall is null
                ? null
                : wall["--wall=".Length..] is "on" or "true" or "1";

            Themes.WinnowLayout? layout = arrangement is null
                ? null
                : Themes.WinnowLayouts.ById(arrangement["--layout=".Length..]);

            // The folder is read even on an overridden session, because
            // --theme= has to be able to name a USER theme: every screenshot of
            // one is taken this way, and resolving the id against the built-ins
            // alone would silently hand back the default. Reading it writes no
            // preference — the seal OverrideForSession applies is about the
            // settings table, and the themes folder is not it.
            theme.ReloadUserThemes();

            theme.OverrideForSession(
                theme.ById(requested?["--theme=".Length..]),
                percent,
                backdrop,
                wallTranslucent,
                layout);
            return;
        }
#endif

        // TASK-22 (F36). The one read on the startup spine whose failure must
        // not be fatal. It runs synchronously inside
        // OnFrameworkInitializationCompleted, so a throw here escapes into
        // Avalonia's start path and out to Program's boundary — which would
        // correctly refuse to open the app over an unreadable palette
        // preference. But a palette is not a library: every token already
        // carries the authored value from tokens.axaml, so a caught failure
        // leaves the window painted in the Winnow default rather than not
        // painted at all, and the user keeps their games.
        try
        {
            theme.LoadAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Program.AppHost?.Services.GetService<ILoggerFactory>()
                ?.CreateLogger(typeof(App).FullName!)
                .LogWarning(ex, "The stored appearance could not be read; opening in the default theme.");
        }

        // Hot reload, started only on a real session. An author editing a
        // palette in a text editor gets the window repainted on save; a capture
        // run has been told what to look like and must not have it change under
        // the screenshot.
        theme.WatchUserThemes();
    }
}
