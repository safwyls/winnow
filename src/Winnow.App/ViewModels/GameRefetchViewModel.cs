using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.App.Services;

namespace Winnow.App.ViewModels;

/// <summary>
/// The More-menu row that re-asks both sources about this game, and the status
/// field that shows the outcome. Every outcome is a word and never a percentage,
/// because no total is knowable in advance (§8's indeterminate-progress rule).
///
/// <para>A refusal is <c>Amber</c> and a landed or running act is <c>TextDim</c>,
/// which is the register the rest of the panel uses for a status line. Only an
/// outcome that wrote something reopens the modal, carrying its confirmation
/// across — the same arrangement §10.9 describes for a landed IGDB assignment,
/// and for the same reason: the reception line and the cover are computed when
/// the library loads.</para>
///
/// <para>The busy flag means a second refetch cannot start while one is in
/// flight. Cancellation on the caller's token is rethrown, because the caller
/// asking to stop is not a failure.</para>
/// </summary>
public sealed partial class GameRefetchViewModel : ObservableObject
{
    private readonly IGameRefetch _service;
    private readonly long _workId;

    /// <summary>
    /// Reloads the library and reopens this modal on the same ownership,
    /// carrying the confirmation note across the rebuild. Null in a test.
    /// </summary>
    private readonly Func<string, Task>? _afterChange;

    private bool _busy;

    /// <param name="note">
    /// A carried confirmation from a previous refetch that triggered a reload.
    /// Survives the reopen so the user sees the outcome where they asked for it.
    /// </param>
    public GameRefetchViewModel(
        IGameRefetch service,
        long workId,
        Func<string, Task>? afterChange = null,
        string? note = null)
    {
        _service = service;
        _workId = workId;
        _afterChange = afterChange;
        Status = note;
    }

    /// <summary>The row's label in the More menu.</summary>
    public string MenuLabel => GameRefetchCopy.MenuLabel;

    /// <summary>Tooltip on the menu row.</summary>
    public string MenuTooltip => GameRefetchCopy.MenuTooltip;

    /// <summary>
    /// The status field, in words. Null at rest; a word while running; the
    /// outcome after it lands.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string? Status { get; set; }

    /// <summary>True when the outcome is a refusal. Drives the <c>Amber</c> ink class.</summary>
    [ObservableProperty]
    public partial bool IsProblem { get; set; }

    /// <summary>True when there is something to show in the status field.</summary>
    public bool HasStatus => !string.IsNullOrEmpty(Status);

    [RelayCommand]
    private async Task RefetchAsync(CancellationToken ct)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        try
        {
            IsProblem = false;
            Status = GameRefetchCopy.Running;

            var result = await _service.RefetchAsync(_workId, ct);
            var (text, problem) = Describe(result);

            Status = text;
            IsProblem = problem;

            if (result.Outcome == GameRefetchOutcome.Updated && _afterChange is not null)
            {
                await _afterChange(text);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            Status = GameRefetchCopy.Unreachable;
            IsProblem = true;
        }
        finally
        {
            _busy = false;
        }
    }

    private static (string Text, bool IsProblem) Describe(GameRefetchResult result) => result.Outcome switch
    {
        GameRefetchOutcome.Updated => (GameRefetchCopy.Updated, false),
        GameRefetchOutcome.NothingNew => (GameRefetchCopy.NothingNew, false),
        GameRefetchOutcome.NotConfigured => (GameRefetchCopy.NotConfigured, true),
        GameRefetchOutcome.NoSourceToAsk => (GameRefetchCopy.NoSourceToAsk, true),
        GameRefetchOutcome.WorkNotFound => (GameRefetchCopy.WorkNotFound, true),
        GameRefetchOutcome.TooSoon => (GameRefetchCopy.TooSoon(result.RetryAfter), true),
        _ => (GameRefetchCopy.Unreachable, true),
    };
}
