using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media;
using Hoard.App.Themes;
using Hoard.Core.Repositories;

namespace Hoard.App.Services;

/// <summary>
/// Owns which theme is up and how much desktop the window admits, applies both
/// to the live resource dictionary, and remembers them.
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
/// <para><b>Transparency is a quantity, not a switch.</b> Mica itself is a
/// binary window hint, but nothing the user can see is: the perceived
/// translucency is entirely the alpha on our own surfaces over that backdrop, so
/// it is continuous and ours to set. It is stored as a whole percent, 0 meaning
/// fully opaque — which stays a real position, is the default, and is the answer
/// for anyone who wants the accessibility floor with no argument.</para>
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

    /// <summary>
    /// The same key the boolean toggle used, now holding a whole percent.
    /// Migrated in place rather than orphaned — see <see cref="ParseTransparency"/>.
    /// </summary>
    public const string TransparencySettingKey = "appearance.transparency";

    /// <summary>
    /// What a stored <c>true</c> becomes.
    ///
    /// <para><b>Not the 14% the boolean actually painted.</b> That setting's whole
    /// problem was that it was imperceptible, so converting someone to the number
    /// that produced it would convert them to the complaint.</para>
    ///
    /// <para><b>And not the far end either.</b> A migration may not carry anyone
    /// across a floor they did not choose to cross: 25 is inside every theme's AA
    /// ceiling — the lowest of the four is 26 — so the window they get
    /// back is unmistakably translucent AND still clears §8 against the brightest
    /// backdrop a wallpaper can produce. The slider is right there for anyone who
    /// wants more, with the number in front of them.</para>
    /// </summary>
    public const int MigratedTransparency = 25;

    private readonly ISettingsRepository? _settings;
    private HoardTheme _theme = HoardThemes.Default;
    private int _transparency;
    private bool _backdropAvailable;
    private bool _loading;
    private bool _sessionOverride;

    public ThemeService(ISettingsRepository? settings = null)
    {
        _settings = settings;
    }

    /// <summary>Raised after the resource dictionary has been rewritten, so the
    /// window can repaint its backdrop and force a redraw.</summary>
    public event EventHandler? Applied;

    public HoardTheme Theme => _theme;

    /// <summary>What the user asked for, as a whole percent. 0 is fully opaque.</summary>
    public int Transparency => _transparency;

    /// <summary>Whether any desktop was asked for at all — the thing the window's
    /// backdrop hint turns on.</summary>
    public bool TransparencyRequested => _transparency > 0;

    /// <summary>What the window actually got. False until a window says otherwise.</summary>
    public bool BackdropAvailable => _backdropAvailable;

    /// <summary>The amount the tokens are painted for: the request, or zero when
    /// the machine cannot composite.</summary>
    public double ActiveTransparency
        => TransparencyRequested && _backdropAvailable ? _transparency / 100.0 : 0;

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
            _transparency = ParseTransparency(storedTransparency);
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
    /// The stored value, in either shape it can have.
    ///
    /// <para>A whole percent is what this setting holds now. A <c>true</c> or
    /// <c>false</c> is what the toggle that preceded it wrote, and both are
    /// answered rather than discarded — a preference someone set does not get to
    /// silently evaporate because the control that set it was replaced. Anything
    /// else reads as unset, which is opaque.</para>
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
    /// Overrides both preferences for this process and SEALS THE SERVICE AGAINST
    /// WRITING for the rest of it. Debug capture flags use it: an agent taking a
    /// screenshot must not leave a preference behind in the user's real library.
    ///
    /// <para><b>The seal is the part that was missing, and it was missing in the
    /// way that costs you a real user's settings row.</b> Suppressing the write
    /// only for the duration of the override was enough as long as nothing
    /// afterwards changed the state — and then a capture run drove the Appearance
    /// screen, something in the posted input reached the slider, and the
    /// preference the run had promised not to touch was rewritten in the live
    /// database. A promise that holds until the first click is not a promise. A
    /// session that was told what to look like is not one whose looks are worth
    /// saving, so nothing it does gets written.</para>
    /// </summary>
    public void OverrideForSession(HoardTheme theme, int transparency)
    {
        _loading = true;
        try
        {
            _theme = theme;
            _transparency = Math.Clamp(transparency, 0, 100);
        }
        finally
        {
            _loading = false;
            _sessionOverride = true;
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

    private static string Format(int percent)
        => percent.ToString(CultureInfo.InvariantCulture);

    private void Save(string key, string value)
    {
        if (_loading || _sessionOverride || _settings is null)
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
            ApplyTo(app.Resources, _theme, ActiveTransparency);
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
    public static void ApplyTo(IResourceDictionary resources, HoardTheme theme, double transparency)
    {
        var tokens = theme.Tokens(transparency);

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
