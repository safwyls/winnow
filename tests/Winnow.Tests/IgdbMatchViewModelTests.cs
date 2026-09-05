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
        var vm = Build(service, afterChange: note =>
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
        var vm = Build(service, afterChange: _ =>
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

    // ══ The same-game offer (TASK-122) ══════════════════════════════════════

    private const long HolderWorkId = 77;

    private static IgdbClaimingGame Holder => new(
        HolderWorkId, "Prey", "https://images.igdb.com/igdb/image/upload/t_cover_big/co2abc.jpg", 2017);

    /// <summary>
    /// The one refusal the user can act on becomes an offer. It names the
    /// game that already holds the entry and shows its cover and year, so
    /// the user can judge whether it really is the same game before
    /// answering.
    /// </summary>
    [Fact]
    public async Task A_claimed_entry_offers_to_link_and_names_the_holder()
    {
        var service = new FakeAssignmentService
        {
            Results = [Prey2017],
            Assignment = IgdbAssignmentOutcome.IgdbIdClaimedByAnotherWork,
            Claimant = Holder,
        };

        var vm = Build(service, linkSameGame: _ => Task.FromResult(true));

        await vm.SearchCommand.ExecuteAsync(null);
        await vm.AssignCommand.ExecuteAsync(vm.Candidates[0]);

        Assert.Equal(5678, service.ClaimLookupIgdbId);
        Assert.True(vm.ShowClaim);
        Assert.NotNull(vm.Claim);
        Assert.Equal(HolderWorkId, vm.Claim.WorkId);
        Assert.Equal("Prey", vm.Claim.Name);
        Assert.Contains("Prey", vm.Claim.Headline);
        Assert.True(vm.Claim.HasYear);
        Assert.Equal("2017", vm.Claim.YearText);
        Assert.Equal("igdb", vm.Claim.CoverKey?.Provider);
        Assert.Equal("co2abc", vm.Claim.CoverKey?.Id);

        // An offer, not a failure: no Amber sentence stands beside it, and
        // the candidate list is still there.
        Assert.False(vm.HasProblem);
        Assert.True(vm.HasCandidates);
        Assert.False(vm.IsPinned);
    }

    /// <summary>
    /// Accepting links this work under the one that holds the entry, and
    /// pins nothing: works.igdb_id is UNIQUE, so pinning the child is the
    /// very thing the constraint refused. The confirmation survives the
    /// reload the link triggers.
    /// </summary>
    [Fact]
    public async Task Accepting_the_offer_links_under_the_holder_and_pins_nothing()
    {
        var service = new FakeAssignmentService
        {
            Results = [Prey2017],
            Assignment = IgdbAssignmentOutcome.IgdbIdClaimedByAnotherWork,
            Claimant = Holder,
        };

        long? parent = null;
        string? carried = null;
        var vm = Build(
            service,
            afterChange: note =>
            {
                carried = note;
                return Task.CompletedTask;
            },
            linkSameGame: parentWorkId =>
            {
                parent = parentWorkId;
                return Task.FromResult(true);
            });

        await vm.SearchCommand.ExecuteAsync(null);
        await vm.AssignCommand.ExecuteAsync(vm.Candidates[0]);
        await vm.LinkClaimCommand.ExecuteAsync(null);

        // The holder is the PARENT: it is the work carrying the igdb_id.
        Assert.Equal(HolderWorkId, parent);

        Assert.False(vm.ShowClaim);
        Assert.False(vm.IsOpen);
        Assert.False(vm.HasProblem);
        Assert.False(vm.HasStatus);

        // Nothing was pinned on the way through: the assignment was refused,
        // and the link is the whole answer.
        Assert.False(vm.IsPinned);
        Assert.Null(vm.Claim);

        Assert.NotNull(carried);
        Assert.Contains("Prey", carried);
    }

    /// <summary>
    /// Declining writes nothing at all — no pin, no link — and restores the
    /// refusal sentence, so the user still knows why the assignment did not
    /// land. The candidate list stays for another try.
    /// </summary>
    [Fact]
    public async Task Declining_the_offer_writes_nothing_and_restores_the_refusal()
    {
        var service = new FakeAssignmentService
        {
            Results = [Prey2017],
            Assignment = IgdbAssignmentOutcome.IgdbIdClaimedByAnotherWork,
            Claimant = Holder,
        };

        var linked = false;
        var reopened = false;
        var vm = Build(
            service,
            afterChange: _ =>
            {
                reopened = true;
                return Task.CompletedTask;
            },
            linkSameGame: _ =>
            {
                linked = true;
                return Task.FromResult(true);
            });

        // Opened the way the user opens it, so the disclosure staying open
        // through the refusal and the decline is what the test observes.
        vm.ToggleCommand.Execute(null);
        await vm.SearchCommand.ExecuteAsync(null);
        await vm.AssignCommand.ExecuteAsync(vm.Candidates[0]);
        vm.DeclineClaimCommand.Execute(null);

        Assert.False(linked);
        Assert.False(reopened);
        Assert.False(vm.ShowClaim);
        Assert.Null(vm.Claim);
        Assert.False(vm.IsPinned);
        Assert.True(vm.IsOpen);
        Assert.True(vm.HasCandidates);
        Assert.Equal(
            GameIgdbMatchCopy.ProblemFor(IgdbAssignmentOutcome.IgdbIdClaimedByAnotherWork),
            vm.Problem);
    }

    /// <summary>
    /// The offer is additive. With no link path wired, no holder to name, or
    /// a holder that resolves to this same work, the collision degrades to
    /// exactly the sentence it drew before the offer existed.
    /// </summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task A_collision_with_nothing_to_offer_stays_a_plain_refusal(
        bool wireLink, bool nameHolder)
    {
        var service = new FakeAssignmentService
        {
            Results = [Prey2017],
            Assignment = IgdbAssignmentOutcome.IgdbIdClaimedByAnotherWork,
            Claimant = nameHolder ? Holder : null,
        };

        var vm = Build(
            service,
            linkSameGame: wireLink ? _ => Task.FromResult(true) : null);

        await vm.SearchCommand.ExecuteAsync(null);
        await vm.AssignCommand.ExecuteAsync(vm.Candidates[0]);

        Assert.False(vm.ShowClaim);
        Assert.Equal(
            GameIgdbMatchCopy.ProblemFor(IgdbAssignmentOutcome.IgdbIdClaimedByAnotherWork),
            vm.Problem);
    }

    /// <summary>
    /// A holder that IS this work is not a collision anyone can act on, and
    /// linking a work to itself is not a thing. The bare sentence stands.
    /// </summary>
    [Fact]
    public async Task A_holder_that_is_this_same_work_offers_nothing()
    {
        var service = new FakeAssignmentService
        {
            Results = [Prey2017],
            Assignment = IgdbAssignmentOutcome.IgdbIdClaimedByAnotherWork,
            Claimant = new IgdbClaimingGame(WorkId, "Prey", null, 2017),
        };

        var vm = Build(service, linkSameGame: _ => Task.FromResult(true));

        await vm.SearchCommand.ExecuteAsync(null);
        await vm.AssignCommand.ExecuteAsync(vm.Candidates[0]);

        Assert.False(vm.ShowClaim);
        Assert.True(vm.HasProblem);
    }

    /// <summary>
    /// A link write that did not land keeps the offer on screen under its own
    /// Amber sentence, so the answer the user already gave is not thrown
    /// away by a failed write.
    /// </summary>
    [Fact]
    public async Task A_link_that_did_not_land_keeps_the_offer()
    {
        var service = new FakeAssignmentService
        {
            Results = [Prey2017],
            Assignment = IgdbAssignmentOutcome.IgdbIdClaimedByAnotherWork,
            Claimant = Holder,
        };

        var reopened = false;
        var vm = Build(
            service,
            afterChange: _ =>
            {
                reopened = true;
                return Task.CompletedTask;
            },
            linkSameGame: _ => Task.FromResult(false));

        await vm.SearchCommand.ExecuteAsync(null);
        await vm.AssignCommand.ExecuteAsync(vm.Candidates[0]);
        await vm.LinkClaimCommand.ExecuteAsync(null);

        Assert.False(reopened);
        Assert.True(vm.ShowClaim);
        Assert.Equal(GameIgdbMatchCopy.LinkFailedText, vm.Problem);
        Assert.False(vm.HasStatus);
    }

    /// <summary>Every sentence and label the offer draws says something, and no two say the same thing.</summary>
    [Fact]
    public void The_offer_has_copy_of_its_own()
    {
        string[] copy =
        [
            GameIgdbMatchCopy.ClaimHeadline("Prey"),
            GameIgdbMatchCopy.ClaimLinkLabel,
            GameIgdbMatchCopy.ClaimDeclineLabel,
            GameIgdbMatchCopy.ClaimLinkAutomationName("Prey"),
            GameIgdbMatchCopy.LinkingStatus,
            GameIgdbMatchCopy.LinkedNote("Prey"),
            GameIgdbMatchCopy.LinkFailedText,
        ];

        Assert.Equal(copy.Length, copy.Distinct().Count());
        Assert.DoesNotContain(copy, string.IsNullOrWhiteSpace);
        Assert.DoesNotContain(copy, c => c.Contains("TODO", StringComparison.Ordinal));
        Assert.DoesNotContain(copy, c => c.Contains("PLACEHOLDER", StringComparison.Ordinal));
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

    // ══ Searching by id (TASK-120) ══════════════════════════════════════════

    /// <summary>
    /// An id IGDB knows leads the list, marked as an id match, and the title
    /// search still runs beside it.
    /// </summary>
    [Fact]
    public async Task An_id_match_leads_the_list_and_is_marked_as_one()
    {
        var service = new FakeAssignmentService
        {
            IdResult = Prey2017,
            Results = [Prey2006],
        };
        var vm = Build(service, title: "5678");

        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Equal(5678, service.LookedUpIgdbId);
        Assert.Equal("5678", service.SearchedFor);
        Assert.Equal(2, vm.Candidates.Count);

        Assert.Equal(5678, vm.Candidates[0].IgdbId);
        Assert.True(vm.Candidates[0].IsIdMatch);
        Assert.False(vm.Candidates[1].IsIdMatch);
        Assert.False(vm.ShowIdMiss);
    }

    /// <summary>
    /// The id lookup once returned no platforms, so the id row drew a year
    /// alone while every title row beside it drew a year and a platform list.
    /// Both rows are the same view model over the same candidate — a row is
    /// only ever as complete as the candidate handed to it.
    /// </summary>
    [Fact]
    public async Task An_id_match_draws_the_same_detail_line_as_a_title_result()
    {
        var service = new FakeAssignmentService
        {
            IdResult = Prey2017,
            Results = [Prey2006],
        };
        var vm = Build(service, title: "5678");

        await vm.SearchCommand.ExecuteAsync(null);

        var idRow = vm.Candidates[0];
        var titleRow = vm.Candidates[1];

        Assert.True(idRow.IsIdMatch);
        Assert.True(idRow.HasYear);
        Assert.True(idRow.HasPlatforms);
        Assert.True(idRow.HasDetailLine);
        Assert.Equal("PlayStation 4", idRow.PlatformsText);
        Assert.Equal("PlayStation 4", idRow.PlatformsTooltip);

        Assert.Equal(titleRow.HasYear, idRow.HasYear);
        Assert.Equal(titleRow.HasPlatforms, idRow.HasPlatforms);
        Assert.Equal(titleRow.HasDetailLine, idRow.HasDetailLine);
    }

    /// <summary>
    /// The id hit is not listed twice when the title search returns it too.
    /// </summary>
    [Fact]
    public async Task An_id_match_is_not_repeated_by_the_title_search()
    {
        var service = new FakeAssignmentService
        {
            IdResult = Prey2017,
            Results = [Prey2017, Prey2006],
        };
        var vm = Build(service, title: "5678");

        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Candidates.Count);
        Assert.Equal([5678, 1234], vm.Candidates.Select(c => c.IgdbId));
        Assert.True(vm.Candidates[0].IsIdMatch);
    }

    /// <summary>
    /// A title that merely begins with digits is not an id, so the id lookup
    /// is never asked and the title stays findable by name. This is the case
    /// that forbids routing every numeric-looking query to the id path.
    /// </summary>
    [Theory]
    [InlineData("1979 Revolution")]
    [InlineData("7 Days to Die")]
    public async Task A_title_that_starts_with_digits_never_reaches_the_id_lookup(string title)
    {
        var service = new FakeAssignmentService { Results = [Prey2006] };
        var vm = Build(service, title: title);

        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Null(service.LookedUpIgdbId);
        Assert.Equal(title, service.SearchedFor);
        Assert.Single(vm.Candidates);
        Assert.False(vm.Candidates[0].IsIdMatch);
        Assert.False(vm.ShowIdMiss);
    }

    /// <summary>
    /// An id IGDB has no record for says so on its own line, and does not
    /// read as a failed search — the title results beside it still stand.
    /// </summary>
    [Fact]
    public async Task An_id_that_matches_nothing_says_so_beside_the_title_results()
    {
        var service = new FakeAssignmentService
        {
            IdResult = null,
            Results = [Prey2006],
        };
        var vm = Build(service, title: "999999");

        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Equal(999999, service.LookedUpIgdbId);
        Assert.True(vm.ShowIdMiss);
        Assert.Single(vm.Candidates);
        Assert.False(vm.Candidates[0].IsIdMatch);
    }

    private static GameIgdbMatchViewModel Build(
        FakeAssignmentService service,
        string title = "Prey",
        WorkIgdbPin? pin = null,
        Func<string, Task>? afterChange = null,
        Func<long, Task<bool>>? linkSameGame = null)
        => new(
            service, WorkId, title,
            pin: pin, afterChange: afterChange, linkSameGame: linkSameGame);

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

        public IgdbCandidate? IdResult { get; set; }

        public long? LookedUpIgdbId { get; private set; }

        public Task<IgdbCandidate?> GetCandidateByIdAsync(
            long igdbId, CancellationToken ct = default)
        {
            LookedUpIgdbId = igdbId;
            return Task.FromResult(IdResult);
        }

        public Task<IgdbAssignmentOutcome> AssignAsync(
            long workId, long igdbId, CancellationToken ct = default)
        {
            AssignedWorkId = workId;
            AssignedIgdbId = igdbId;
            return Task.FromResult(Assignment);
        }

        public IgdbClaimingGame? Claimant { get; set; }

        public long? ClaimLookupIgdbId { get; private set; }

        public Task<IgdbClaimingGame?> FindClaimingGameAsync(
            long igdbId, CancellationToken ct = default)
        {
            ClaimLookupIgdbId = igdbId;
            return Task.FromResult(Claimant);
        }

        public Task<bool> ClearAsync(long workId, CancellationToken ct = default)
        {
            ClearedWorkId = workId;
            return Task.FromResult(ClearResult);
        }

        public Task<WorkIgdbPin?> GetPinAsync(long workId, CancellationToken ct = default)
            => Task.FromResult<WorkIgdbPin?>(null);

        // The bulk pin read belongs to the library load's cover precedence,
        // not to this control, so the fake answers the empty set.
        public Task<IReadOnlySet<long>> GetLivePinnedWorkIdsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlySet<long>>(new HashSet<long>());
    }
}
