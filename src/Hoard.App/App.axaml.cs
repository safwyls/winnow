using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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

            desktop.MainWindow = new MainWindow
            {
                DataContext = services.GetRequiredService<MainWindowViewModel>(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
