using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media;
using Winnow.App.Themes;
using Winnow.Core.Repositories;

namespace Winnow.App.Services;

/// <summary>
/// Owns the four appearance decisions (theme, transparency, backdrop material,
/// wall translucency), applies them to the live resource dictionary by mutating
/// brush colours in place, and persists them in the settings table.
/// </summary>
public sealed class ThemeService
{
    /// <summary>§6's settings table is shared by every module, so the keys are
    /// namespaced (see <see cref="ISettingsRepository"/>).</summary>
    public const string ThemeSettingKey = "appearance.theme";

    /// <summary>
    /// The same key the boolean toggle used, now holding a whole percent.
    /// Migrated in place rather than orphaned — see <see cref="ParseTransparency"/>.
    /// </summary>
    public const string TransparencySettingKey = "appearance.transparency";

    /// <summary>Which material the user asked Windows for: <c>acrylic</c> or
    /// <c>mica</c>. Unset reads as acrylic (<see cref="WinnowBackdrops.Default"/>).</summary>
    public const string BackdropSettingKey = "appearance.backdrop";

    /// <summary>Whether the cover wall's field opens up along with the chrome.
    /// Unset reads as false — the previous default, and a real preference.</summary>
    public const string WallSettingKey = "appearance.wall";

    /// <summary>How the window is put together: <c>flush</c> or
    /// <c>floating</c>. Unset reads as flush (<see cref="WinnowLayouts.Default"/>),
    /// which is what the app has always looked like and what every contrast
    /// figure in §14 was measured against.</summary>
    public const string LayoutSettingKey = "appearance.layout";

    /// <summary>What a stored <c>true</c> becomes when migrating from the old boolean toggle.</summary>
    public const int MigratedTransparency = 25;

    private readonly ISettingsRepository? _settings;
    private readonly UserThemeStore? _userThemes;
    private IReadOnlyList<WinnowTheme> _catalogue = WinnowThemes.All;
    private IReadOnlyList<ThemeDiagnostic> _diagnostics = [];
    private WinnowTheme _theme = WinnowThemes.Default;
    private int _transparency;
    private WinnowBackdrop _backdrop = WinnowBackdrops.Default;
    private WinnowBackdrop _activeBackdrop = WinnowBackdrop.None;
    private bool _wallTranslucent;
    private WinnowLayout _layout = WinnowLayouts.Default;
    private bool _loading;
    private bool _sessionOverride;

    public ThemeService(ISettingsRepository? settings = null, UserThemeStore? userThemes = null)
    {
        _settings = settings;
        _userThemes = userThemes;
    }

    /// <summary>Raised after the resource dictionary has been rewritten, so the
    /// window can repaint its backdrop and force a redraw.</summary>
    public event EventHandler? Applied;

    /// <summary>
    /// Raised when the set of available themes changed (folder re-read, file
    /// added or removed), as distinct from which theme is currently active.
    /// </summary>
    public event EventHandler? CatalogueChanged;

    public WinnowTheme Theme => _theme;

    /// <summary>
    /// Every theme that can be picked: the four built-ins first, then user themes
    /// from the themes folder in file-name order.
    /// </summary>
    public IReadOnlyList<WinnowTheme> Catalogue => _catalogue;

    /// <summary>What was wrong with the folder and the files in it, as of the
    /// last read. Empty is the normal case and the screen says nothing when it
    /// is.</summary>
    public IReadOnlyList<ThemeDiagnostic> Diagnostics => _diagnostics;

    /// <summary>Where the user's themes live, or <c>null</c> when the host did
    /// not register a store — which is what a view-model test gets.</summary>
    public string? UserThemeDirectory => _userThemes?.Directory;

    /// <summary>What the user asked for, as a whole percent. 0 is fully opaque.</summary>
    public int Transparency => _transparency;

    /// <summary>Whether any desktop was asked for at all — the thing the window's
    /// backdrop hint turns on.</summary>
    public bool TransparencyRequested => _transparency > 0;

    /// <summary>Which material the user picked. What they GOT is
    /// <see cref="ActiveBackdrop"/>, and the two can differ.</summary>
    public WinnowBackdrop Backdrop => _backdrop;

    /// <summary>What the platform actually composed, as reported by the window.
    /// <see cref="WinnowBackdrop.None"/> until a window says otherwise.</summary>
    public WinnowBackdrop ActiveBackdrop => _activeBackdrop;

