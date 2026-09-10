using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Winnow.App.Services;
using Winnow.App.ViewModels;

namespace Winnow.App.Views.Fullscreen;

/// <summary>Setup retains its place while a TV-owned provider or settings page is open.</summary>
public sealed class FullscreenSetupPage : FullscreenPage
{
    private readonly FirstRunSetupViewModel _setup;
    public override string Title => "Set up Winnow";
    public override string Hints => _setup.Step != FirstRunStep.Welcome ? "A  Select     B  Previous step" : "A  Select";

    public FullscreenSetupPage(FullscreenContext context) : base(context)
    {
        _setup = context.Shared.Setup;
        Name = "FullscreenSetup";
        AutomationProperties.SetName(this, Title);
        _setup.PropertyChanged += SetupChanged;
        Render();
    }

    private void SetupChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FirstRunSetupViewModel.IsBusy) && !_setup.IsBusy)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (TopLevel.GetTopLevel(this) is not null && IsEffectivelyVisible) FocusInitial();
            }, Avalonia.Threading.DispatcherPriority.Loaded);
            return;
        }
        if (e.PropertyName != nameof(FirstRunSetupViewModel.Step)) return;
        Render();
        FocusInitial();
    }

    private void Render()
    {
        var focus = new List<Control[]>();
        var body = new StackPanel { Spacing = 24 };
        var progress = FullscreenHistoryTypography.Data(_setup.ProgressText, 24);
        AutomationProperties.SetLiveSetting(progress, AutomationLiveSetting.Polite);
        body.Children.Add(progress);
        body.Children.Add(FullscreenUi.Text(_setup.Title, 64));
        var description = FullscreenUi.Text(_setup.Description, 32, "TextDim");
        description.MaxWidth = 1100;
        description.HorizontalAlignment = HorizontalAlignment.Left;
        body.Children.Add(description);
        void Status(string property, object source)
        {
            var status = FullscreenUi.Text("", 28, "TextDim");
            status.Bind(TextBlock.TextProperty, new Binding(property) { Source = source });
            AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
            body.Children.Add(status);
        }
        void Open(string label, Func<FullscreenPage> page)
        {
            var button = FullscreenUi.Button(label, () => Context.Push(page()));
            button.Bind(IsEnabledProperty, new Binding("!IsBusy") { Source = _setup });
            body.Children.Add(button);
            focus.Add([button]);
        }
        switch (_setup.Step)
        {
            case FirstRunStep.Igdb:
                Status(nameof(Context.Shared.ApplicationSettings.Igdb.Status), Context.Shared.ApplicationSettings.Igdb);
                Open("Set up IGDB metadata", () => new FullscreenIgdbSettingsPage(Context));
                break;
            case FirstRunStep.Steam:
                Status(nameof(Context.Shared.Stores.SteamLocalMessage), Context.Shared.Stores);
                Status(nameof(Context.Shared.Stores.SteamStatusLabel), Context.Shared.Stores);
                Open("Set up Steam", () => new FullscreenPlatformPage(Context, "Steam"));
                break;
            case FirstRunStep.Epic:
                Status(nameof(Context.Shared.Stores.EpicLocalMessage), Context.Shared.Stores);
                Status(nameof(Context.Shared.Stores.EpicStatusLabel), Context.Shared.Stores);
                Open("Set up Epic", () => new FullscreenPlatformPage(Context, "Epic"));
                break;
            case FirstRunStep.Gog:
                Status(nameof(Context.Shared.Stores.GogLocalMessage), Context.Shared.Stores);
                Open("Check GOG Galaxy", () => new FullscreenPlatformPage(Context, "GOG"));
                break;
            case FirstRunStep.Theme:
                Open("Choose theme and appearance", () => new FullscreenSettingsPage(Context, "Appearance"));
                break;
            case FirstRunStep.Application:
                Open("Choose app settings", () => new FullscreenSettingsPage(Context, "Application"));
                break;
            case FirstRunStep.Library:
                Open("Choose library settings", () => new FullscreenSettingsPage(Context, "Library"));
                break;
        }
        var problem = FullscreenUi.Text("", 28, "Amber");
        problem.Bind(TextBlock.TextProperty, new Binding(nameof(_setup.Problem)) { Source = _setup });
        AutomationProperties.SetLiveSetting(problem, AutomationLiveSetting.Polite);
        body.Children.Add(problem);
        var note = FullscreenUi.Text("", 28, "TextDim");
        note.IsVisible = _setup.Step is FirstRunStep.Igdb or FirstRunStep.Ready;
        note.Bind(TextBlock.TextProperty, new Binding(nameof(_setup.RestartNote)) { Source = _setup });
        body.Children.Add(note);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24 };
        var buttons = new List<Control>();
        void Command(string label, ICommand command)
        {
            var button = FullscreenUi.Button(label, () => { });
            button.Command = command;
            actions.Children.Add(button);
            buttons.Add(button);
        }
        if (_setup.Step != FirstRunStep.Welcome) Command("Back", _setup.BackCommand);
        Command(_setup.NextLabel, _setup.NextCommand);
        if (_setup.Step is not (FirstRunStep.Welcome or FirstRunStep.Ready)) Command("Skip this step", _setup.SkipStepCommand);
        Command("Skip setup", _setup.SkipAllCommand);
        focus.Add(buttons.ToArray());
        var layout = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 24 };
        layout.Children.Add(FullscreenUi.Scroll(body));
        Grid.SetRow(actions, 1);
        layout.Children.Add(actions);
        Content = FullscreenAmbientBackdrop.Behind(layout, "settings");
        SetFocusRows(focus.ToArray());
        Changed();
    }

    public override bool Handle(GamepadButtons buttons)
    {
        if (buttons.HasFlag(GamepadButtons.Back))
        {
            if (_setup.BackCommand.CanExecute(null)) _setup.BackCommand.Execute(null);
            return true;
        }
        return base.Handle(buttons);
    }

    public override void Dispose()
    {
        _setup.PropertyChanged -= SetupChanged;
        base.Dispose();
    }
}
