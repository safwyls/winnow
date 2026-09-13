using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using System.Runtime.InteropServices;
using Winnow.App.Services;
AppBuilder.Configure<SmokeApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
public sealed class SmokeApp : Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        lifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var window = new Window { Title = "Winnow notification verification", Width = 300, Height = 100, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        var adapter = new WindowsJournalNotification();
        adapter.Attach(window);
        Win32Properties.AddWndProcHookCallback(window, (nint hwnd, uint message, nint wParam, nint lParam, ref bool handled) =>
        {
            if (message == 0x8574 && lParam == 0x402)
            {
                Console.WriteLine("Native balloon SHOW callback observed.");
                Dispatcher.UIThread.Post(() => PostMessageW(hwnd, message, wParam, (nint)0x405));
            }
            return 0;
        });
        window.Opened += (_, _) =>
        {
            window.Hide();
            var result = adapter.Show("Winnow verification session", () =>
            {
                Console.WriteLine("Native hook activation reached the app callback; no journal write.");
                adapter.Dispose(); lifetime.Shutdown();
            }, () => { Console.WriteLine("Native delivery unconfirmed: in-window fallback requested."); adapter.Dispose(); lifetime.Shutdown(); });
            Console.WriteLine("Delivery result: " + result);
            if (result != JournalNotificationDelivery.Submitted) { adapter.Dispose(); lifetime.Shutdown(); }
        };
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
        timeout.Tick += (_, _) => { Console.WriteLine("Verification timeout."); adapter.Dispose(); lifetime.Shutdown(); };
        timeout.Start();
        lifetime.MainWindow = window;
        base.OnFrameworkInitializationCompleted();
    }
    [DllImport("user32.dll")] private static extern bool PostMessageW(nint hwnd, uint message, nint wParam, nint lParam);
}
