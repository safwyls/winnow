using System.Net;
using System.Text.Json;
using Xunit;

namespace Winnow.Tests.SteamStore;

/// <summary>
/// Verifies that the Steam store client requests the reviews block, projects
/// the filtered summary (label, percentage, count), falls back to the
/// unfiltered summary when the filtered one is absent, carries no figure
/// rather than a zero when the block is missing or empty, and isolates a
/// malformed reviews block so it costs the figure and nothing else.
/// </summary>
public sealed class SteamStoreReviewTests
{
    private const string AppId = "1245620";

    [Fact]
    public async Task The_query_asks_for_reviews()
    {
        using var host = new SteamStoreTestHost(SteamStoreTestHost.CapturedResponder());

        await host.Client.GetItemsAsync([AppId]);

        using var query = JsonDocument.Parse(host.Handler.Requests[0].InputJson);
        var data = query.RootElement.GetProperty("data_request");

        Assert.True(data.GetProperty("include_reviews").GetBoolean());

        // One extra boolean on the call Winnow already makes: the batch, the
        // keylessness and the other four blocks are untouched.
        Assert.Equal(20, data.GetProperty("include_tag_count").GetInt32());
        Assert.True(data.GetProperty("include_basic_info").GetBoolean());
        Assert.DoesNotContain(
            "key=", host.Handler.Requests[0].Uri.Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_filtered_summary_supplies_the_label_the_percentage_and_the_count()
    {
        using var host = new SteamStoreTestHost(Responder(WithReviews));

        var items = await host.Client.GetItemsAsync([AppId]);

        var reviews = items[AppId].Reviews;
        Assert.False(reviews.IsEmpty);
        Assert.Equal("Very Positive", reviews.Label);
        Assert.Equal(91, reviews.PercentPositive);
        Assert.Equal(41_203, reviews.ReviewCount);
        Assert.Equal(8, reviews.ReviewScore);
    }

    [Fact]
    public async Task The_unfiltered_summary_answers_when_the_filtered_one_is_absent()
    {
        using var host = new SteamStoreTestHost(Responder(UnfilteredOnly));

        var reviews = (await host.Client.GetItemsAsync([AppId]))[AppId].Reviews;

        Assert.Equal("Mostly Positive", reviews.Label);
        Assert.Equal(74, reviews.PercentPositive);
        Assert.Equal(512, reviews.ReviewCount);
    }

    [Fact]
    public async Task An_app_with_no_reviews_block_carries_no_figure_rather_than_a_zero()
    {
        using var host = new SteamStoreTestHost(Responder(
            """{"response":{"store_items":[{"id":1245620,"appid":1245620,"success":1,"visible":true,"name":"Elden Ring"}]}}"""));

        var reviews = (await host.Client.GetItemsAsync([AppId]))[AppId].Reviews;

        Assert.True(reviews.IsEmpty);
        Assert.Null(reviews.Label);
        Assert.Equal(0, reviews.ReviewCount);
    }

    [Fact]
    public async Task A_summary_with_no_reviews_behind_it_is_read_as_no_figure()
    {
        using var host = new SteamStoreTestHost(Responder(EmptySummary));

        var reviews = (await host.Client.GetItemsAsync([AppId]))[AppId].Reviews;

        Assert.True(reviews.IsEmpty);
    }

    [Fact]
    public async Task A_reviews_block_that_is_not_the_shape_we_know_costs_the_figure_and_nothing_else()
    {
        using var host = new SteamStoreTestHost(Responder(MalformedReviews));

        var item = (await host.Client.GetItemsAsync([AppId]))[AppId];

        Assert.True(item.Reviews.IsEmpty);
        Assert.Equal("Elden Ring", item.Name);
    }

    private static Func<RecordedStoreRequest, int, HttpResponseMessage> Responder(string body)
        => (request, _) => request.Endpoint == SteamStoreTestHost.GetItems
            ? FakeStoreHandler.Json(HttpStatusCode.OK, body)
            : FakeStoreHandler.Json(HttpStatusCode.NotFound, "{}");

    private const string WithReviews = """
        {"response":{"store_items":[{"id":1245620,"appid":1245620,"success":1,"visible":true,
          "name":"Elden Ring",
          "reviews":{
            "summary_filtered":{"review_count":41203,"percent_positive":91,
                                "review_score":8,"review_score_label":"Very Positive"},
            "summary_unfiltered":{"review_count":41500,"percent_positive":90,
                                  "review_score":8,"review_score_label":"Very Positive"}}}]}}
        """;

    private const string UnfilteredOnly = """
        {"response":{"store_items":[{"id":1245620,"appid":1245620,"success":1,"visible":true,
          "name":"Elden Ring",
          "reviews":{
            "summary_unfiltered":{"review_count":512,"percent_positive":74,
                                  "review_score":7,"review_score_label":"Mostly Positive"}}}]}}
        """;

    private const string EmptySummary = """
        {"response":{"store_items":[{"id":1245620,"appid":1245620,"success":1,"visible":true,
          "name":"Elden Ring",
          "reviews":{"summary_filtered":{"review_count":0,"percent_positive":0}}}]}}
        """;

    private const string MalformedReviews = """
        {"response":{"store_items":[{"id":1245620,"appid":1245620,"success":1,"visible":true,
          "name":"Elden Ring","reviews":"not an object"}]}}
        """;
}
