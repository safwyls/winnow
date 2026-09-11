using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Winnow.App.ViewModels.Lists;

/// <summary>
/// Shared modal prompt for naming a list, picking a membership target,
/// or confirming a delete.
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    [NotifyPropertyChangedFor(nameof(CanInteract))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChooseCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    public partial string? Problem { get; set; }

    public bool HasProblem => Problem is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    [NotifyPropertyChangedFor(nameof(CanInteract))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChooseCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial bool IsCompleted { get; private set; }

    public bool CanInteract => !IsBusy && !IsCompleted;

    /// <summary>A prompt with a field needs something in it; one without is always ready.</summary>
    public bool CanConfirm => CanInteract && (!HasInput || Text.Trim().Length > 0);

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private Task Confirm() => CanConfirm ? RunAsync(() => _confirm(this)) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private void Cancel()
    {
        if (!CanInteract) return;
        IsCompleted = true;
        _cancel();
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private Task Choose(GameListViewModel? list)
        => !CanInteract || list is null || _choose is null ? Task.CompletedTask : RunAsync(() => _choose(list));

    private async Task RunAsync(Func<Task> action)
    {
        IsBusy = true;
        Problem = null;
        try
        {
            await action();
            IsCompleted = Problem is null;
        }
        catch (Exception)
        {
            Problem = "Couldn't complete that. Try again.";
        }
        finally { IsBusy = false; }
    }
}
