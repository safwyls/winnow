using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// TASK-104: adding a game starts from the executable on disk rather than from
/// an empty title box.
///
/// <para>Two halves, and the split is the point. The derivation is pure string
/// work over a path and three Win32 version-info fields, so every interesting
/// case — a launcher stub, an engine name, a build suffix, a file that says
/// nothing at all — is a table row here and needs no file, no window and no
/// network. The flow is a view-model test over fakes, with a real
/// <see cref="ManualEntryRepository"/> behind it so the claim that nothing is
/// written before Save is asserted against the database rather than against a
/// mock.</para>
/// </summary>
public sealed class ManualGameFromExecutableTests : IDisposable
{
    private readonly TempDatabase _db = new();
    private readonly IManualEntryRepository _manual;

    public ManualGameFromExecutableTests() => _manual = new ManualEntryRepository(_db.Factory);

    public void Dispose() => _db.Dispose();

    // ══ Derivation (AC2, AC7) ══════════════════════════════════════════════

    /// <summary>
    /// The ordinary case: the installer filled in the version info and it names
    /// the game.
    /// </summary>
    [Fact]
    public void The_file_description_names_the_game()
    {
        var facts = ExecutableFacts.Derive(
            @"D:\Games\HK\hk.exe", "Hollow Knight", "Hollow Knight", "Team Cherry");

        Assert.Equal("Hollow Knight", facts.Title);
        Assert.Equal(ExecutableTitleSource.FileDescription, facts.TitleSource);
        Assert.Equal("Team Cherry", facts.Publisher);
        Assert.Equal(@"D:\Games\HK", facts.InstallPath);
        Assert.True(facts.HasTitle);
    }

    /// <summary>The product name answers when the description is missing.</summary>
    [Fact]
    public void The_product_name_answers_when_there_is_no_description()
    {
        var facts = ExecutableFacts.Derive(@"D:\Games\CH\ch.exe", null, "Cuphead", "StudioMDHR");

        Assert.Equal("Cuphead", facts.Title);
        Assert.Equal(ExecutableTitleSource.ProductName, facts.TitleSource);
    }

    /// <summary>
    /// A launcher stub is the common failure the version info produces, and the
    /// folder is what actually names the game.
    /// </summary>
    [Fact]
    public void A_stub_description_falls_through_to_the_folder()
    {
        var facts = ExecutableFacts.Derive(
            @"D:\Games\Hollow Knight\launcher.exe", "Launcher", "Launcher", null);

        Assert.Equal("Hollow Knight", facts.Title);
        Assert.Equal(ExecutableTitleSource.FolderName, facts.TitleSource);
    }

    /// <summary>
    /// The Unreal layout: the executable sits three folders below the one that
    /// names the game, and every folder between is a container.
    /// </summary>
    [Fact]
    public void A_shipping_binary_walks_up_past_its_build_folders()
    {
        var facts = ExecutableFacts.Derive(
            @"D:\Games\Deep Rock Galactic\Binaries\Win64\FSD-Win64-Shipping.exe");

        Assert.Equal("Deep Rock Galactic", facts.Title);
        Assert.Equal(ExecutableTitleSource.FolderName, facts.TitleSource);
        Assert.Equal(@"D:\Games\Deep Rock Galactic\Binaries\Win64", facts.InstallPath);
    }

    /// <summary>An engine names itself, not the game built with it.</summary>
    [Theory]
    [InlineData("Unreal Engine 4")]
    [InlineData("UnrealEngine")]
    [InlineData("UE4")]
    [InlineData("Unity")]
    [InlineData("GameMaker")]
    public void An_engine_name_is_not_a_game_title(string description)
    {
        var facts = ExecutableFacts.Derive(
            @"D:\Games\Outer Wilds\OuterWilds.exe", description, description, null);

        Assert.Equal("Outer Wilds", facts.Title);
        Assert.Equal(ExecutableTitleSource.FolderName, facts.TitleSource);
    }

    /// <summary>Underscores stand in for spaces and build suffixes are noise.</summary>
    [Fact]
    public void Underscores_and_build_suffixes_are_cleaned_off()
    {
        var facts = ExecutableFacts.Derive(@"C:\g\hollow_knight_x64.exe");

        Assert.Equal("hollow knight", facts.Title);
        Assert.Equal(ExecutableTitleSource.FileName, facts.TitleSource);
    }

