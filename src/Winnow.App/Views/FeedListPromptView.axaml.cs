using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Winnow.App.ViewModels.Lists;

namespace Winnow.App.Views;

public partial class FeedListPromptView : UserControl
{
    private InputElement? _returnFocus;

    public FeedListPromptView() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not ActionPromptViewModel prompt)
        {
            var target = _returnFocus;
            _returnFocus = null;
            Dispatcher.UIThread.Post(() =>
            {
                if (target is { IsEffectivelyVisible: true }) target.Focus(NavigationMethod.Tab);
            }, DispatcherPriority.Input);
            return;
        }
        _returnFocus = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as InputElement;
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(DataContext, prompt) || !IsEffectivelyVisible) return;
            PromptInput.Focus(NavigationMethod.Tab);
            PromptInput.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void OnPromptKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ActionPromptViewModel prompt) return;
        if (e.Key == Key.Escape)
        {
            prompt.CancelCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && e.Source is TextBox && prompt.ConfirmCommand.CanExecute(null))
        {
            prompt.ConfirmCommand.Execute(null);
            e.Handled = true;
        }
    }
}