    /// <summary>Whether the desktop is reaching the window at all.</summary>
    public bool BackdropAvailable
        => _activeBackdrop is WinnowBackdrop.Acrylic or WinnowBackdrop.Mica;

    /// <summary>True once the machine composited something OTHER than what was
    /// asked for — the case the Appearance screen has to name rather than
    /// swallow.</summary>
    public bool BackdropSubstituted
        => BackdropAvailable && _activeBackdrop != _backdrop;

    /// <summary>Whether the user asked for the cover wall's field to open up
    /// along with the chrome. The tiles never do, at any setting.</summary>
    public bool WallTranslucent => _wallTranslucent;

    /// <summary>
    /// Whether the content panes float as rounded cards on the window's ground,
    /// or meet edge to edge.
    /// </summary>
    public WinnowLayout Layout => _layout;

    /// <summary>Convenience for the shell, which needs the answer as a bool to
    /// drive one style class.</summary>
    public bool IsFloating => _layout == WinnowLayout.Floating;

    /// <summary>The amount the tokens are painted for: the request, or zero when
    /// the machine cannot composite.</summary>
    public double ActiveTransparency
        => TransparencyRequested && BackdropAvailable ? _transparency / 100.0 : 0;

    /// <summary>Whether the wall's field is actually painted translucent right
    /// now: asked for, and the desktop is reaching the window to be seen.</summary>
    public bool ActiveWallTranslucency
        => _wallTranslucent && ActiveTransparency > 0;

