using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(Winnow.Ui.Tests.TestAppBuilder))]
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerAssembly)]

namespace Winnow.Ui.Tests;

public static class TestAppBuilder
{
    // Real fonts and templates are needed: fake text metrics hide overlapping hit targets.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App.App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
