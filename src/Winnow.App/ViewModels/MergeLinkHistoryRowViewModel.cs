using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Winnow.Core.Identity;

namespace Winnow.App.ViewModels;

/// <summary>
/// One link act, as the history list reads it: which titles were grouped under
/// which, and when. Retraction is an ordinary act, so a row is never terminal:
/// the same group can be linked, retracted and linked again any number of
/// times, and nothing on this row ever refuses a second attempt.
/// </summary>
public partial class MergeLinkHistoryRowViewModel : ObservableObject
{
    public MergeLinkHistoryRowViewModel(
        IdentityAct act,
        string parentTitle,
        IReadOnlyList<string> childTitles,
        bool isLive,
        DateTime? retractedAt)
    {
        ArgumentNullException.ThrowIfNull(act);
        ArgumentNullException.ThrowIfNull(childTitles);

        ActId = act.Id;
        PerformedAt = act.PerformedAt;
        ParentTitle = parentTitle;
        ChildTitles = childTitles;
        IsLive = isLive;
        RetractedAt = retractedAt;
    }

    /// <summary>The <c>identity_acts.id</c> a retraction reverses.</summary>
    public long ActId { get; }

    /// <summary>When the act was recorded, in UTC.</summary>
    public DateTime PerformedAt { get; }

    /// <summary>The title the group is known by.</summary>
    public string ParentTitle { get; }

    /// <summary>The titles linked under it, in work id order.</summary>
    public IReadOnlyList<string> ChildTitles { get; }

    /// <summary>True while the act still has live links, which is what makes it retractable.</summary>
    public bool IsLive { get; }

    /// <summary>When the act was retracted, or null while it stands.</summary>
    public DateTime? RetractedAt { get; }

    /// <summary>Latched across an in-flight retraction so a double click cannot ask twice.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRetract))]
    public partial bool IsRetracting { get; set; }

    /// <summary>The row in user language: which titles this act grouped.</summary>
    public string Description => ChildTitles.Count == 1
        ? string.Format(
            CultureInfo.CurrentCulture, MergeCopy.LinkRowFormat, ChildTitles[0], ParentTitle)
        : string.Format(
            CultureInfo.CurrentCulture,
            MergeCopy.LinkRowManyFormat,
            ChildTitlesText,
            ParentTitle);

    /// <summary>Every linked title, listed.</summary>
    public string ChildTitlesText => string.Join(MergeCopy.MemberSeparator, ChildTitles);

    /// <summary>How many titles the act grouped, in the data face.</summary>
    public string ChildCountText =>
        ChildTitles.Count.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>Small uppercase label before the date.</summary>
    public string LinkedAtLabel => MergeCopy.LinkedAtLabel;

    /// <summary>When the act was recorded, local, in the data face.</summary>
    public string LinkedAtText => FormatStamp(PerformedAt);

    /// <summary>Small uppercase label marking a row that has been retracted.</summary>
    public string RetractedLabelText => MergeCopy.RetractedLabel;

    /// <summary>When the act was retracted, local, in the data face.</summary>
    public string RetractedAtText => FormatStamp(RetractedAt);

    /// <summary>True when the act no longer stands.</summary>
    public bool IsRetracted => !IsLive;

    /// <summary>A standing act can be retracted; a retracted one has no control at all.</summary>
    public bool CanRetract => IsLive && !IsRetracting;

    /// <summary>Label on the retract control.</summary>
    public string RetractButtonText => MergeCopy.RetractButton;

    /// <summary>Tooltip on the retract control.</summary>
    public string RetractTooltip => MergeCopy.RetractTooltip;

    /// <summary>
    /// Names the group rather than the verb: a static label repeated down the
    /// list is one target a screen reader cannot distinguish.
    /// </summary>
    public string RetractAutomationName => string.Format(
        CultureInfo.CurrentCulture, MergeCopy.RetractAutomationFormat, Description);

    private static string FormatStamp(DateTime? stamp)
        => stamp is { } value
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
                .ToLocalTime()
                .ToString("d MMM yyyy", CultureInfo.InvariantCulture)
            : "—";
}
