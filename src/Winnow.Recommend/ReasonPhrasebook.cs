namespace Winnow.Recommend;

/// <summary>
/// Standalone primary reasons without terminating punctuation. Each list has a
/// token-free fallback for missing evidence; variants cite facts, never motives.
/// The builder chooses deterministically and supplies capitalisation and punctuation.
/// </summary>
internal static class ReasonPhrasebook
{
    public const string Fallback = "Picked for today's rotation";

    public static IReadOnlyList<string> Variants(ReasonSignal signal) => signal switch
    {
        ReasonSignal.PatchedSinceYouLeft =>
        [
            "\"{updateTitle}\" shipped after your last play",
            "Updated since your last play with \"{updateTitle}\"",
            "{updates} since you last played",
            "Your last play predates {updates}",
            "Updated since you last played",
            "A major update followed your last play",
            "New updates since your last play",
            "Patched after you last played",
        ],
        ReasonSignal.Bounced =>
        [
            "{minutes} played, last opened in {year}",
            "Last played in {year}, with {minutes} recorded",
            "{minutes} of recorded playtime",
            "You have played for {minutes}",
            "Previously played",
        ],
        ReasonSignal.Sampled =>
        [
            "Only {minutes} of recorded playtime",
            "Last played in {year}, after {minutes} total",
            "{minutes} played so far",
            "A brief start: {minutes} recorded",
            "Briefly played",
        ],
        ReasonSignal.NeverOpened =>
        [
            "Owned with no recorded launch",
            "No play recorded yet",
            "In your library, still unplayed",
            "No recorded playtime or launch date",
            "Waiting for a first recorded play",
        ],
        ReasonSignal.LaunchedUnmeasured =>
        [
            "Last launched in {year}, with no playtime recorded",
            "Launched {age} ago, with no measured playtime",
            "A launch in {year}, but no measured playtime",
            "Launched, with no playtime recorded",
        ],
        ReasonSignal.ProbablyDone =>
        [
            "{minutes} played, with no known updates since your last play",
            "No known updates since you left, after {minutes} played",
            "Ranked lower: {minutes} played and no known updates since",
            "Possibly finished, with no known updates since your last play",
        ],
        _ => [Fallback],
    };
}
