namespace Winnow.Api.Contracts.Feed;

public sealed record FeedImpressionRequest(long ReleaseId, string ShelfId);
public sealed record FeedFeedbackRequest(long ReleaseId, Winnow.App.Services.FeedVerdictKind Kind);
