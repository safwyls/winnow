using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The details modal's IGDB reassignment control (TASK-89): search IGDB by
/// title, pick the right entry, and clear the choice again.
///
/// <para>Pure view-model tests over a fake seam -- no database, no network,
/// no Avalonia application. The seam is what makes these possible.</para>
/// </summary>
public sealed class IgdbMatchViewModelTests
{
    private const long WorkId = 42;

    private static IgdbCandidate Prey2006 => new(
        1234, "Prey", "//images.igdb.com/igdb/image/upload/t_cover_big/co1r76.jpg", 2006,
        ["Xbox 360", "PC (Microsoft Windows)"]);

    private static IgdbCandidate Prey2017 => new(
        5678, "Prey", "//images.igdb.com/igdb/image/upload/t_cover_big/co2abc.jpg", 2017,
        ["PlayStation 4"]);

    // ══ Search ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task A_search_lists_candidates_with_cover_year_and_platform()
    {
        var service = new FakeAssignmentService { Results = [Prey2006, Prey2017] };
        var vm = Build(service);

        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Equal("Prey", service.SearchedFor);
        Assert.True(vm.HasCandidates);
        Assert.Equal(2, vm.Candidates.Count);

        var first = vm.Candidates[0];
        Assert.Equal(1234, first.IgdbId);
        Assert.Equal("Prey", first.Name);
        Assert.True(first.HasDetailLine);
        Assert.Equal("2006 · ", first.YearText);
        Assert.Equal("Xbox 360, PC (Microsoft Windows)", first.PlatformsText);

        // The cover rides the existing image path: the asset id out of the URL.
        Assert.Equal("igdb", first.CoverKey?.Provider);
        Assert.Equal("co1r76", first.CoverKey?.Id);

        Assert.Equal("2017 · ", vm.Candidates[1].YearText);
        Assert.Equal("PlayStation 4", vm.Candidates[1].PlatformsText);
    }

    /// <summary>An entry with neither year nor platform draws no detail line.</summary>
    [Fact]
    public async Task A_candidate_with_no_year_and_no_platform_draws_no_detail_line()
    {
        var service = new FakeAssignmentService
        {
            Results = [new IgdbCandidate(9, "Unreleased", null, null, [])],
        };
        var vm = Build(service);

        await vm.SearchCommand.ExecuteAsync(null);

        var only = Assert.Single(vm.Candidates);
        Assert.False(only.HasDetailLine);
        Assert.Equal(string.Empty, only.YearText);
        Assert.Null(only.CoverKey);
    }

    /// <summary>
    /// A search that matched nothing and a search that failed are the same
    /// empty list, and neither reads as an error.
    /// </summary>
    [Fact]
    public async Task A_search_that_matched_nothing_says_so_and_is_not_a_problem()
    {
        var vm = Build(new FakeAssignmentService { Results = [] });

        Assert.False(vm.ShowNoMatches);

        await vm.SearchCommand.ExecuteAsync(null);

        Assert.True(vm.ShowNoMatches);
        Assert.False(vm.HasCandidates);
        Assert.False(vm.HasProblem);
    }

    [Fact]
    public void A_blank_title_is_not_searchable()
    {
        var vm = Build(new FakeAssignmentService(), title: "   ");

        Assert.False(vm.SearchCommand.CanExecute(null));

        vm.Query = "Prey";

        Assert.True(vm.SearchCommand.CanExecute(null));
    }

    /// <summary>The status field is words while the search runs and cleared when it returns.</summary>
    [Fact]
    public async Task The_status_field_is_cleared_when_the_search_returns()
    {
        var vm = Build(new FakeAssignmentService { Results = [Prey2017] });

        await vm.SearchCommand.ExecuteAsync(null);

        Assert.False(vm.HasStatus);
    }

    // ══ Assignment ══════════════════════════════════════════════════════════

    [Fact]
    public async Task Choosing_a_candidate_pins_it_and_reopens_the_modal()
    {
        var service = new FakeAssignmentService
        {
            Results = [Prey2017],
            Assignment = IgdbAssignmentOutcome.Assigned,
        };

        string? carried = null;
        var vm = Build(service, afterAssign: note =>
        {
            carried = note;
            return Task.CompletedTask;
        });

        await vm.SearchCommand.ExecuteAsync(null);
        await vm.AssignCommand.ExecuteAsync(vm.Candidates[0]);

        Assert.Equal(WorkId, service.AssignedWorkId);
        Assert.Equal(5678, service.AssignedIgdbId);
        Assert.True(vm.IsPinned);
        Assert.True(vm.ShowPinned);
        Assert.False(vm.IsOpen);
        Assert.False(vm.HasProblem);

        // The confirmation survives the reload the assignment triggers.
        Assert.NotNull(carried);
        Assert.Contains("Prey", carried);
    }

    /// <summary>
    /// Every refusal is a state the control renders, and each one says
    /// something different. Nothing is pinned and the candidate list stays
    /// on screen for a second try.
    /// </summary>
    [Theory]
    [InlineData(IgdbAssignmentOutcome.WorkNotFound)]
    [InlineData(IgdbAssignmentOutcome.MetadataUnavailable)]
    [InlineData(IgdbAssignmentOutcome.IgdbIdClaimedByAnotherWork)]
    [InlineData(IgdbAssignmentOutcome.Failed)]
    public async Task A_refusal_is_stated_and_nothing_is_pinned(IgdbAssignmentOutcome status)
    {
        var service = new FakeAssignmentService
        {
            Results = [Prey2017],
            Assignment = status,
        };

        var reopened = false;
        var vm = Build(service, afterAssign: _ =>
        {
            reopened = true;
            return Task.CompletedTask;
        });

        await vm.SearchCommand.ExecuteAsync(null);
        await vm.AssignCommand.ExecuteAsync(vm.Candidates[0]);

        Assert.True(vm.HasProblem);
        Assert.Equal(GameIgdbMatchCopy.ProblemFor(status), vm.Problem);
        Assert.False(vm.IsPinned);
        Assert.False(reopened);
        Assert.True(vm.HasCandidates);
        Assert.False(vm.HasStatus);
    }

