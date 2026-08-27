using Hoard.App.Services;
using Hoard.App.Themes;
using Hoard.Core.Repositories;
using Xunit;

namespace Hoard.Tests;

/// <summary>
/// The folder half of the theme engine: what happens on first run, what a bad
/// file does to a good one beside it, and how a theme out of a file reaches the
/// window and comes back after a restart.
///
/// <para>Every test here runs against a temp directory. Nothing touches
/// <c>%LOCALAPPDATA%\Hoard\themes</c>, which is a real folder on the machine
/// this runs on.</para>
/// </summary>
public class UserThemeStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "hoard-themes-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }
        catch (IOException)
        {
            // A watcher on a temp folder can hold a handle a moment longer than
            // the test does. Leaving a temp directory behind is not a failure.
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// An empty folder is the wrong first thing to show an author: it says the
    /// feature exists and nothing else. What goes in is the default theme
    /// exported through the same path a user theme is READ by — so the example
    /// cannot describe a format the build does not have.
    /// </summary>
    [Fact]
    public void First_run_leaves_a_working_theme_in_the_folder()
    {
        var store = new UserThemeStore(_dir);
        Assert.Empty(store.EnsureSeeded());

        var file = Assert.Single(Directory.GetFiles(_dir, "*.json"));
        Assert.Equal("example.json", Path.GetFileName(file));

        var text = File.ReadAllText(file);

        // It explains itself in place, which is the only documentation an author
        // is guaranteed to find.
        Assert.Contains("// SEEDS are the eight colours", text, StringComparison.Ordinal);
        Assert.Contains("NOTHING else, ever", text, StringComparison.Ordinal);

        // And it parses, comments and all.
        var (theme, diagnostics) = ThemeJson.Parse("example.json", text);
        Assert.NotNull(theme);
        Assert.DoesNotContain(diagnostics, d => d.IsError);
    }

    /// <summary>The folder is the user's. A file that reappeared after they
    /// deleted it would be the app arguing with them.</summary>
    [Fact]
    public void Seeding_does_not_touch_a_folder_that_already_has_a_file()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "mine.json"), Theme("mine", "Mine"));

        new UserThemeStore(_dir).EnsureSeeded();

        Assert.Single(Directory.GetFiles(_dir, "*.json"));
    }

    /// <summary>
    /// The point of the whole validation design: one broken file does not take
    /// the others down, and does not take the app down.
    /// </summary>
    [Fact]
    public void A_broken_file_is_reported_and_the_good_ones_still_load()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "good.json"), Theme("good", "Good"));
        File.WriteAllText(Path.Combine(_dir, "broken.json"), "{ this is not json");
        File.WriteAllText(Path.Combine(_dir, "future.json"), """
            { "schemaVersion": 9, "id": "future", "name": "Future", "reason": "r" }
            """);

        var (themes, diagnostics) = new UserThemeStore(_dir).Load();

        Assert.Equal("good", Assert.Single(themes).Id);
        Assert.Contains(diagnostics, d => d.File == "broken.json" && d.IsError);
        Assert.Contains(diagnostics, d => d.File == "future.json" && d.Field == "schemaVersion");
    }

    /// <summary>Two files claiming one id is not a merge to resolve: the setting
    /// stores the id, so only one of them could ever be picked and choosing
    /// silently would make the answer depend on directory order.</summary>
    [Fact]
    public void Two_files_with_one_id_is_an_error_naming_the_first()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "a.json"), Theme("twin", "First"));
        File.WriteAllText(Path.Combine(_dir, "z.json"), Theme("twin", "Second"));

        var (themes, diagnostics) = new UserThemeStore(_dir).Load();

        Assert.Equal("First", Assert.Single(themes).Name);
        var d = Assert.Single(diagnostics, x => x.File == "z.json");
        Assert.Contains("a.json", d.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Export_writes_a_file_that_loads_back()
    {
        var store = new UserThemeStore(_dir);
        var (file, problem) = store.Export(HoardThemes.Nightshift);

        Assert.Null(problem);
        Assert.Equal("nightshift.json", file);

        var (themes, diagnostics) = store.Load();
        Assert.DoesNotContain(diagnostics, d => d.IsError);

        var loaded = Assert.Single(themes);
        Assert.Equal("nightshift-copy", loaded.Id);
        Assert.Equal(HoardThemes.Nightshift.Line, loaded.Line);
        Assert.True(loaded.IsUserTheme);
    }

    /// <summary>Exporting twice does not overwrite the first one — an author's
    /// edits are not ours to discard.</summary>
    [Fact]
    public void Export_never_overwrites()
    {
        var store = new UserThemeStore(_dir);
        Assert.Equal("hoard.json", store.Export(HoardThemes.Hoard).FileName);
        Assert.Equal("hoard-2.json", store.Export(HoardThemes.Hoard).FileName);
    }

    // ══ Reaching the window, and coming back ════════════════════════════════

    [Fact]
    public async Task A_user_theme_is_selectable_and_survives_a_restart()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "mine.json"), Theme("mine", "Mine"));

        var settings = new FakeSettings();
        var service = new ThemeService(settings, new UserThemeStore(_dir));
        await service.LoadAsync();

        Assert.Equal(HoardThemes.Default.Id, service.Theme.Id);
        Assert.Equal(5, service.Catalogue.Count);

        var mine = Assert.Single(service.Catalogue, t => t.Id == "mine");
        service.SelectTheme(mine);
        await service.PendingSave;

        // A second service, as a restart is.
        var next = new ThemeService(settings, new UserThemeStore(_dir));
        await next.LoadAsync();
        Assert.Equal("mine", next.Theme.Id);
        Assert.True(next.Theme.IsUserTheme);
    }

    /// <summary>A settings row naming a theme file the user threw away must not
    /// stop the app, for the same reason a preference written by a later version
    /// does not: it reads as unset.</summary>
    [Fact]
    public async Task A_deleted_theme_file_falls_back_to_the_default()
    {
        var settings = new FakeSettings();
        await settings.SetAsync(ThemeService.ThemeSettingKey, "gone");

        var service = new ThemeService(settings, new UserThemeStore(_dir));
        await service.LoadAsync();

        Assert.Equal(HoardThemes.Default.Id, service.Theme.Id);
    }

    /// <summary>
    /// Hot reload's actual claim: the theme that is up is re-resolved BY ID, so
    /// the window ends up wearing what was just saved rather than the palette
    /// from before it.
    /// </summary>
    [Fact]
    public async Task Reloading_repaints_the_theme_that_is_up()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "mine.json");
        File.WriteAllText(path, Theme("mine", "Mine", volt: "#4DE8C2"));

        var service = new ThemeService(new FakeSettings(), new UserThemeStore(_dir));
        await service.LoadAsync();
        service.SelectTheme(Assert.Single(service.Catalogue, t => t.Id == "mine"));

        var catalogueChanged = 0;
        service.CatalogueChanged += (_, _) => catalogueChanged++;

        File.WriteAllText(path, Theme("mine", "Mine", volt: "#FF9900"));
        service.ReloadUserThemes();

        Assert.Equal(1, catalogueChanged);
        Assert.Equal("mine", service.Theme.Id);
        Assert.Equal("#FF9900", ThemeJson.Hex(service.Theme.Volt));
    }

    /// <summary>
    /// Hot reload end to end: the watcher fires, and it fires ONCE for one save.
    ///
    /// <para>The debounce is the part worth a test. An editor writing a file
    /// produces two to four events and half of them arrive while the file is
    /// still truncated, so a store that re-read on the first one would report
    /// "the file is empty" a moment before it reported the theme — turning the
    /// feature into a source of spurious diagnostics.</para>
    /// </summary>
    [Fact]
    public void Saving_a_theme_file_wakes_the_store_once()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "mine.json");
        File.WriteAllText(path, Theme("mine", "Mine"));

        using var store = new UserThemeStore(_dir);
        var woke = new ManualResetEventSlim();
        var count = 0;

        store.Changed += (_, _) =>
        {
            Interlocked.Increment(ref count);
            woke.Set();
        };

        store.Watch();
        File.WriteAllText(path, Theme("mine", "Mine", volt: "#FF9900"));

        Assert.True(woke.Wait(TimeSpan.FromSeconds(10)), "the watcher never fired");

        // And what it wakes to is the file as saved, not as it was.
        var (themes, _) = store.Load();
        Assert.Equal("#FF9900", ThemeJson.Hex(Assert.Single(themes).Volt));

        // One save, one wake. Give the debounce room to prove it did not fire
        // again for the trailing events the write produced.
        Thread.Sleep(600);
        Assert.Equal(1, Volatile.Read(ref count));
    }

    // ══ Per-theme defaults ══════════════════════════════════════════════════

    /// <summary>
    /// A theme built against a 40% acrylic field and then first seen solid is
    /// not that theme, so picking one applies what it asks for.
    /// </summary>
    [Fact]
    public async Task Picking_a_theme_applies_the_position_it_asks_for()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "open.json"), Theme(
            "open", "Open",
            defaults: """ "transparency": 40, "reach": "chrome-and-wall", "layout": "floating" """));

        var settings = new FakeSettings();
        var service = new ThemeService(settings, new UserThemeStore(_dir));
        await service.LoadAsync();

        Assert.Equal(0, service.Transparency);

        service.SelectTheme(Assert.Single(service.Catalogue, t => t.Id == "open"));
        await service.PendingSave;

        Assert.Equal(40, service.Transparency);
        Assert.True(service.WallTranslucent);
        Assert.Equal(HoardLayout.Floating, service.Layout);

        // Written, not merely applied — so the next launch reads back what the
        // user was left holding rather than re-deriving it.
        Assert.Equal("40", await settings.GetAsync(ThemeService.TransparencySettingKey));
    }

    /// <summary>And they LOSE to anything the user actually stored, which is
    /// what makes them an opening position rather than a setting the theme keeps
    /// taking back.</summary>
    [Fact]
    public async Task A_stored_value_beats_the_themes_own_default()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "open.json"), Theme(
            "open", "Open", defaults: """ "transparency": 40 """));

        var settings = new FakeSettings();
        await settings.SetAsync(ThemeService.ThemeSettingKey, "open");
        await settings.SetAsync(ThemeService.TransparencySettingKey, "12");

        var service = new ThemeService(settings, new UserThemeStore(_dir));
        await service.LoadAsync();

        Assert.Equal("open", service.Theme.Id);
        Assert.Equal(12, service.Transparency);
    }

    /// <summary>
    /// The regression that matters most: the four shipped themes ask for
    /// nothing, so nothing about them moved.
    /// </summary>
    [Fact]
    public async Task Selecting_a_builtin_touches_no_other_setting()
    {
        var settings = new FakeSettings();
        var service = new ThemeService(settings, new UserThemeStore(_dir));
        await service.LoadAsync();

        service.SetTransparency(33);
        service.SetWallTranslucent(true);
        await service.PendingSave;

        service.SelectTheme(HoardThemes.BoxArt);
        await service.PendingSave;

        Assert.Equal(33, service.Transparency);
        Assert.True(service.WallTranslucent);
        Assert.Equal(HoardBackdrops.Default, service.Backdrop);
        Assert.Equal(HoardLayouts.Default, service.Layout);
    }

    /// <summary>Every write a theme change makes has to be waitable, or a caller
    /// that awaits the last one is awaiting a task that says nothing about the
    /// other four.</summary>
    [Fact]
    public async Task Every_write_a_theme_change_makes_reaches_the_store()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "open.json"), Theme(
            "open", "Open",
            defaults: """ "transparency": 40, "backdrop": "mica", "reach": "chrome-and-wall", "layout": "floating" """));

        var settings = new FakeSettings();
        var service = new ThemeService(settings, new UserThemeStore(_dir));
        await service.LoadAsync();

        service.SelectTheme(Assert.Single(service.Catalogue, t => t.Id == "open"));
        await service.PendingSave;

        Assert.Equal("open", await settings.GetAsync(ThemeService.ThemeSettingKey));
        Assert.Equal("40", await settings.GetAsync(ThemeService.TransparencySettingKey));
        Assert.Equal("mica", await settings.GetAsync(ThemeService.BackdropSettingKey));
        Assert.Equal("true", await settings.GetAsync(ThemeService.WallSettingKey));
        Assert.Equal("floating", await settings.GetAsync(ThemeService.LayoutSettingKey));
    }

    private static string Theme(
        string id, string name, string volt = "#4DE8C2", string defaults = "")
        => $$"""
            {
              "schemaVersion": 1,
              "id": "{{id}}",
              "name": "{{name}}",
              "reason": "A theme written by a test.",
              "seeds": {
                "ground":  "#0F1C1E",
                "surface": "#16282A",
                "text":    "#F0EDE7",
                "flare":   "#FF4D93",
                "volt":    "{{volt}}",
                "amber":   "#FFB63D",
                "azure":   "#57A8F0",
                "danger":  "#E04B45"
              },
              "defaults": { {{defaults}} }
            }
            """;

    private sealed class FakeSettings : ISettingsRepository
    {
        private readonly Dictionary<string, string> _rows = new(StringComparer.Ordinal);

        public Task<string?> GetAsync(string key, CancellationToken ct = default)
            => Task.FromResult(_rows.TryGetValue(key, out var v) ? v : null);

        public Task SetAsync(string key, string value, CancellationToken ct = default)
        {
            _rows[key] = value;
            return Task.CompletedTask;
        }
    }
}
