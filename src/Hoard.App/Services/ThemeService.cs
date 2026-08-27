using Avalonia.Controls;
using Avalonia.Media;
using Hoard.App.Themes;
using Hoard.Core.Repositories;

namespace Hoard.App.Services;

/// <summary>
/// Owns which theme is up and whether the window admits the desktop, applies
/// both to the live resource dictionary, and remembers them.
///
/// <para><b>How a theme change reaches the window.</b> Every view in this app
/// names its tokens with <c>StaticResource</c>, which resolves once when the
/// XAML is parsed and never looks again — so replacing the dictionary would
/// repaint precisely nothing. What all of them share is the brush OBJECT they
/// resolved, so this writes <see cref="SolidColorBrush.Color"/> on the brushes
/// already in <c>Application.Resources</c>. <c>Brush</c> implements
/// <c>IAffectsRender</c>, and every render property registered with
/// <c>AffectsRender</c> subscribes to it, so the window invalidates down the
/// same path a colour animation takes. No binding is re-evaluated, no control is
/// rebuilt, and no view has to be rewritten to <c>DynamicResource</c> for a
/// feature that arrived after it.</para>
///
/// <para><b>Requested is not active.</b> Transparency is a preference; whether
/// the machine can honour it is a fact. Windows 10, a remote-desktop session and
/// a composition failure all end with <see cref="TopLevel.ActualTransparencyLevel"/>
/// reporting something other than Mica — and Avalonia's Win32 backend falls back
/// to <c>Transparent</c> rather than the <c>None</c> that was asked for, so the
/// test has to be positive. The window reports what it actually got through
/// <see cref="SetBackdropAvailable"/>, and the OPAQUE token set is applied
/// whenever the answer is no. A translucent rail over a window with nothing
/// behind it is the failure this exists to avoid: the preference is remembered,
/// so it comes back by itself on a machine that can do it.</para>
/// </summary>
public sealed class ThemeService
{
    /// <summary>§6's settings table is shared by every module, so the keys are
    /// namespaced (see <see cref="ISettingsRepository"/>).</summary>
    public const string ThemeSettingKey = "appearance.theme";

    public const string TransparencySettingKey = "appearance.transparency";

    private readonly ISettingsRepository? _settings;
    private HoardTheme _theme = HoardThemes.Default;
    private bool _transparencyRequested;
    private bool _backdropAvailable;
    private bool _loading;

    public ThemeService(ISettingsRepository? settings = null)
    {
        _settings = settings;
    }

    /// <summary>Raised after the resource dictionary has been rewritten, so the
    /// window can repaint its backdrop and force a redraw.</summary>
    public event EventHandler? Applied;

    public HoardTheme Theme => _theme;

    /// <summary>What the user asked for.</summary>
    public bool TransparencyRequested => _transparencyRequested;

    /// <summary>What the window actually got. False until a window says otherwise.</summary>
    public bool BackdropAvailable => _backdropAvailable;

    /// <summary>The state the tokens are painted for: both of the above.</summary>
    public bool TransparencyActive => _transparencyRequested && _backdropAvailable;

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
        if (_settings is null)
        {
            Apply();
            return;
        }

        var storedTheme = await _settings.GetAsync(ThemeSettingKey, ct);
        var storedTransparency = await _settings.GetAsync(TransparencySettingKey, ct);

        _loading = true;
        try
        {
            _theme = HoardThemes.ById(storedTheme);
            _transparencyRequested = bool.TryParse(storedTransparency, out var on) && on;
        }
        finally
        {
            _loading = false;
        }

        Apply();
    }

    /// <summary>
    /// Overrides both preferences for this process without writing either.
    /// Debug capture flags use it, which is why it is the only way to change the
    /// state without touching the database — an agent taking a screenshot must
    /// not leave a preference behind in the user's real library.
    /// </summary>
    public void OverrideForSession(HoardTheme theme, bool transparency)
    {
        _loading = true;
        try
        {
            _theme = theme;
            _transparencyRequested = transparency;
        }
        finally
        {
            _loading = false;
        }

        Apply();
    }

    public void SelectTheme(HoardTheme theme)
    {
        if (ReferenceEquals(_theme, theme))
        {
            return;
        }

        _theme = theme;
        Apply();
        Save(ThemeSettingKey, theme.Id);
    }

    public void SetTransparency(bool on)
    {
        if (_transparencyRequested == on)
        {
            return;
        }

        _transparencyRequested = on;
        Apply();
        Save(TransparencySettingKey, on ? "true" : "false");
    }

    /// <summary>
    /// The window's report of what the platform actually gave it. Repaints when
    /// the answer changes, because it can change while the window is open — the
    /// OS theme variant flipping, or a remote session taking composition away.
    /// </summary>
    public void SetBackdropAvailable(bool available)
    {
        if (_backdropAvailable == available)
        {
            return;
        }

        _backdropAvailable = available;
        Apply();
    }

    private void Save(string key, string value)
    {
        if (_loading || _settings is null)
        {
            return;
        }

        PendingSave = _settings.SetAsync(key, value);
    }

    private void Apply()
    {
        var app = Avalonia.Application.Current;
        if (app is not null)
        {
            ApplyTo(app.Resources, _theme, TransparencyActive);
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
    public static void ApplyTo(IResourceDictionary resources, HoardTheme theme, bool translucent)
    {
        foreach (var (key, colour) in theme.Tokens(translucent))
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

        var tokens = theme.Tokens(translucent);

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
