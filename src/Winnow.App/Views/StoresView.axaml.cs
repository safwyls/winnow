using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.ViewModels;

namespace Winnow.App.Views;

/// <summary>
/// Code-behind for the Stores panel. There is deliberately almost none of it.
///
/// <para>Everything on this screen is either static copy or a bound state on
/// <see cref="ViewModels.StoresViewModel"/>, and the actions are commands. The
/// one thing a code-behind would otherwise be tempted to own — running the
/// sign-in — belongs to the view model precisely so it can be tested without a
/// browser, a window or an Epic account.</para>
///
/// <para>The single exception is scrim dismissal, which is a pointer gesture on
/// a specific visual and has no meaning to the view model; the game details
/// modal handles its own the same way. It closes through the view model's own
/// command, so the state still has one owner.</para>
/// </summary>
public partial class StoresView : UserControl
{
    private StoresViewModel? _embeddedModel;
    private IInputElement? _modalOrigin;
    public bool IsEmbedded
    {
        get => !PlatformHeader.IsVisible;
        set
        {
            PlatformHeader.IsVisible = !value;
            if (value) ObserveEmbeddedModel();
        }
    }

    public StoresView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => { if (IsEmbedded) ObserveEmbeddedModel(); };
        DetachedFromVisualTree += (_, _) =>
        {
            if (_embeddedModel is not null) _embeddedModel.PropertyChanged -= OnEmbeddedModelChanged;
            _embeddedModel = null;
        };
        SizeChanged += (_, _) => { if (IsEmbedded) FitEmbeddedModals(); };

        // The previewer gets the panel in its not-connected state; runtime
        // leaves the DataContext to the shell. See Design/PreviewData.cs.
        if (Avalonia.Controls.Design.IsDesignMode)
        {
            DataContext = Design.PreviewData.Stores;
        }
    }

    private void ObserveEmbeddedModel()
    {
        if (ReferenceEquals(_embeddedModel, DataContext)) return;
        if (_embeddedModel is not null) _embeddedModel.PropertyChanged -= OnEmbeddedModelChanged;
        _embeddedModel = DataContext as StoresViewModel;
        if (_embeddedModel is not null) _embeddedModel.PropertyChanged += OnEmbeddedModelChanged;
    }

    private void FitEmbeddedModals()
    {
        foreach (var modal in this.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("modal")))
        {
            KeyboardNavigation.SetTabNavigation(modal, KeyboardNavigationMode.Cycle);
            foreach (var scroll in modal.GetVisualDescendants().OfType<ScrollViewer>())
                scroll.MaxHeight = Math.Max(64, Bounds.Height - 180);
        }
    }

    private void OnEmbeddedModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(StoresViewModel.IsAnyModalOpen)) return;
        if (_embeddedModel?.IsAnyModalOpen == true)
        {
            _modalOrigin ??= TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
            Dispatcher.UIThread.Post(() =>
            {
                FitEmbeddedModals();
                var modal = this.GetVisualDescendants().OfType<Border>()
                    .FirstOrDefault(b => b.Classes.Contains("modal") && b.IsEffectivelyVisible);
                modal?.GetVisualDescendants().OfType<Button>()
                    .FirstOrDefault(b => b.IsEffectivelyVisible && b.IsEffectivelyEnabled)?.Focus(NavigationMethod.Tab);
            }, DispatcherPriority.Loaded);
        }
        else
        {
            _modalOrigin?.Focus(NavigationMethod.Tab);
            _modalOrigin = null;
        }
    }

    /// <summary>
    /// Clicking outside a modal closes it, the keyboard half of which is the
    /// shell's Escape.
    ///
    /// <para>Attached only to the two modals that INFORM. The consent modal has
    /// no scrim handler at all: a click that happened to land outside it must
    /// never be able to read as the consent its Continue button grants, so
    /// backing out of that one is explicit (ROADMAP §4.7 condition 3).</para>
    /// </summary>
    private void OnModalScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is StoresViewModel stores)
        {
            stores.CloseModalCommand.Execute(null);
            e.Handled = true;
        }
    }
}
