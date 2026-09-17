using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class FirstRunSetupDesktopTests
{
    [AvaloniaTheory]
    [InlineData("winnow")]
    [InlineData("rose-pine-dawn")]
    public async Task Header_and_footer_fills_stay_inside_the_rounded_frame(string themeId)
    {
        var service = PreviewData.Appearance.Service;
        var original = service.Theme;
        service.SelectTheme(service.Catalogue.Single(t => t.Id == themeId));
        try
        {
            using var fixture = new Fixture();
            await fixture.OpenAsync();
            var frame = fixture.View.FindControl<Border>("SetupFrame")!;
            var clip = fixture.View.FindControl<Border>("SetupContentClip")!;
            Assert.Equal(new CornerRadius(7), clip.CornerRadius);
            Assert.Equal(new Thickness(1), frame.BorderThickness);
            using var corrected = await Capture();
            if (Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is { } directory)
            {
                Directory.CreateDirectory(directory);
                corrected.Save(Path.Combine(directory, $"setup-rounded-{themeId}.png"));
            }
            var expected = Pixels(corrected);
            clip.ClipToBounds = false;
            using var square = await Capture();
            var actual = Pixels(square);
            var origin = clip.TranslatePoint(default, fixture.Window)!.Value;
            foreach (var right in new[] { false, true })
            foreach (var bottom in new[] { false, true })
            {
                var changed = false;
                for (var y = 0; y < 7; y++)
                for (var x = 0; x < 7; x++)
                {
                    var px = (int)origin.X + (right ? (int)clip.Bounds.Width - 1 - x : x);
                    var py = (int)origin.Y + (bottom ? (int)clip.Bounds.Height - 1 - y : y);
                    var offset = (py * corrected.PixelSize.Width + px) * 4;
                    changed |= !expected.AsSpan(offset, 4).SequenceEqual(actual.AsSpan(offset, 4));
                }
                Assert.True(changed, "Removing the inner clip must reproduce corner overpainting.");
            }

            async Task<Bitmap> Capture()
            {
                Flush(); fixture.Window.UpdateLayout();
                await Task.Delay(100);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Flush();
                return fixture.Window.CaptureRenderedFrame()!;
            }
        }
        finally { service.SelectTheme(original); }
    }

    private static byte[] Pixels(Bitmap bitmap)
    {
        var pixels = new byte[bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4];
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), pin.AddrOfPinnedObject(), pixels.Length, bitmap.PixelSize.Width * 4); }
        finally { pin.Free(); }
        return pixels;
    }

    [AvaloniaFact]
    public async Task Every_step_keeps_navigation_inside_a_short_desktop_window()
    {
        using var fixture = new Fixture();
        await fixture.OpenAsync();
        foreach (var step in Enum.GetValues<FirstRunStep>())
        {
            Assert.Equal(step, fixture.Model.Step);
            Flush();
            foreach (var name in new[] { "NextButton", "BackButton", "SkipSetupButton", "StepHeading" })
                AssertInside(fixture.View.FindControl<Control>(name)!, fixture.Window);
            Assert.Same(fixture.View.FindControl<Button>("NextButton"), fixture.Window.FocusManager!.GetFocusedElement());
            if (step == FirstRunStep.Igdb)
            {
                var editor = fixture.View.GetVisualDescendants().OfType<IgdbSettingsView>().Single();
                foreach (var name in new[] { "SaveCredentialsButton", "RemoveCredentialsButton" })
                {
                    var action = editor.FindControl<Button>(name)!;
                    AssertInside(action, fixture.Window);
                    Assert.DoesNotContain(action.GetVisualAncestors(), ancestor => ancestor is ScrollViewer);
                }
            }
            if (step is FirstRunStep.Steam or FirstRunStep.Epic or FirstRunStep.Gog)
            {
                var stores = fixture.View.GetVisualDescendants().OfType<StoresView>().Single();
                Assert.True(stores.IsEmbedded);
                Assert.False(stores.FindControl<Border>("PlatformHeader")!.IsVisible);
            }
            if (step == FirstRunStep.Application)
            {
                var application = fixture.View.GetVisualDescendants().OfType<ApplicationSettingsView>().Single();
                Assert.False(application.FindControl<Border>("SetupCard")!.IsVisible);
                Assert.Null(application.FindControl<Border>("IgdbCard"));
            }
            if (Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is { } directory)
            {
                Directory.CreateDirectory(directory);
                using var frame = fixture.Window.CaptureRenderedFrame();
                frame!.Save(Path.Combine(directory, $"setup-desktop-{step}.png"));
            }
            await fixture.Model.NextCommand.ExecuteAsync(null);
        }
        Assert.False(fixture.Model.IsOpen);
        Assert.Same(fixture.Origin, fixture.Window.FocusManager!.GetFocusedElement());
    }

    [AvaloniaFact]
    public async Task Tab_cycles_inside_setup_and_skip_all_restores_the_invoking_control()
    {
        using var fixture = new Fixture();
        await fixture.OpenAsync();
        var visited = new HashSet<Control>();
        for (var i = 0; i < 12; i++)
        {
            Press(fixture.Window, PhysicalKey.Tab);
            var focused = Assert.IsAssignableFrom<Control>(fixture.Window.FocusManager!.GetFocusedElement());
            Assert.Contains(fixture.View, focused.GetVisualAncestors());
            visited.Add(focused);
        }
        Assert.Equal(2, visited.Count);
        Press(fixture.Window, PhysicalKey.Tab, RawInputModifiers.Shift);
        Assert.Contains(fixture.View, ((Control)fixture.Window.FocusManager!.GetFocusedElement()!).GetVisualAncestors());
        await fixture.Model.SkipAllCommand.ExecuteAsync(null);
        Flush();
        Assert.Same(fixture.Origin, fixture.Window.FocusManager!.GetFocusedElement());
    }

    [AvaloniaFact]
    public async Task Skipping_IGDB_clears_the_masked_draft_without_saving_it()
    {
        using var fixture = new Fixture();
        await fixture.OpenAsync();
        await fixture.Model.NextCommand.ExecuteAsync(null);
        Flush();
        var editor = fixture.View.GetVisualDescendants().OfType<IgdbSettingsView>().Single();
        var secret = editor.FindControl<TextBox>("IgdbClientSecret")!;
        Assert.Equal('●', secret.PasswordChar);
        secret.Text = "unsaved-secret";
        editor.FindControl<TextBox>("IgdbClientId")!.Text = "client";
        secret.Focus();
        Press(fixture.Window, PhysicalKey.Escape);
        if (fixture.Model.SkipStepCommand.ExecutionTask is { } task) await task;
        Flush();
        Assert.Equal(FirstRunStep.Steam, fixture.Model.Step);
        Assert.Empty(fixture.Model.Application.Igdb.ClientSecret);
        Assert.Equal(0, fixture.Settings.Saves);
    }

    [AvaloniaFact]
    public async Task Platform_consent_traps_focus_and_escape_closes_only_that_layer()
    {
        using var fixture = new Fixture();
        await fixture.OpenAsync();
        await fixture.Model.NextCommand.ExecuteAsync(null);
        await fixture.Model.NextCommand.ExecuteAsync(null);
        fixture.Model.Stores.OpenSignInConsentCommand.Execute(null);
        Flush();
        var modal = fixture.View.GetVisualDescendants().OfType<Border>()
            .Single(b => b.Classes.Contains("modal") && b.IsEffectivelyVisible);
        AssertInside(modal, fixture.Window);
        for (var i = 0; i < 10; i++)
        {
            Press(fixture.Window, PhysicalKey.Tab);
            Assert.Contains(modal, ((Control)fixture.Window.FocusManager!.GetFocusedElement()!).GetVisualAncestors());
        }
        Press(fixture.Window, PhysicalKey.Escape);
        Assert.False(fixture.Model.Stores.IsAnyModalOpen);
        Assert.Equal(FirstRunStep.Steam, fixture.Model.Step);
        Assert.False(fixture.Model.Stores.CapturePurchaseHistory);
    }

    [AvaloniaFact]
    public void Application_settings_exposes_the_shared_replay_command()
    {
        var model = new ApplicationSettingsViewModel();
        var view = new ApplicationSettingsView { DataContext = model };
        Assert.Same(model.OpenSetupCommand, view.FindControl<Button>("OpenSetupButton")!.Command);
        Assert.True(view.FindControl<Border>("SetupCard")!.IsVisible);
    }

    private static void AssertInside(Control control, Window window)
    {
        var point = control.TranslatePoint(default, window)!.Value;
        Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0);
        Assert.InRange(point.X, 0, window.ClientSize.Width);
        Assert.InRange(point.Y, 0, window.ClientSize.Height);
        Assert.True(point.X + control.Bounds.Width <= window.ClientSize.Width + 1);
        Assert.True(point.Y + control.Bounds.Height <= window.ClientSize.Height + 1);
    }

    private static void Flush() => Dispatcher.UIThread.RunJobs();
    private static void Press(Window window, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, modifiers);
        window.KeyReleaseQwerty(key, modifiers);
        Flush();
    }

    private sealed class Fixture : IDisposable
    {
        public TestSettings Settings { get; } = new();
        public FirstRunSetupViewModel Model { get; }
        public FirstRunSetupView View { get; }
        public Button Origin { get; } = new() { Content = "Run setup again" };
        public Window Window { get; }
        public Fixture()
        {
            Model = new FirstRunSetupViewModel(PreviewData.Stores, PreviewData.Appearance,
                new ApplicationSettingsViewModel(igdb: new IgdbSettingsViewModel(Settings)), PreviewData.LibrarySettings);
            View = new FirstRunSetupView { DataContext = Model };
            View.Bind(Visual.IsVisibleProperty, new Binding(nameof(Model.IsOpen)) { Source = Model });
            var panel = new Panel();
            panel.Children.Add(Origin);
            panel.Children.Add(View);
            // The minimum 640px window reserves 36px for its desktop caption.
            Window = new Window { Width = 1200, Height = 604, Content = panel };
            Window.Show();
            Origin.Focus();
        }
        public async Task OpenAsync() { await Model.ReopenCommand.ExecuteAsync(null); Flush(); }
        public void Dispose() => Window.Close();
    }

    private sealed class TestSettings : IIgdbSettingsService
    {
        public int Saves { get; private set; }
        public Task<IgdbSettingsSnapshot> LoadAsync(CancellationToken ct = default)
            => Task.FromResult(new IgdbSettingsSnapshot("", false, false, false));
        public Task<IgdbSettingsSaveResult> SaveAsync(string clientId, string clientSecret)
        {
            Saves++;
            return Task.FromResult(IgdbSettingsSaveResult.Saved);
        }
        public Task<bool> RemoveAsync() => Task.FromResult(false);
    }
}
