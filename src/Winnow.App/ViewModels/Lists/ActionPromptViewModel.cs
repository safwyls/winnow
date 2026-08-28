using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Winnow.App.ViewModels.Lists;

/// <summary>
/// The strip above the grid, in the one mode where it is asking rather than
/// reporting: name a live list, pick a list to add to, confirm a delete.
///
/// <para><b>It is a strip and not a flyout, deliberately.</b> Avalonia's global
/// <c>FocusAdorner</c> does not render inside a popup — a popup is its own root
/// and has no adorner layer — so every control in a menu here would have to draw
/// its own ring by hand, exactly as the display-preferences checkbox does and
/// as design-system §10.7 records for the detail panel. Keeping these three
/// tasks in the window's own tree keeps §8's focus ring free, keeps Tab order
/// linear, and puts the question directly above the thing it is about.</para>
///
/// <para>It replaces the cut bar rather than stacking under it: a transient task
/// is always about the cut the bar was describing, and two strips of chrome
/// between the command bar and the art is one too many.</para>
/// </summary>
public partial class ActionPromptViewModel : ObservableObject
{
    private readonly Func<ActionPromptViewModel, Task> _confirm;
    private readonly Action _cancel;
    private readonly Func<GameListViewModel, Task>? _choose;

    public ActionPromptViewModel(
        string question,
        string confirmLabel,
        Func<ActionPromptViewModel, Task> confirm,
        Action cancel,
        string? inputWatermark = null,
        string? initialText = null,
        string? note = null,
        bool isDestructive = false,
        IReadOnlyList<GameListViewModel>? choices = null,
        Func<GameListViewModel, Task>? choose = null)
    {
        Question = question;
        ConfirmLabel = confirmLabel;
        InputWatermark = inputWatermark ?? string.Empty;
        HasInput = inputWatermark is not null;
        Text = initialText ?? string.Empty;
        Note = note;
        IsDestructive = isDestructive;
        Choices = choices ?? [];
        _confirm = confirm;
        _cancel = cancel;
        _choose = choose;
    }

    /// <summary>What is being asked, in the app's voice: "Name this live list".</summary>
    public string Question { get; }

    /// <summary>The button says what happens: "Save", "Add", "Delete list".</summary>
    public string ConfirmLabel { get; }

    public string InputWatermark { get; }

    public bool HasInput { get; }

    /// <summary>One clause of consequence, where there is one worth stating.</summary>
    public string? Note { get; }

    public bool HasNote => Note is not null;

    /// <summary>Draws the confirm button in Danger rather than Volt.</summary>
    public bool IsDestructive { get; }

    /// <summary>Existing lists to add to. Empty on every other mode.</summary>
    public IReadOnlyList<GameListViewModel> Choices { get; }

    public bool HasChoices => Choices.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial string Text { get; set; }

    /// <summary>A prompt with a field needs something in it; one without is always ready.</summary>
    public bool CanConfirm => !HasInput || Text.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private Task Confirm() => _confirm(this);

    [RelayCommand]
    private void Cancel() => _cancel();

    [RelayCommand]
    private Task Choose(GameListViewModel? list)
        => list is null || _choose is null ? Task.CompletedTask : _choose(list);
}
