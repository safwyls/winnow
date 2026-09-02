using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Winnow.Core.Identity;

namespace Winnow.App.ViewModels;

/// <summary>
/// One act on the history list. The consolidated game draws as the row's
/// headline, the titles consolidated into it as subtext beneath, and the
/// relation (LINKED or GROUPED) and date on the meta line below. The list
/// holds only acts still in force; undoing one removes its row rather than
/// striking it through. The same group can be linked, undone and linked
/// again any number of times.
/// </summary>
public partial class MergeLinkHistoryRowViewModel : ObservableObject
{
    public MergeLinkHistoryRowViewModel(
        IdentityAct act,
        string parentTitle,
        IReadOnlyList<string> childTitles,
        bool isExpansionAct = false)
    {
        ArgumentNullException.ThrowIfNull(act);
        ArgumentNullException.ThrowIfNull(childTitles);

        IsExpansionAct = isExpansionAct;
        ActId = act.Id;
        PerformedAt = act.PerformedAt;
        ParentTitle = parentTitle;
        ChildTitles = childTitles;
    }

    /// <summary>The <c>identity_acts.id</c> that an undo reverses.</summary>
    public long ActId { get; }

    /// <summary>When the act was recorded, in UTC.</summary>
    public DateTime PerformedAt { get; }

    /// <summary>The consolidated game, drawn as the row's headline.</summary>
    public string ParentTitle { get; }

    /// <summary>The titles consolidated into it, in work id order, drawn as
    /// subtext beneath the headline.</summary>
    public IReadOnlyList<string> ChildTitles { get; }

    /// <summary>Latched while an undo is in flight, so a double click cannot
    /// write twice.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUndo))]
    public partial bool IsUndoing { get; set; }

    /// <summary>
    /// True when every link this act wrote is an expansion. "All" rather than
    /// "any", because a same-game act can carry expansion links it displaced
    /// and re-parented, and the row must describe the act the user performed.
    ///
    /// <para>The row has to say which of the two relations it recorded,
    /// because they are different facts: a same-game link says two entries are
    /// one game, and an expansion link says one game extends another and
    /// changes no number. A row that read the same for both would invite the
    /// user to undo the wrong one.</para>
    /// </summary>
    public bool IsExpansionAct { get; }

    /// <summary>
    /// Flat sentence for automation, never drawn. The verb ("linked under" /
    /// "grouped under") survives here because a flat automation string has
    /// no position to carry the relation; the drawn row states it with a
    /// headline above its subtext and the LINKED / GROUPED meta label beside
    /// the date. An encoding must be decorative-redundant (§8), so the spoken
    /// form names the relation in words.
    /// </summary>
    public string SpokenDescription => string.Format(
        CultureInfo.CurrentCulture,
        IsExpansionAct ? ExpansionCopy.GroupRowFormat : MergeCopy.LinkRowFormat,
        ChildTitlesText,
        ParentTitle);

    /// <summary>Every linked title, comma-joined for the subtext line.</summary>
    public string ChildTitlesText => string.Join(MergeCopy.MemberSeparator, ChildTitles);

    /// <summary>True when the act names children. Withdraws the subtext line
    /// when it does not.</summary>
    public bool HasChildTitles => ChildTitles.Count > 0;

    /// <summary>Small uppercase label before the date.</summary>
    public string LinkedAtLabel =>
        IsExpansionAct ? ExpansionCopy.GroupedAtLabel : MergeCopy.LinkedAtLabel;

    /// <summary>When the act was recorded, local, in the data face.</summary>
    public string LinkedAtText => FormatStamp(PerformedAt);

    /// <summary>Every row on the list is in force, so the only thing that
    /// withdraws the undo control is an undo already in flight.</summary>
    public bool CanUndo => !IsUndoing;

    /// <summary>Label on the undo control.</summary>
    public string UndoButtonText => MergeCopy.UndoButton;

    /// <summary>Tooltip on the undo control.</summary>
    public string UndoTooltip => MergeCopy.UndoTooltip;

    /// <summary>
    /// Names the group rather than the verb: a static label repeated down the
    /// list is one target a screen reader cannot distinguish.
    /// </summary>
    public string UndoAutomationName => string.Format(
        CultureInfo.CurrentCulture, MergeCopy.UndoAutomationFormat, SpokenDescription);

    private static string FormatStamp(DateTime stamp)
        => DateTime.SpecifyKind(stamp, DateTimeKind.Utc)
            .ToLocalTime()
            .ToString("d MMM yyyy", CultureInfo.InvariantCulture);
}
