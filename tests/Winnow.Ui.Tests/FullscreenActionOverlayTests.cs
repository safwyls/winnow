using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.Views.Fullscreen;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class FullscreenActionOverlayTests
{
    [AvaloniaTheory]
    [InlineData("controller")]
    [InlineData("keyboard")]
    [InlineData("pointer")]
    [InlineData("right-click")]
    public void Dismissal_keeps_the_origin_attached_traps_focus_and_restores_its_trigger(string input)
    {
        using var fixture = new Fixture();
        fixture.Open([new("Choose artwork", () => { }), new("Edit game details", () => { })]);
        var overlayPage = fixture.Shell.CurrentPage;
        Assert.NotSame(fixture.Origin, overlayPage);
        Assert.True(fixture.Origin.IsEffectivelyVisible);
        Assert.False(fixture.Origin.IsEffectivelyEnabled);
        Assert.Equal(1, fixture.Origin.Attaches); Assert.Equal(0, fixture.Origin.Detaches);
        fixture.Shell.Handle(GamepadButtons.Play | GamepadButtons.Search);
        Assert.Equal(0, fixture.Origin.LeakedActions);
        for (var index = 0; index < 6; index++)
        {
            Assert.True(fixture.Shell.HandleKey(new KeyEventArgs { Key = Key.Tab }));
            fixture.Flush();
            var focused = Assert.IsAssignableFrom<Control>(fixture.Window.FocusManager!.GetFocusedElement());
            Assert.Contains(fixture.Panel, focused.GetVisualAncestors());
        }
        if (input == "controller") fixture.Shell.Handle(GamepadButtons.Back);
        else if (input == "keyboard") Assert.True(fixture.Shell.HandleKey(new KeyEventArgs { Key = Key.Escape }));
        else if (input == "right-click")
        {
            var button = fixture.Button("Choose artwork");
            var point = button.TranslatePoint(new Point(10, 10), fixture.Window)!.Value;
            fixture.Window.MouseDown(point, MouseButton.Right); fixture.Window.MouseUp(point, MouseButton.Right);
        }
        else
        {
            var veil = fixture.Overlay;
            var point = veil.TranslatePoint(new Point(30, veil.Bounds.Height / 2), fixture.Window)!.Value;
            fixture.Window.MouseDown(point, MouseButton.Left); fixture.Window.MouseUp(point, MouseButton.Left);
        }
        fixture.Flush();
        Assert.Same(fixture.Origin, fixture.Shell.CurrentPage);
        Assert.True(fixture.Origin.IsEffectivelyEnabled);
        Assert.Equal(0, fixture.Origin.Detaches);
        Assert.Same(fixture.Origin.More, fixture.Window.FocusManager!.GetFocusedElement());
        Assert.DoesNotContain(fixture.Shell.GetVisualDescendants().OfType<Control>(), x => x.Name == "FullscreenActionPanel" && x.IsEffectivelyVisible);
    }

    [AvaloniaTheory]
    [InlineData("controller")]
    [InlineData("keyboard")]
    [InlineData("pointer")]
    public void Actions_invoke_once_and_disabled_choices_do_not_receive_input(string input)
    {
        using var fixture = new Fixture();
        var invoked = 0; var disabledInvoked = 0;
        fixture.Open([new("Unavailable action", () => disabledInvoked++, false), new("Pin this game", () => invoked++)]);
        var disabled = fixture.Button("Unavailable action");
        Assert.False(disabled.IsEffectivelyEnabled); Assert.False(disabled.Focus());
        var disabledPoint = disabled.TranslatePoint(new Point(disabled.Bounds.Width / 2, disabled.Bounds.Height / 2), fixture.Window)!.Value;
        fixture.Window.MouseDown(disabledPoint, MouseButton.Left); fixture.Window.MouseUp(disabledPoint, MouseButton.Left);
        fixture.Flush(); Assert.Equal(0, disabledInvoked); Assert.NotSame(fixture.Origin, fixture.Shell.CurrentPage);
        var enabled = fixture.Button("Pin this game"); enabled.Focus();
        if (input == "controller") fixture.Shell.Handle(GamepadButtons.Accept);
        else if (input == "keyboard") Assert.True(fixture.Shell.HandleKey(new KeyEventArgs { Key = Key.Enter }));
        else
        {
            var point = enabled.TranslatePoint(new Point(enabled.Bounds.Width / 2, enabled.Bounds.Height / 2), fixture.Window)!.Value;
            fixture.Window.MouseDown(point, MouseButton.Left); fixture.Window.MouseUp(point, MouseButton.Left);
        }
        fixture.Flush(); Assert.Equal(1, invoked); Assert.Equal(0, disabledInvoked);
        Assert.Same(fixture.Origin, fixture.Shell.CurrentPage);
        Assert.Same(fixture.Origin.More, fixture.Window.FocusManager!.GetFocusedElement());
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Right_click_on_a_page_respects_page_back_handling(bool handlesBack)
    {
        using var fixture = new Fixture();
        var child = new ProbePage(fixture.Context, "Edit game details") { HandlesBack = handlesBack };
        fixture.Context.Push(child); fixture.Flush();
        var point = child.More.TranslatePoint(new Point(10, 10), fixture.Window)!.Value;
        fixture.Window.MouseDown(point, MouseButton.Right); fixture.Window.MouseUp(point, MouseButton.Right);
        fixture.Flush();
        Assert.Equal(1, child.BackCalls);
        Assert.Same(handlesBack ? child : fixture.Origin, fixture.Shell.CurrentPage);
    }

    [AvaloniaFact]
    public void Action_can_open_another_page_and_back_returns_to_its_origin()
    {
        using var fixture = new Fixture();
        var child = new ProbePage(fixture.Context, "Edit game details");
        fixture.Open([new("Edit game details", () => fixture.Context.Push(child))]);
        fixture.Shell.Handle(GamepadButtons.Accept); fixture.Flush();
        Assert.Same(child, fixture.Shell.CurrentPage); Assert.True(child.IsEffectivelyEnabled);
        Assert.DoesNotContain(fixture.Shell.GetVisualDescendants().OfType<Control>(), x => x.Name == "FullscreenActionPanel" && x.IsEffectivelyVisible);
        fixture.Shell.Handle(GamepadButtons.Back); fixture.Flush();
        Assert.Same(fixture.Origin, fixture.Shell.CurrentPage);
        Assert.Same(fixture.Origin.More, fixture.Window.FocusManager!.GetFocusedElement());
    }

    [AvaloniaFact]
    public void Nested_action_menu_keeps_the_same_origin_without_detaching_it()
    {
        using var fixture = new Fixture();
        var calls = 0;
        fixture.Open([new("Remove game", () => fixture.Context.ShowActions("Remove this game?", [new("Keep game", () => calls++)]))]);
        fixture.Shell.Handle(GamepadButtons.Accept); fixture.Flush();
        Assert.Equal("Remove this game?", fixture.Shell.CurrentPage.Title);
        Assert.True(fixture.Origin.IsEffectivelyVisible); Assert.Equal(0, fixture.Origin.Detaches);
        fixture.Shell.Handle(GamepadButtons.Accept); fixture.Flush();
        Assert.Equal(1, calls); Assert.Same(fixture.Origin, fixture.Shell.CurrentPage);
        Assert.Equal(0, fixture.Origin.Detaches);
        Assert.Same(fixture.Origin.More, fixture.Window.FocusManager!.GetFocusedElement());
    }

    [AvaloniaTheory]
    [InlineData(1d, true)]
    [InlineData(1.4d, true)]
    [InlineData(1d, false)]
    public async Task Edge_panel_has_readable_wrapping_and_scrolling_at_each_text_scale(double textScale, bool reducedMotion)
    {
        using var fixture = new Fixture(textScale, reducedMotion);
        fixture.Open([
            new("Choose artwork", () => { }), new("Edit game details", () => { }),
            new("Choose launch version", () => { }),
            new("Open installation folder", () => { }, false), new("View recorded sessions", () => { }),
            new("Manage store links", () => { }), new("Add to a collection", () => { }),
            new("Hide this game", () => { }), new("Open store page", () => { }),
            new("Show related games", () => { }), new("Close", () => { })]);
        if (!reducedMotion)
        {
            Assert.True((fixture.Panel.RenderTransform?.Value.M31 ?? 0) > 0, "The panel should begin entering from the right edge.");
            await Task.Delay(350); fixture.Flush();
            Assert.InRange(Math.Abs(fixture.Panel.RenderTransform?.Value.M31 ?? 0), 0, .1);
        }
        var panel = fixture.Panel; var veil = fixture.Overlay;
        Assert.InRange(panel.Bounds.Width, 500, 900);
        var right = panel.TranslatePoint(new Point(panel.Bounds.Width, 0), veil)!.Value.X;
        Assert.InRange(Math.Abs(right - veil.Bounds.Width), 0, 2);
        Assert.True(panel.Bounds.Width < veil.Bounds.Width * .6);
        Assert.Equal(0, fixture.Origin.Detaches);
        if (reducedMotion) Assert.InRange(Math.Abs(panel.RenderTransform?.Value.M31 ?? 0), 0, .1);
        Assert.All(fixture.Shell.CurrentPage.GetVisualDescendants().OfType<Button>().Where(x => x.Classes.Contains("tv-action")), button =>
        {
            Assert.True(button.FontSize >= 28 * textScale - .1);
            Assert.True(button.Bounds.Width <= panel.Bounds.Width);
        });
        fixture.Capture($"fullscreen-actions-{textScale * 100:0}-{(reducedMotion ? "reduced" : "animated")}");
        var scroll = fixture.Shell.CurrentPage.GetVisualDescendants().OfType<ScrollViewer>().OrderByDescending(x => x.Viewport.Height).First();
        Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
        for (var i = 0; i < 12; i++) fixture.Shell.Handle(GamepadButtons.Down);
        fixture.Flush(); Assert.True(scroll.Offset.Y > 0);
        fixture.Capture($"fullscreen-actions-{textScale * 100:0}-scrolled");
    }

    [AvaloniaTheory]
    [InlineData(1d)]
    [InlineData(1.2d)]
    public void Panel_controls_respect_the_configured_safe_margin(double interfaceScale)
    {
        using var fixture = new Fixture(1.4, true, 10, interfaceScale);
        fixture.Open([new("Choose artwork", () => { })]);
        var title = fixture.Shell.CurrentPage.GetVisualDescendants().OfType<TextBlock>().Single(x => x.Text == "More actions");
        Assert.DoesNotContain(fixture.Shell.CurrentPage.GetVisualDescendants().OfType<Button>(),
            x => AutomationProperties.GetName(x) == "Close");
        var titleTop = title.TranslatePoint(default, fixture.Window)!.Value.Y;
        var titleRight = title.TranslatePoint(new Point(title.Bounds.Width, 0), fixture.Window)!.Value.X;
        Assert.True(titleTop >= fixture.Window.ClientSize.Height * .1 - 1);
        Assert.True(titleRight <= fixture.Window.ClientSize.Width * .9 + 1);
        var footer = fixture.Shell.CurrentPage.GetVisualDescendants().OfType<TextBlock>().Last(x => x.Text?.Contains("Close") == true);
        var footerBottom = footer.TranslatePoint(new Point(0, footer.Bounds.Height), fixture.Window)!.Value.Y;
        Assert.True(footerBottom <= fixture.Window.ClientSize.Height * .9 + 1);
    }

    private sealed class Fixture : IDisposable
    {
        public FullscreenContext Context { get; }
        public FullscreenView Shell { get; }
        public Window Window { get; }
        public ProbePage Origin { get; }
        public Control Panel => Shell.GetVisualDescendants().OfType<Control>().Single(x => x.Name == "FullscreenActionPanel" && x.IsEffectivelyVisible);
        public Control Overlay => Shell.GetVisualDescendants().OfType<Control>().Single(x => x.Name == "FullscreenActionOverlay" && x.IsEffectivelyVisible);
        public Fixture(double scale = 1, bool reducedMotion = true, double margin = 5, double interfaceScale = 1)
        {
            Context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell) { TextScale = scale, ReducedMotion = reducedMotion, SafeMarginPercent = margin, UiScale = interfaceScale };
            Shell = new FullscreenView(Context);
            Window = new Window { Width = 1920, Height = 1080, Content = Shell };
            Window.Show(); Flush();
            Origin = new ProbePage(Context, "Hollow Knight"); Context.Push(Origin); Flush();
        }
        public void Open(IReadOnlyList<FullscreenAction> actions)
        {
            Origin.More.Focus(); Context.ShowActions("More actions", actions); Flush();
        }
        public Button Button(string label) => Shell.CurrentPage.GetVisualDescendants().OfType<Button>().Single(x => AutomationProperties.GetName(x) == label);
        public void Flush() { Dispatcher.UIThread.RunJobs(); Window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
        public void Capture(string name)
        {
            if (Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is not { } directory) return;
            Directory.CreateDirectory(directory);
            using var frame = Window.CaptureRenderedFrame(); frame?.Save(Path.Combine(directory, name + ".png"));
        }
        public void Dispose() { Window.Close(); Shell.Dispose(); Context.Dispose(); }
    }
    private sealed class ProbePage : FullscreenPage
    {
        private readonly string _title;
        public override string Title => _title;
        public Button More { get; }
        public int Attaches { get; private set; }
        public int Detaches { get; private set; }
        public int LeakedActions { get; private set; }
        public bool HandlesBack { get; init; }
        public int BackCalls { get; private set; }
        public ProbePage(FullscreenContext context, string title) : base(context)
        {
            _title = title;
            More = FullscreenUi.Button("More actions", () => { });
            More.HorizontalAlignment = HorizontalAlignment.Left;
            Content = FullscreenUi.Stack(FullscreenUi.Text(title, 64), FullscreenUi.Text("Steam · 42.8 h played", 28, "TextDim"),
                FullscreenUi.Text("Explore a vast ruined kingdom of insects and heroes.", 32), More);
            SetFocusRows([More]);
            AttachedToVisualTree += (_, _) => Attaches++;
            DetachedFromVisualTree += (_, _) => Detaches++;
        }
        public override bool Handle(GamepadButtons buttons)
        {
            if (buttons.HasFlag(GamepadButtons.Back))
            {
                BackCalls++;
                if (HandlesBack) return true;
            }
            if ((buttons & (GamepadButtons.Play | GamepadButtons.Search)) != 0) { LeakedActions++; return true; }
            return base.Handle(buttons);
        }
    }
}