    /// <summary>
    /// Four refusals, four distinct sentences. A shared "something went
    /// wrong" would make the one the user can act on -- another game
    /// already holds that entry -- indistinguishable from the three they
    /// cannot.
    /// </summary>
    [Fact]
    public void Each_refusal_has_its_own_sentence()
    {
        string[] sentences =
        [
            GameIgdbMatchCopy.ProblemFor(IgdbAssignmentOutcome.WorkNotFound),
            GameIgdbMatchCopy.ProblemFor(IgdbAssignmentOutcome.MetadataUnavailable),
            GameIgdbMatchCopy.ProblemFor(IgdbAssignmentOutcome.IgdbIdClaimedByAnotherWork),
            GameIgdbMatchCopy.ProblemFor(IgdbAssignmentOutcome.Failed),
        ];

        Assert.Equal(sentences.Length, sentences.Distinct().Count());
        Assert.DoesNotContain(sentences, string.IsNullOrWhiteSpace);
    }

    // ══ Clearing ════════════════════════════════════════════════════════════

    [Fact]
    public void A_work_on_automatic_resolution_offers_nothing_to_clear()
        => Assert.False(Build(new FakeAssignmentService()).ShowPinned);

    [Fact]
    public async Task A_pinned_work_can_be_returned_to_automatic_resolution()
    {
        var service = new FakeAssignmentService { ClearResult = true };
        var vm = Build(service, pin: new WorkIgdbPin
        {
            WorkId = WorkId,
            IgdbId = 5678,
            PinnedAt = new DateTime(2026, 9, 4, 0, 0, 0, DateTimeKind.Utc),
        });

        Assert.True(vm.ShowPinned);

        await vm.ClearCommand.ExecuteAsync(null);

        Assert.Equal(WorkId, service.ClearedWorkId);
        Assert.False(vm.IsPinned);
        Assert.False(vm.ShowPinned);
        Assert.False(vm.HasProblem);
        Assert.True(vm.HasNote);
    }

    /// <summary>A clear that did not land leaves the control in place for a retry.</summary>
    [Fact]
    public async Task A_clear_that_did_not_land_keeps_the_control()
    {
        var vm = Build(
            new FakeAssignmentService { ClearResult = false },
            pin: new WorkIgdbPin
            {
                WorkId = WorkId,
                IgdbId = 5678,
                PinnedAt = new DateTime(2026, 9, 4, 0, 0, 0, DateTimeKind.Utc),
            });

        await vm.ClearCommand.ExecuteAsync(null);

        Assert.True(vm.IsPinned);
        Assert.True(vm.HasProblem);
    }

    // ══ Disclosure ══════════════════════════════════════════════════════════

    [Fact]
    public void The_search_is_folded_away_until_it_is_asked_for()
    {
        var vm = Build(new FakeAssignmentService());

        Assert.False(vm.IsOpen);
        var closedLabel = vm.ToggleLabel;

        vm.ToggleCommand.Execute(null);

        Assert.True(vm.IsOpen);
        Assert.NotEqual(closedLabel, vm.ToggleLabel);
    }

    /// <summary>The search field is pre-filled with the title the library already shows.</summary>
    [Fact]
    public void The_field_opens_on_the_title_the_library_shows()
        => Assert.Equal("Prey", Build(new FakeAssignmentService()).Query);

    private static GameIgdbMatchViewModel Build(
        FakeAssignmentService service,
        string title = "Prey",
        WorkIgdbPin? pin = null,
        Func<string, Task>? afterAssign = null)
        => new(service, WorkId, title, pin: pin, afterAssign: afterAssign);

    /// <summary>
    /// Answers from canned values and records what it was asked, exactly as
    /// the real seam does: never throws, and a failure is a status or an
    /// empty list.
    /// </summary>
    private sealed class FakeAssignmentService : IIgdbAssignmentService
    {
        public IReadOnlyList<IgdbCandidate> Results { get; set; } = [];

        public IgdbAssignmentOutcome Assignment { get; set; } = IgdbAssignmentOutcome.Failed;

        public bool ClearResult { get; set; }

        public string? SearchedFor { get; private set; }

        public long? AssignedWorkId { get; private set; }

        public long? AssignedIgdbId { get; private set; }

        public long? ClearedWorkId { get; private set; }

        public Task<IReadOnlyList<IgdbCandidate>> SearchAsync(
            string title, CancellationToken ct = default)
        {
            SearchedFor = title;
            return Task.FromResult(Results);
        }

        public Task<IgdbAssignmentOutcome> AssignAsync(
            long workId, long igdbId, CancellationToken ct = default)
        {
            AssignedWorkId = workId;
            AssignedIgdbId = igdbId;
            return Task.FromResult(Assignment);
        }

        public Task<bool> ClearAsync(long workId, CancellationToken ct = default)
        {
            ClearedWorkId = workId;
            return Task.FromResult(ClearResult);
        }

        public Task<WorkIgdbPin?> GetPinAsync(long workId, CancellationToken ct = default)
            => Task.FromResult<WorkIgdbPin?>(null);
    }
}
