using CommunityToolkit.Mvvm.Input;

namespace Hoard.App.ViewModels.Filters;

/// <summary>
/// Where a rule on the cut bar came from — the one thing the bar could not say
/// until a live list could be left.
///
/// <para>Opening a live list pours its saved rules into the rail and the panel
/// (§12.2), which is what makes them editable in place. The cost of that is
/// that the rules then LOOK like rules the user set, and a user who leaves and
/// finds them still applied has been lied to. So each rule states its
/// provenance, and the treatment is the palette's existing grammar rather than
/// a new colour: <b>Volt means you chose this</b>, which is what Volt has always
/// meant (§2), and a neutral <c>Line</c> edge means the place you are standing
/// in chose it.</para>
/// </summary>
public enum FilterChipOrigin
{
    /// <summary>The user set this by hand. Volt-edged, like every chip before lists existed.</summary>
    User,

    /// <summary>
    /// The user set it by hand while inside a live list — so it is theirs, and
    /// it is also an unsaved edit to that list's rules. Volt-edged like any
    /// other choice of theirs; the cut bar's <c>Update list</c> / <c>Revert</c>
    /// pair is what says it has not been written down yet.
    /// </summary>
    Unsaved,

    /// <summary>Part of the open live list's saved rules. Neutral-edged.</summary>
    List,

    /// <summary>The open list itself — not a rule but the place the rules belong to.</summary>
    Context,
}

/// <summary>
/// One rule, restated above the grid where the result is.
///
/// <para>The cut bar exists because a filtered library that does not say it is
/// filtered is the single most expensive confusion this screen can produce — the
/// panel can be closed, the rail scrolled past, and the user is left looking at
/// 41 of 926 games with nothing on screen admitting it. Each chip carries its
/// own way out, so undoing one rule never means finding the control that set
/// it.</para>
///
/// <para><b>Chips are Volt-edged, never Flare.</b> A chip is a selection, which
/// is what Volt is for (§2). Flare marks unread updates and nothing else. The
/// one chip that is NOT Volt-edged is one the user did not make: see
/// <see cref="FilterChipOrigin"/>.</para>
/// </summary>
public sealed partial class FilterChipViewModel
{
    private readonly Action _remove;

    public FilterChipViewModel(
        string label,
        string dimension,
        Action remove,
        FilterChipOrigin origin = FilterChipOrigin.User)
    {
        Label = label;
        Dimension = dimension;
        Origin = origin;
        _remove = remove;
    }

    public string Label { get; }

    /// <summary>The group it came from — the chip's tooltip, so "Action" is legibly a genre.</summary>
    public string Dimension { get; }

    /// <summary>Who set this rule: the user, or the list they are inside.</summary>
    public FilterChipOrigin Origin { get; }

    /// <summary>Volt edge. A selection, and the only kind of rule Volt describes.</summary>
    public bool IsUserRule => Origin is FilterChipOrigin.User or FilterChipOrigin.Unsaved;

    /// <summary>Neutral edge: a rule the open live list contributed.</summary>
    public bool IsListRule => Origin == FilterChipOrigin.List;

    /// <summary>
    /// The open list itself, which leads the bar. It shows its kind — LIST or
    /// LIVE LIST — inside the chip rather than only in a tooltip, because "which
    /// live list am I in" is the question this whole strip failed to answer.
    /// </summary>
    public bool IsContext => Origin == FilterChipOrigin.Context;

    /// <summary>
    /// Screen-reader and hover text: which axis this chip is on, and — once a
    /// list is in the picture — who put it there. §8 asks for the ramp to be
    /// decorative-redundant; the same rule applies to a chip's edge colour.
    /// </summary>
    public string Description => Origin switch
    {
        FilterChipOrigin.Context =>
            $"{Dimension}: {Label} — leaving it takes its rules with it",
        FilterChipOrigin.List =>
            $"{Dimension}: {Label} — from this live list",
        FilterChipOrigin.Unsaved =>
            $"{Dimension}: {Label} — yours, not saved to this list",
        _ => $"{Dimension}: {Label}",
    };

    /// <summary>
    /// What the chip's cross does. On the context chip it is not "drop a rule"
    /// but "leave", and saying so is the one place the exit is named before it
    /// is taken.
    /// </summary>
    public string RemoveTip => IsContext ? "Leave this list" : "Drop this filter";

    [RelayCommand]
    private void Remove() => _remove();
}
