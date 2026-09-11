using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Winnow.App.ViewModels;

namespace Winnow.App.Views;

public partial class FirstRunSetupView : UserControl
{
    private FirstRunSetupViewModel? _model;
    private IInputElement? _origin;
    private bool _stepNeedsFocus;

    public FirstRunSetupView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => ObserveModel();
        AttachedToVisualTree += (_, _) =>
        {
            ObserveModel();
            if (_model?.IsOpen == true) OpenFocus();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            RestoreFocus();
            if (_model is not null) _model.PropertyChanged -= OnModelChanged;
            _model = null;
        };
        AddHandler(KeyDownEvent, OnSetupKeyDown, RoutingStrategies.Bubble);
    }

    private void ObserveModel()
    {
        if (ReferenceEquals(_model, DataContext)) return;
        if (_model is not null) _model.PropertyChanged -= OnModelChanged;
        _model = DataContext as FirstRunSetupViewModel;
        if (_model is null) return;
        _model.PropertyChanged += OnModelChanged;
        ShowStep();
        if (_model.IsOpen) OpenFocus();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FirstRunSetupViewModel.Step))
        {
            ShowStep();
            _stepNeedsFocus = true;
            FocusNext();
        }
        else if (e.PropertyName == nameof(FirstRunSetupViewModel.IsOpen))
        {
            if (_model?.IsOpen == true) OpenFocus();
            else RestoreFocus();
        }
        else if (e.PropertyName == nameof(FirstRunSetupViewModel.IsBusy) && _stepNeedsFocus && _model?.IsBusy == false)
            FocusNext();
    }

    private void ShowStep()
    {
        if (_model is null) return;
        StepContent.Content = _model.Step switch
        {
            FirstRunStep.Igdb => new IgdbSettingsView { DataContext = _model.Application.Igdb },
            FirstRunStep.Steam or FirstRunStep.Epic or FirstRunStep.Gog =>
                new StoresView { DataContext = _model.Stores, IsEmbedded = true },
            FirstRunStep.Theme => new AppearanceView { DataContext = _model.Appearance, IsEmbedded = true },
            FirstRunStep.Application => new ApplicationSettingsView { DataContext = _model.Application, IsEmbedded = true },
            FirstRunStep.Library => BuildTemplate("LibraryStep"),
            FirstRunStep.Ready => BuildTemplate("ReadyStep"),
            _ => BuildTemplate("WelcomeStep")
        };
    }

    private Control? BuildTemplate(string key) => ((IDataTemplate)this.FindResource(key)!).Build(_model);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != IsVisibleProperty || _model is null) return;
        if (change.GetNewValue<bool>())
        {
            if (_model.IsOpen) OpenFocus();
        }
        else
        {
            _model.Application.Igdb.ClientSecret = "";
            _model.Stores.SteamApiKeyInput = "";
            RestoreFocus();
        }
    }

    private void OpenFocus()
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        if (focused is not null && !ReferenceEquals(focused, NextButton)) _origin ??= focused;
        _stepNeedsFocus = true;
        FocusNext();
    }

    private void FocusNext() => Dispatcher.UIThread.Post(() =>
    {
        if (_model?.IsOpen == true && !_model.IsBusy && IsEffectivelyVisible && NextButton.Focus(NavigationMethod.Tab))
            _stepNeedsFocus = false;
    }, DispatcherPriority.Loaded);

    private void RestoreFocus()
    {
        _origin?.Focus(NavigationMethod.Tab);
        _origin = null;
    }

    private void OnSetupKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _model is null || !_model.IsOpen) return;
        // A platform's consent or explanation is a layer above the step.
        if (_model.Stores.IsAnyModalOpen) _model.Stores.CloseModalCommand.Execute(null);
        else if (_model.CanSkipStep && _model.SkipStepCommand.CanExecute(null))
            _model.SkipStepCommand.Execute(null);
        e.Handled = true;
    }
}
