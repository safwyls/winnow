using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Winnow.App.ViewModels.Lists;

/// <summary>
/// Inline prompt strip above the grid for transient actions: naming a list,
/// picking a list to add to, or confirming a delete. Replaces the cut bar while active.
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
