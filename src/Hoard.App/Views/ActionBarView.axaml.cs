using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hoard.App.ViewModels;

namespace Hoard.App.Views;

/// <summary>
/// The cut bar and the prompt that replaces it. Two pieces of behaviour, both
/// about the keyboard.
/// </summary>
public partial class ActionBarView : UserControl
{
    private LibraryViewModel? _library;

    public ActionBarView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>
    /// Enter confirms, Escape cancels. A prompt with one field and two buttons
    /// that still needs a mouse is a prompt that has failed at the only thing it
    /// is for — and Escape has to work here for the same reason it works on the
    /// detail modal: whatever this strip is asking, backing out is free.
    /// </summary>
    private void OnPromptKeyDown(object? sender, KeyEventArgs e)
    {
        if (_library?.Prompt is not { } prompt)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter when prompt.ConfirmCommand.CanExecute(null):
                prompt.ConfirmCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Escape:
                prompt.CancelCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_library is not null)
        {
            _library.PropertyChanged -= OnLibraryPropertyChanged;
        }

        _library = DataContext as LibraryViewModel;

        if (_library is not null)
        {
            _library.PropertyChanged += OnLibraryPropertyChanged;
        }
    }

    /// <summary>
    /// Focus follows the prompt. §8 asks for the interface to be usable from
    /// the keyboard, and a field that appears without the caret in it makes the
    /// user hunt for a control that was put on screen by their own action.
    ///
    /// <para>Posted at input priority: the strip is still being laid out when
    /// the property changes, so there is nothing to focus into yet.</para>
    /// </summary>
    private void OnLibraryPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LibraryViewModel.Prompt) || _library?.Prompt is not { } prompt)
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                if (prompt.HasInput
                    && this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.IsVisible) is { } box)
                {
                    box.Focus(NavigationMethod.Tab);
                    box.SelectAll();
                    return;
                }

                // A confirm-only prompt (deleting a list) puts focus on the
                // first button instead, so Tab and Enter both land somewhere
                // sensible. Never on the destructive one by default.
                if (this.GetVisualDescendants().OfType<Button>().LastOrDefault(b => b.IsVisible) is { } cancel)
                {
                    cancel.Focus(NavigationMethod.Tab);
                }
            },
            DispatcherPriority.Input);
    }
}
