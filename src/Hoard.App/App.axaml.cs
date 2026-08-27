using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Hoard.App.Services;
using Hoard.App.ViewModels;
using Hoard.App.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Hoard.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

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

            desktop.MainWindow = new MainWindow
            {
                DataContext = services.GetRequiredService<MainWindowViewModel>(),
            };
        }

        base.OnFrameworkInitializationCompleted();
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
            Themes.HoardBackdrop? backdrop = material is null
                ? null
                : Themes.HoardBackdrops.ById(material["--backdrop=".Length..]);

            bool? wallTranslucent = wall is null
                ? null
                : wall["--wall=".Length..] is "on" or "true" or "1";

            Themes.HoardLayout? layout = arrangement is null
                ? null
                : Themes.HoardLayouts.ById(arrangement["--layout=".Length..]);

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

        theme.LoadAsync().GetAwaiter().GetResult();

        // Hot reload, started only on a real session. An author editing a
        // palette in a text editor gets the window repainted on save; a capture
        // run has been told what to look like and must not have it change under
        // the screenshot.
        theme.WatchUserThemes();
    }
}