    /// <summary>The in-flight write, so a caller — or a test — can wait for the
    /// preference to reach disk. Nothing in the UI waits on it.</summary>
    public Task PendingSave { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Reads both stored preferences and paints them. Anything unparseable
    /// leaves the default standing: the store returns exactly what was written
    /// and takes no position on bad text, so this one does.
    /// </summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        // The folder is read BEFORE the stored id is resolved, or a user theme
        // could never be the one that comes back on launch: the id would resolve
        // against the built-ins alone, fall through to the default, and the
        // user's preference would look like it had not been saved.
        ReadUserThemes();

        if (_settings is null)
        {
            Apply();
            return;
        }

        var storedTheme = await _settings.GetAsync(ThemeSettingKey, ct);
        var storedTransparency = await _settings.GetAsync(TransparencySettingKey, ct);
        var storedBackdrop = await _settings.GetAsync(BackdropSettingKey, ct);
        var storedWall = await _settings.GetAsync(WallSettingKey, ct);
        var storedLayout = await _settings.GetAsync(LayoutSettingKey, ct);

        _loading = true;
        try
        {
            _theme = ById(storedTheme);

            // ── A theme's own defaults, and where they lose ─────────────────
            // A STORED value always wins, at every one of these four. That is
            // what makes a theme's defaults an opening position rather than a
            // setting it keeps taking back: it gets to say what it wants the
            // first time it is picked (see SelectTheme), and after that the
            // user's own answer is the one on disk and the one that comes back.
            // The four built-ins declare nothing here, so every one of these
            // falls through to exactly the expression it had before.
            var wants = _theme.Defaults;

            _transparency = storedTransparency is not null
                ? ParseTransparency(storedTransparency)
                : wants?.Transparency ?? 0;

            _backdrop = storedBackdrop is not null
                ? WinnowBackdrops.ById(storedBackdrop)
                : wants?.Backdrop ?? WinnowBackdrops.Default;

            // Unparseable reads as false, which is the previous behaviour and
            // the conservative one: a wall that opens up unasked is a surprise,
            // a wall that stays solid is what the app has always looked like.
            _wallTranslucent = storedWall is not null
                ? bool.TryParse(storedWall, out var wall) && wall
                : wants?.WallTranslucent ?? false;

            // Same rule as every other appearance key: anything unparseable
            // leaves the default standing rather than throwing, because the
            // store returns exactly what was written and takes no position on
            // bad text.
            _layout = storedLayout is not null
                ? WinnowLayouts.ById(storedLayout)
                : wants?.Layout ?? WinnowLayouts.Default;
        }
        finally
        {
            _loading = false;
        }

        Apply();

        // Rewrite the migrated value so the boolean is converted once rather
        // than re-derived on every launch. Only when it actually changed shape.
        if (storedTransparency is not null
            && !int.TryParse(storedTransparency, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            Save(TransparencySettingKey, Format(_transparency));
        }
    }

    /// <summary>
    /// Parses the stored transparency value: a whole percent, or the legacy
    /// boolean (<c>true</c> becomes <see cref="MigratedTransparency"/>).
    /// </summary>
    public static int ParseTransparency(string? stored)
    {
        if (int.TryParse(stored, NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent))
        {
            return Math.Clamp(percent, 0, 100);
        }

        return bool.TryParse(stored, out var on) && on ? MigratedTransparency : 0;
    }

    /// <summary>
    /// Overrides appearance for this process and seals the service against
    /// writing for the rest of it (used by debug capture flags).
    /// </summary>
    public void OverrideForSession(
        WinnowTheme theme,
        int transparency,
        WinnowBackdrop? backdrop = null,
        bool? wallTranslucent = null,
        WinnowLayout? layout = null)
    {
        _loading = true;
        try
        {
            _theme = theme;
            _transparency = Math.Clamp(transparency, 0, 100);
            _backdrop = backdrop ?? _backdrop;
            _wallTranslucent = wallTranslucent ?? _wallTranslucent;
            _layout = layout ?? _layout;
        }
        finally
        {
            _loading = false;
            _sessionOverride = true;
        }

        Apply();
    }

    public void SelectTheme(WinnowTheme theme)
    {
        if (ReferenceEquals(_theme, theme))
        {
            return;
        }

        _theme = theme;

        // ── The theme's opening position, applied once, here ────────────────
        // Deliberately at SELECTION rather than at load: a theme built against a
        // 40% acrylic field and then first seen solid is not that theme, and the
        // moment a person picks it is the only moment they are asking to be
        // shown what it looks like. From here on the slider beside it is theirs
        // — every one of these writes a settings row, so the next launch reads
        // back what they left rather than what the theme asked for.
        //
        // Null on all four built-ins, so this is a no-op for them and the
        // shipped set behaves exactly as it always did.
        ApplyOpeningPosition(theme.Defaults);

        Apply();
        Save(ThemeSettingKey, theme.Id);
    }

    /// <summary>
    /// The theme stored under <paramref name="id"/>, or the default — resolved
    /// against the user's folder as well as the built-ins.
    ///
    /// <para>Same rule as <c>WinnowThemes.ById</c> and for one more reason: an id
    /// this build cannot find is now also what a deleted theme file looks like,
    /// and a settings row naming a file the user threw away must not stop the
    /// app any more than a preference written by a later version does.</para>
    /// </summary>
    public WinnowTheme ById(string? id)
        => _catalogue.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal))
            // The pre-rename id for the house theme. Resolved AFTER the
            // catalogue so a user theme that took the name keeps it, and only
            // then, so a settings row written by a Hoard build comes back as the
            // theme it was actually choosing. See WinnowThemes.LegacyDefaultId.
            ?? (string.Equals(id, WinnowThemes.LegacyDefaultId, StringComparison.Ordinal)
                ? WinnowThemes.Winnow
                : WinnowThemes.Default);

    /// <summary>
    /// Re-reads the themes folder and repaints if the active theme came from it.
    /// The active theme is re-resolved by id so hot reload picks up the new palette.
    /// </summary>
    public void ReloadUserThemes()
    {
        var wasUp = _theme.Id;
        ReadUserThemes();

        var resolved = ById(wasUp);
        var changed = !ReferenceEquals(resolved, _theme);
        _theme = resolved;

        CatalogueChanged?.Invoke(this, EventArgs.Empty);

        if (changed)
        {
            Apply();
        }
    }

    /// <summary>
    /// Writes a theme into the user's folder as a starting template, and picks
    /// the folder up again so it appears in the picker immediately.
    ///
    /// <para>Here rather than on the view model reaching into the store, because
    /// §5.1's boundary is that the UI raises commands and reads state: a view
    /// model that held a <see cref="UserThemeStore"/> would be a view model that
    /// does file IO, and the next thing it would do is read one.</para>
    /// </summary>
    /// <returns>The file name written, or a sentence saying why not.</returns>
    public (string? FileName, string? Problem) ExportTheme(WinnowTheme theme)
    {
        if (_userThemes is null)
        {
            return (null, "There is no themes folder on this machine.");
        }

        var (file, problem) = _userThemes.Export(theme);
        if (file is not null)
        {
            ReloadUserThemes();
        }

        return (file, problem?.Message);
    }

    /// <summary>Starts hot reload. Separate from the constructor so a test, and
    /// the capture harness, can use the store without a file watcher running.</summary>
    public void WatchUserThemes()
    {
        if (_userThemes is null)
        {
            return;
        }

        _userThemes.Changed += (_, _) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(ReloadUserThemes);
        _userThemes.Watch();
    }

    private void ReadUserThemes()
    {
        if (_userThemes is null)
        {
            return;
        }

        var seeding = _userThemes.EnsureSeeded();
        var (themes, diagnostics) = _userThemes.Load();

        _catalogue = themes.Count == 0 ? WinnowThemes.All : [.. WinnowThemes.All, .. themes];
        _diagnostics = [.. seeding, .. diagnostics];
    }

    /// <summary>
    /// Sets the three qualifiers and the layout to what a theme asked for,
    /// writing each one it actually moves.
    ///
    /// <para>Written straight onto the fields rather than through the four
    /// setters, so the window repaints once at the end of
    /// <see cref="SelectTheme"/> instead of four times on the way there.</para>
    /// </summary>
    private void ApplyOpeningPosition(ThemeAppearanceDefaults? wants)
    {
        if (wants is null || wants.IsEmpty)
        {
            return;
        }

        if (wants.Transparency is { } percent && percent != _transparency)
        {
            _transparency = Math.Clamp(percent, 0, 100);
            Save(TransparencySettingKey, Format(_transparency));
        }

        if (wants.Backdrop is { } backdrop && backdrop != _backdrop && backdrop is not WinnowBackdrop.None)
        {
            _backdrop = backdrop;

            // What the platform will give us for the NEW material is not known
            // until the window has asked — same reasoning as SelectBackdrop.
            _activeBackdrop = WinnowBackdrop.None;
            Save(BackdropSettingKey, WinnowBackdrops.Id(backdrop));
        }

        if (wants.WallTranslucent is { } wall && wall != _wallTranslucent)
        {
            _wallTranslucent = wall;
            Save(WallSettingKey, wall ? "true" : "false");
        }

        if (wants.Layout is { } layout && layout != _layout)
        {
            _layout = layout;
            Save(LayoutSettingKey, WinnowLayouts.Id(layout));
        }
    }

    /// <summary>
    /// Sets how much desktop the chrome admits, as a whole percent.
    ///
    /// <para>Rounded on the way in rather than stored as a double, because the
    /// slider is dragged and a preference row rewritten on every pixel of travel
    /// is a write per frame. One value per percent is finer than anyone can
    /// resolve and coarse enough to be a setting.</para>
    /// </summary>
    public void SetTransparency(int percent)
    {
        var clamped = Math.Clamp(percent, 0, 100);
        if (_transparency == clamped)
        {
            return;
        }

        _transparency = clamped;
        Apply();
        Save(TransparencySettingKey, Format(clamped));
    }

    /// <summary>
    /// Which material the user wants Windows to compose. Changing it re-requests
    /// the backdrop, which is the window's job — hence <see cref="Applied"/>
    /// rather than a call from here.
    /// </summary>
    public void SelectBackdrop(WinnowBackdrop backdrop)
    {
        if (_backdrop == backdrop || backdrop is WinnowBackdrop.None)
        {
            return;
        }

        _backdrop = backdrop;

        // The answer for the NEW material is not known until the window has
        // asked. Clearing it first means the screen never reports the old
        // material's result as the new one's — a wrong "available" is exactly
        // the silent substitution this is here to prevent.
        _activeBackdrop = WinnowBackdrop.None;

        Apply();
        Save(BackdropSettingKey, WinnowBackdrops.Id(backdrop));
    }

    /// <summary>
    /// Whether the cover wall's field admits the desktop along with the chrome.
    /// The tiles are unaffected at every setting — see <c>TileGround</c>.
    /// </summary>
    public void SetWallTranslucent(bool translucent)
    {
        if (_wallTranslucent == translucent)
        {
            return;
        }

        _wallTranslucent = translucent;
        Apply();
        Save(WallSettingKey, translucent ? "true" : "false");
    }

    /// <summary>
    /// Whether the content panes float. Repaints, because the layout moves four
    /// tokens — the shell's ground, the caption, the command bar and the search
    /// field's fill — and the shell reads the flag to place its margins.
    /// </summary>
    public void SetLayout(WinnowLayout layout)
    {
        if (_layout == layout)
        {
            return;
        }

        _layout = layout;
        Apply();
        Save(LayoutSettingKey, WinnowLayouts.Id(layout));
    }

    /// <summary>
    /// The window's report of what the platform actually composed. Repaints when
    /// the answer changes, because it can change while the window is open — the
    /// OS theme variant flipping, or a remote session taking composition away.
    ///
    /// <para>A VALUE and not a bool, because "we got nothing" and "we got the
    /// other one" need different words on screen and a bool cannot tell them
    /// apart.</para>
    /// </summary>
    public void SetActiveBackdrop(WinnowBackdrop active)
    {
        if (_activeBackdrop == active)
        {
            return;
        }

        _activeBackdrop = active;
        Apply();
    }

    private static string Format(int percent)
        => percent.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Writes one preference, chained behind whatever write is already in flight.
    /// </summary>
    private void Save(string key, string value)
    {
        if (_loading || _sessionOverride || _settings is null)
        {
            return;
        }

        PendingSave = Chain(PendingSave, _settings, key, value);

        static async Task Chain(Task previous, ISettingsRepository settings, string key, string value)
        {
            try
            {
                await previous;
            }
            catch (Exception)
            {
                // The earlier write's failure is that write's business. Losing
                // one preference row must not stop the next one being tried.
            }

            await settings.SetAsync(key, value);
        }
    }

    private void Apply()
    {
        var app = Avalonia.Application.Current;
        if (app is not null)
        {
            ApplyTo(app.Resources, _theme, ActiveTransparency, ActiveWallTranslucency, _layout);
        }

        Applied?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Writes a palette into a live resource dictionary. Static and
    /// dictionary-taking so a test can run it without an Application.
    ///
    /// <para>A key the dictionary does not hold is skipped rather than added.
    /// Adding it would be worse than useless: nothing resolved it at parse time,
    /// so nothing would read it, and the missing token would look handled.
    /// <c>tokens.axaml</c> is the list of what exists.</para>
    /// </summary>
    public static void ApplyTo(
        IResourceDictionary resources,
        WinnowTheme theme,
        double transparency,
        bool wallTranslucent = false,
        WinnowLayout layout = WinnowLayouts.Default)
    {
        var tokens = theme.Tokens(transparency, wallTranslucent, layout);

        foreach (var (key, colour) in tokens)
        {
            if (resources.TryGetResource(key, null, out var existing)
                && existing is SolidColorBrush brush)
            {
                brush.Color = colour;
            }
        }

        // The tile hover scrim is a gradient, so it is two stops rather than one
        // brush colour (§5.3).
        if (resources.TryGetResource("TileScrim", null, out var scrim)
            && scrim is LinearGradientBrush gradient
            && gradient.GradientStops.Count >= 2)
        {
            var (top, bottom) = theme.TileScrim();
            gradient.GradientStops[0].Color = top;
            for (var i = 1; i < gradient.GradientStops.Count; i++)
            {
                gradient.GradientStops[i].Color = bottom;
            }
        }

        // Fluent's scrollbar template reads this one as a Color rather than a
        // brush, and reads it with DynamicResource — so it is the one token that
        // is replaced in the dictionary instead of mutated in place.
        if (resources.ContainsKey("ScrollBarThumbBackgroundColor"))
        {
            resources["ScrollBarThumbBackgroundColor"] = tokens["ScrollBarPanningThumbBackground"];
        }

        // §5.2's optional soft glow behind the unread dot, and the same glow on
        // the rail's pip and the gap rail's marks. These are ADDED rather than
        // mutated: BoxShadows is a struct parsed from a string, so there is no
        // object in the dictionary to write a colour onto and no way to declare
        // one in the token file. The views read them with DynamicResource for
        // that reason, and this is the only place they exist.
        //
        // The glow is the one decorative use of Flare, and it is legal for the
        // same reason the dot is: it IS the dot.
        resources["BadgeGlow"] = Glow(tokens["FlareGlow"], 10);
        resources["PipGlow"] = Glow(tokens["FlareSoft"], 8);
    }

    private static BoxShadows Glow(Color colour, double blur)
        => new(new BoxShadow { OffsetX = 0, OffsetY = 0, Blur = blur, Spread = 0, Color = colour });
}
