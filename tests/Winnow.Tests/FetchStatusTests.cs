using Winnow.App.ViewModels;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The rail's fetch status field: a plain object with no Dispatcher dependency,
/// driven here on the test thread. The field names what is left as a real count,
/// never appears when there is nothing to do, and disappears on completion or
/// after <see cref="FetchStatusViewModel.Clear"/>.
/// </summary>
public sealed class FetchStatusTests
{
    [Fact]
    public void A_pass_with_nothing_to_do_shows_nothing()
    {
        var status = new FetchStatusViewModel();

        status.Begin(0);

        Assert.False(status.IsActive);
    }

    [Fact]
    public void The_field_names_what_is_left_as_a_count()
    {
        var status = new FetchStatusViewModel();

        status.Begin(1247);

        Assert.True(status.IsActive);
        Assert.Equal("1,247", status.RemainingText);
        Assert.Contains("1,247", status.AutomationName);
        Assert.NotEmpty(status.Label);
        Assert.NotEmpty(status.RemainingNote);
    }

    [Fact]
    public void The_count_falls_as_the_pass_advances()
    {
        var status = new FetchStatusViewModel();

        status.Begin(80);
        status.Report(40);

        Assert.True(status.IsActive);
        Assert.Equal("40", status.RemainingText);
    }

    [Fact]
    public void The_field_disappears_on_completion()
    {
        var status = new FetchStatusViewModel();

        status.Begin(80);
        status.Report(0);

        Assert.False(status.IsActive);
    }

    [Fact]
    public void A_report_that_arrives_after_completion_does_not_bring_it_back()
    {
        var status = new FetchStatusViewModel();

        status.Begin(80);
        status.Clear();
        status.Report(40);

        Assert.False(status.IsActive);
    }

    [Fact]
    public void The_singular_and_the_plural_are_both_written()
    {
        var status = new FetchStatusViewModel();

        status.Begin(1);
        var one = status.RemainingNote;

        status.Report(2);
        var many = status.RemainingNote;

        Assert.NotEqual(one, many);
    }

}
