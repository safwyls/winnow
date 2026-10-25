using Winnow.Resolve;

namespace Winnow.App.Services;

public interface IMergeSuggestionRefresh
{
    /// <summary>Changes after every successful pass, including a pass with an unchanged pending count.</summary>
    long Revision { get; }
    Task<SoftMatchSweepReport> RefreshAsync(CancellationToken ct = default);
}
