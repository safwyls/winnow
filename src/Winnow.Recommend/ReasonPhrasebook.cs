namespace Winnow.Recommend;

/// <summary>Which half of the sentence a template is written for.</summary>
internal enum ReasonClause
{
    /// <summary>Opens the sentence. Capitalised, no leading joiner, no terminator.</summary>
    Primary,

    /// <summary>Continues the sentence. Carries its OWN leading joiner and no terminator.</summary>
    Secondary,
}

/// <summary>
/// Every phrasing a reason may take, keyed by signal and clause. All of the
/// feed's copy lives here and nowhere else, so rewording the feed never touches
/// the model that ranked it.
///
/// <para>Contract for whoever writes these strings:</para>
/// <list type="bullet">
/// <item><description>A primary template is a sentence opening: capitalised,
/// NO leading joiner, NO terminating punctuation.</description></item>
/// <item><description>A secondary template continues the same sentence and
/// carries its own leading joiner (", and …", " — …", ", though …"). No
/// terminating punctuation.</description></item>
/// <item><description>A secondary may NOT open on a bare relative pronoun.
/// Any primary may precede it and most of them end on a verb, so ", which …"
/// attaches to the wrong word ("…then stopped, which you own twice over").
/// A participle, an appositive or a fresh coordinate clause carries its own
/// footing; a relative pronoun borrows one that may not be
/// there.</description></item>
/// <item><description>Tokens are <c>{title} {store} {minutes} {year} {age}
/// {updates} {updateCount} {updateTitle} {episodes} {stores} {facet}
/// {strongFacet}</c>. A
/// variant whose tokens cannot all be resolved for a given game is skipped, so
/// every list MUST contain at least one variant using no tokens at
/// all.</description></item>
/// <item><description>Several variants per signal is the point: the variant is
/// chosen deterministically from the game's own release id, so the feed is
/// stable across reloads while two cards in one session do not read as
/// siblings.</description></item>
/// <item><description>A variant may only claim what the engine has proved
/// about that game, never a rank, a maximum, a uniqueness or a quantified
/// share of the library. The variant is chosen per card with no knowledge
/// of what any other card said, so an absolute claim can render on two
/// cards in one screen (observed 2026-08-28: two adjacent cards both
/// called a descriptor "your deepest pile"). <c>{strongFacet}</c> is the
/// gated form of <c>{facet}</c>: it resolves only when the descriptor's
/// weight is at least 60% of the user's single strongest descriptor,
/// licensing "one of your deepest piles" but not "your deepest
/// pile".</description></item>
/// </list>
/// </summary>
internal static class ReasonPhrasebook
{
    /// <summary>Last-resort opening when a signal has no usable variant at all.</summary>
    public const string Fallback = "Nothing in its history stands out, so the rotation picked it today";

    /// <summary>Templates for one signal in one clause position, in no significant order.</summary>
    public static IReadOnlyList<string> Variants(ReasonSignal signal, ReasonClause clause)
        => clause == ReasonClause.Primary ? Primary(signal) : Secondary(signal);

    private static IReadOnlyList<string> Primary(ReasonSignal signal) => signal switch
    {
        ReasonSignal.PatchedSinceYouLeft =>
        [
            "\"{updateTitle}\" shipped after your last session",
            "You have not seen \"{updateTitle}\", which arrived after you left",
            "{updates} landed here since you last played",
            "This is not the game you put down, {updates} arrived after you left",
            "The patch notes you have not read start at \"{updateTitle}\"",
            "A major update shipped after you stopped playing",
        ],
        ReasonSignal.Bounced =>
        [
            "{minutes} in, well past the refund line, then nothing",
            "You gave this {minutes} in {year} and did not come back",
            "Something held your attention for {minutes}, then stopped",
            "{minutes} of yours went into this before you drifted off",
            "You went well past the refund line here and stopped anyway",
        ],
        ReasonSignal.Sampled =>
        [
            "{minutes} and you were done with it",
            "You opened this in {year}, gave it {minutes}, and closed it for good",
            "{minutes} is the whole of your history with this game",
            "A brief look, {minutes}, and nothing after",
            "Barely opened, and never for long",
        ],
        ReasonSignal.NeverOpened =>
        [
            "Bought and never once launched",
            "You own this and have never met it",
            "Zero minutes, no launch date, nothing recorded here at all",
            "Still sealed since the day it arrived",
            "This has been waiting since you bought it",
        ],
        ReasonSignal.LaunchedUnmeasured =>
        [
            "You opened this in {year}, and no store recorded a minute of it",
            "Launched {age} ago, with zero minutes measured against it",
            "The record shows a launch in {year} and not one minute after it",
            "There is a launch date here and not one measured minute to go with it",
        ],
        ReasonSignal.ProbablyDone =>
        [
            "You gave this {minutes} and left {age} ago, and nothing has shipped since to call you back",
            "{minutes} was your answer {age} ago, and nothing since has argued with it",
            "Ranked low on purpose: {minutes} of yours, and nothing has shipped since",
            "Winnow has watched this one and found nothing new, so you were probably right to be done",
        ],
        _ => [Fallback],
    };

    private static IReadOnlyList<string> Secondary(ReasonSignal signal) => signal switch
    {
        ReasonSignal.TriedToLikeIt =>
        [
            ", and you went back {episodes} times before giving up on it",
            ", spread over {episodes} sittings rather than one",
            ", and it took {episodes} tries before you stopped",
            ", and you kept coming back to it",
        ],
        ReasonSignal.TasteMatch =>
        [
            ", and you have real hours in {facet} games",
            ", filed under {facet}, a corner of your library you actually play",
            ", sitting in {facet} alongside games you gave real time to",
            ", and {strongFacet} is one of your deepest piles",
            ", landing in {strongFacet}, a kind of game you keep coming back to",
            ", and it sits squarely in what you actually play",
        ],
        ReasonSignal.BoughtTwice =>
        [
            ", and you bought it on {stores} different stores",
            ", paid for twice across {stores} stores",
            ", owned {stores} times over, probably from a bundle",
            ", and you own it more than once",
        ],
        ReasonSignal.Installed =>
        [
            ", and it is already on your disk",
            ", installed right now, so there is nothing in the way",
            ", installed and idle on your disk",
            ", and nothing needs downloading first",
        ],
        ReasonSignal.Dormant =>
        [
            ", untouched for {age}",
            ", and nobody has opened it in {age}",
            ", quiet for {age} now",
            ", and it has sat untouched ever since",
        ],
        ReasonSignal.UndatedDormancy =>
        [
            ", from before Steam kept last-played dates at all",
            ", and there is no date on it, which puts it before 2009",
            ", old enough that Steam never recorded when",
        ],
        ReasonSignal.OnlineOnlyMismatch =>
        [
            ", though it needs other people and you play alone",
            ", but it is online-only and your hours are not",
            ", though a game that needs a lobby is a poor fit for you",
        ],
        ReasonSignal.SoloOnlyMismatch =>
        [
            ", though it is single-player and you play with people",
            ", but nearly everything you play is online and this one is not",
            ", though solo games are not where your time goes",
        ],
        ReasonSignal.PlayedRecently =>
        [
            ", though you played it {age} ago, so hardly forgotten",
            ", though {age} is no time at all to have been away",
            ", though this one is still in rotation for you",
        ],
        ReasonSignal.ShownRecently =>
        [
            ", though the feed put it in front of you a day or two ago",
            ", shown here recently and rotated down since",
            ", though you have seen this card lately",
        ],
        _ => [],
    };
}
