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
    /// <para><c>--theme=&lt;id&gt;</c> and <c>--transparent</c> override both
    /// preferences FOR THE SESSION ONLY and write nothing. That is the whole
    /// point of them: every theme and both transparency states have to be
    /// reviewable in a screenshot, and driving the settings screen by an
    /// injected click is not reliable enough to trust — but neither is leaving a
    /// preference behind in somebody's real library to get a picture.</para>
    /// </summary>
    private static void ApplyStartupTheme(ThemeService theme)
    {
#if DEBUG
        var args = Environment.GetCommandLineArgs();
        var requested = args.FirstOrDefault(a => a.StartsWith("--theme=", StringComparison.Ordinal));
        var transparent = args.Contains("--transparent");

        if (requested is not null || transparent)
        {
            theme.OverrideForSession(
                Themes.HoardThemes.ById(requested?["--theme=".Length..]),
                transparent);
            return;
        }
#endif

        theme.LoadAsync().GetAwaiter().GetResult();
    }
}