    /// <summary>
    /// The degrade case AC4 is about: a file whose version info, folder and
    /// name are all containers or stubs proposes no title at all, which is what
    /// leaves the form to be typed.
    /// </summary>
    [Fact]
    public void An_executable_that_says_nothing_proposes_no_title()
    {
        var facts = ExecutableFacts.Derive(@"C:\Games\game.exe");

        Assert.Null(facts.Title);
        Assert.False(facts.HasTitle);
        Assert.Equal(ExecutableTitleSource.None, facts.TitleSource);

        // The path is still a fact, and it is the one session monitoring needs.
        Assert.Equal(@"C:\Games", facts.InstallPath);
    }

    /// <summary>Unity's unset default is not a publisher.</summary>
    [Fact]
    public void An_unset_company_is_not_a_publisher()
    {
        var facts = ExecutableFacts.Derive(@"D:\Games\X\x.exe", "Fez", null, "DefaultCompany");

        Assert.Equal("Fez", facts.Title);
        Assert.Null(facts.Publisher);
    }

    // ══ The inspector, on real files (AC7) ═════════════════════════════════

    /// <summary>
    /// A file that is not a PE image has no version resource. It must come back
    /// as facts derived from the path, never as an exception in the UI.
    /// </summary>
    [Fact]
    public void A_file_that_is_not_a_program_still_yields_the_path()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winnow-exe-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(directory, "Chasm");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "notes.txt");
        File.WriteAllText(file, "not a program");

        try
        {
            var facts = new FileVersionInfoExecutableInspector().Inspect(file);

            Assert.Equal(file, facts.ExecutablePath);
            Assert.Equal(folder, facts.InstallPath);
            Assert.Equal("Chasm", facts.Title);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A path that is not there is a shrug, not a throw.</summary>
    [Fact]
    public void A_missing_file_does_not_throw()
    {
        var file = Path.Combine(
            Path.GetTempPath(), "winnow-missing-" + Guid.NewGuid().ToString("N"), "Braid", "b.exe");

        var facts = new FileVersionInfoExecutableInspector().Inspect(file);

        Assert.Equal("Braid", facts.Title);
    }

    // ══ The flow (AC1, AC3, AC4, AC5, AC6) ═════════════════════════════════

    /// <summary>
    /// AC1 and AC2: one gesture opens the form and the dialog, and the chosen
    /// file fills the executable, proposes the title and searches IGDB with it.
    /// </summary>
    [Fact]
    public async Task Browsing_fills_the_executable_proposes_the_title_and_searches()
    {
        var picker = new FakePicker(@"D:\Games\Celeste\Celeste.exe");
        var igdb = new FakeIgdb { Results = [Candidate(1, "Celeste", 2018)] };
        var vm = Build(picker, Inspector("Celeste"), igdb);

        await vm.BeginAddFromFileCommand.ExecuteAsync(null);

        Assert.True(vm.IsFormOpen);
        Assert.Equal(@"D:\Games\Celeste\Celeste.exe", vm.DraftExecutable);
        Assert.Equal("Celeste", vm.DraftTitle);
        Assert.True(vm.HasExecutableNote);

        Assert.Equal("Celeste", igdb.SearchedFor);
        Assert.True(vm.HasCandidates);
        Assert.Single(vm.Candidates);
        Assert.Equal(2018, vm.Candidates[0].FirstReleaseYear);
    }

    /// <summary>A dismissed dialog changes nothing at all.</summary>
    [Fact]
    public async Task A_cancelled_dialog_changes_nothing()
    {
        var igdb = new FakeIgdb();
        var vm = Build(new FakePicker(null), Inspector("Celeste"), igdb);

        vm.BeginAddCommand.Execute(null);
        await vm.BrowseForExecutableCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, vm.DraftExecutable);
        Assert.Equal(string.Empty, vm.DraftTitle);
        Assert.False(vm.HasExecutableNote);
        Assert.Null(igdb.SearchedFor);
    }

    /// <summary>
    /// AC3, the confirm half: choosing a candidate fills the form and writes
    /// nothing. The library is asked directly, because "nothing was written" is
    /// a claim about the database.
    /// </summary>
    [Fact]
    public async Task Choosing_a_candidate_fills_the_form_and_writes_nothing()
    {
        var igdb = new FakeIgdb { Results = [Candidate(1877, "Cyberpunk 2077", 2020)] };
        var vm = Build(new FakePicker(@"D:\Games\CP\bin\game.exe"), Inspector(null), igdb);

        await vm.BeginAddFromFileCommand.ExecuteAsync(null);

        // Nothing usable on the file, so the user types the search themselves.
        vm.DraftTitle = "Cyberpunk";
        await vm.SearchIgdbCommand.ExecuteAsync(null);

        vm.UseCandidateCommand.Execute(vm.Candidates[0]);

        Assert.Equal("Cyberpunk 2077", vm.DraftTitle);
        Assert.Equal("2020", vm.DraftYear);
        Assert.Equal("1877", vm.DraftIgdbId);
        Assert.True(vm.HasMatchNote);
        Assert.False(vm.HasCandidates);

        Assert.Empty(await _manual.GetAllAsync());
    }

    /// <summary>
    /// AC3, the override half: correcting the title and searching again is what
    /// overrides a wrong proposal, and the second search is the corrected one.
    /// </summary>
    [Fact]
    public async Task Correcting_the_title_and_searching_again_overrides_the_proposal()
    {
        var igdb = new FakeIgdb { Results = [Candidate(1, "Prey", 2006)] };
        var vm = Build(new FakePicker(@"D:\Games\Prey\prey.exe"), Inspector("Prey"), igdb);

        await vm.BeginAddFromFileCommand.ExecuteAsync(null);
        Assert.Equal("Prey", igdb.SearchedFor);

        vm.DraftTitle = "Prey 2017";
        await vm.SearchIgdbCommand.ExecuteAsync(null);

        Assert.Equal("Prey 2017", igdb.SearchedFor);
    }

    /// <summary>
    /// AC3, the reject half: dismissing folds the proposal away and leaves the
    /// form exactly as it was, and Save still adds the game.
    /// </summary>
    [Fact]
    public async Task Dismissing_the_proposal_leaves_the_form_as_typed()
    {
        var igdb = new FakeIgdb { Results = [Candidate(9, "Something Else", 1999)] };
        var vm = Build(new FakePicker(@"D:\Games\Iconoclasts\game.exe"), Inspector(null), igdb);

        await vm.BeginAddFromFileCommand.ExecuteAsync(null);

        vm.DraftTitle = "Iconoclasts";
        await vm.SearchIgdbCommand.ExecuteAsync(null);
        Assert.True(vm.HasCandidates);

        vm.DismissMatchCommand.Execute(null);

        Assert.False(vm.HasCandidates);
        Assert.False(vm.ShowNoMatches);
        Assert.Equal("Iconoclasts", vm.DraftTitle);
        Assert.Equal(@"D:\Games\Iconoclasts\game.exe", vm.DraftExecutable);
        Assert.Equal(string.Empty, vm.DraftIgdbId);

        await vm.SaveFormCommand.ExecuteAsync(null);

        var entry = Assert.Single(await _manual.GetAllAsync());
        Assert.Equal("Iconoclasts", entry.Title);
    }

    /// <summary>
    /// AC4: an executable that yields nothing does not dead-end. No title is
    /// proposed, no search runs, and the form is the one TASK-99 shipped.
    /// </summary>
    [Fact]
    public async Task An_executable_that_yields_nothing_still_saves_by_hand()
    {
        var igdb = new FakeIgdb();
        var vm = Build(new FakePicker(@"C:\Games\game.exe"), null, igdb);

        await vm.BeginAddFromFileCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, vm.DraftTitle);
        Assert.True(vm.HasExecutableNote);
        Assert.Null(igdb.SearchedFor);
        Assert.False(vm.SearchIgdbCommand.CanExecute(null));

        vm.DraftTitle = "A Disc I Own";
        vm.DraftPlatform = "PlayStation 2";
        await vm.SaveFormCommand.ExecuteAsync(null);

        var entry = Assert.Single(await _manual.GetAllAsync());
        Assert.Equal("A Disc I Own", entry.Title);
        Assert.Equal("PlayStation 2", entry.PlatformLabel);
    }

    /// <summary>
    /// AC5: the executable the user picked is the one stored, and its own
    /// folder is the install path session monitoring watches.
    /// </summary>
    [Fact]
    public async Task The_chosen_executable_is_the_one_stored()
    {
        var picked = @"D:\Games\Tunic\Tunic.exe";
        var vm = Build(new FakePicker(picked), Inspector("Tunic"), new FakeIgdb());

        await vm.BeginAddFromFileCommand.ExecuteAsync(null);
        await vm.SaveFormCommand.ExecuteAsync(null);

        var entry = Assert.Single(await _manual.GetAllAsync());
        Assert.Equal("Tunic", entry.Title);
        Assert.Equal(picked, entry.ExecutablePath);
        Assert.Equal(@"D:\Games\Tunic", entry.InstallPath);
    }

    /// <summary>
    /// A second browse replaces its own guess, but never a title the user
    /// typed over it.
    /// </summary>
    [Fact]
    public async Task A_second_browse_replaces_its_own_guess_but_not_the_users()
    {
        var picker = new FakePicker(@"D:\Games\Celeste\Celeste.exe");
        var inspector = new StubInspector("Celeste");
        var vm = Build(picker, inspector, new FakeIgdb());

        await vm.BeginAddFromFileCommand.ExecuteAsync(null);
        Assert.Equal("Celeste", vm.DraftTitle);

        picker.Path = @"D:\Games\Tunic\Tunic.exe";
        inspector.Title = "Tunic";
        await vm.BrowseForExecutableCommand.ExecuteAsync(null);
        Assert.Equal("Tunic", vm.DraftTitle);

        vm.DraftTitle = "My own title";
        picker.Path = @"D:\Games\Braid\Braid.exe";
        inspector.Title = "Braid";
        await vm.BrowseForExecutableCommand.ExecuteAsync(null);

        Assert.Equal("My own title", vm.DraftTitle);
        Assert.Equal(@"D:\Games\Braid\Braid.exe", vm.DraftExecutable);
    }

    // ══ Harness ════════════════════════════════════════════════════════════

    private LibrarySettingsViewModel Build(
        FakePicker picker, StubInspector? inspector, FakeIgdb igdb)
        => new(manual: _manual, executables: picker, inspector: inspector, igdb: igdb);

    private static StubInspector Inspector(string? title) => new(title);

    private static IgdbCandidate Candidate(long id, string name, int year)
        => new(id, name, null, year, []);

    private sealed class FakePicker : IExecutableFilePicker
    {
        public FakePicker(string? path) => Path = path;

        public string? Path { get; set; }

        public Task<string?> PickAsync(string title, CancellationToken ct = default)
            => Task.FromResult(Path);
    }

    /// <summary>
    /// Stands in for the version info a real installer would have written. The
    /// derivation itself is the real one, so the path still does its half.
    /// </summary>
    private sealed class StubInspector : IExecutableInspector
    {
        public StubInspector(string? title) => Title = title;

        public string? Title { get; set; }

        public ExecutableFacts Inspect(string executablePath)
            => ExecutableFacts.Derive(executablePath, Title);
    }

    private sealed class FakeIgdb : IIgdbAssignmentService
    {
        public IReadOnlyList<IgdbCandidate> Results { get; set; } = [];

        public string? SearchedFor { get; private set; }

        public Task<IReadOnlyList<IgdbCandidate>> SearchAsync(
            string title, CancellationToken ct = default)
        {
            SearchedFor = title;
            return Task.FromResult(Results);
        }

        public Task<IgdbAssignmentOutcome> AssignAsync(
            long workId, long igdbId, CancellationToken ct = default)
            => Task.FromResult(IgdbAssignmentOutcome.Failed);

        public Task<bool> ClearAsync(long workId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<Winnow.Core.Domain.WorkIgdbPin?> GetPinAsync(
            long workId, CancellationToken ct = default)
            => Task.FromResult<Winnow.Core.Domain.WorkIgdbPin?>(null);

        public Task<IReadOnlySet<long>> GetLivePinnedWorkIdsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlySet<long>>(new HashSet<long>());
    }
}
