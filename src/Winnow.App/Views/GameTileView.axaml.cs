using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Winnow.App.ViewModels;

namespace Winnow.App.Views;

/// <summary>
/// Code-behind for the cover tile. Two jobs, both forced by container recycling
/// in <see cref="CoverWall"/>.
/// <para>Hover: the view model's <see cref="GameTileViewModel.IsPointerOver"/>
/// drives <see cref="GameTileViewModel.DisplayAlpha"/>, which the vivid art
/// layer animates over 140ms (§5.1 — "the game wakes up under the cursor").
/// Avalonia's <c>:pointerover</c> pseudo-class can't reach a view-model
/// property, so the two pointer events do it explicitly.</para>
/// <para>Cover art: realization is the load trigger. The wall only gives a
/// container a data context while its cell is on screen (plus a buffer row), so
/// "visible first" falls out of the context swap for free — and the swap
/// retargets this container's <see cref="Cover"/> presenter, which drops the
/// previous game's art and lease. Detaching releases it. That is what keeps the
/// cache's memory bound honest with 616 tiles virtualized.</para>
/// </summary>
public partial class GameTileView : UserControl
{
    // A feed card owns the larger clickable caption and the preview lifetime.
    // Its cover still owns art leases and the two independent action buttons.
    public static readonly Avalonia.StyledProperty<bool> EmbeddedProperty =
        Avalonia.AvaloniaProperty.Register<GameTileView, bool>(nameof(Embedded));
    public bool Embedded { get => GetValue(EmbeddedProperty); set => SetValue(EmbeddedProperty, value); }

    public static readonly Avalonia.StyledProperty<Avalonia.Thickness> ActionInsetProperty =
        Avalonia.AvaloniaProperty.Register<GameTileView, Avalonia.Thickness>(nameof(ActionInset));
    public Avalonia.Thickness ActionInset { get => GetValue(ActionInsetProperty); set => SetValue(ActionInsetProperty, value); }

    public static readonly Avalonia.StyledProperty<bool> InteractionActiveProperty =
        Avalonia.AvaloniaProperty.Register<GameTileView, bool>(nameof(InteractionActive));
    public bool InteractionActive { get => GetValue(InteractionActiveProperty); set => SetValue(InteractionActiveProperty, value); }

    private GameTileViewModel? _pressedTile;
    private Avalonia.Point _pressPosition;
    /// <summary>Fallback tile width when the container is measured after attach (the wall's density minimum).</summary>
    private const double NominalTileWidth = 148;

    /// <summary>The view model this container is currently showing art for.</summary>
    private GameTileViewModel? _bound;

    private bool _pointerInside;
    private bool _keyboardActionFocus;
    private readonly GameHoverPreview _preview;
    private GamePreviewViewModel? _previewModel;

    public GameTileView()
    {
        InitializeComponent();
        _preview = new GameHoverPreview(this, Face);
        AddHandler(PointerPressedEvent, (_, _) => _preview.Suppress(), RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Escape) _preview.Suppress(); }, RoutingStrategies.Tunnel);
        AddHandler(GotFocusEvent, OnDescendantGotFocus, RoutingStrategies.Bubble);
        SizeChanged += (_, _) => RequestCover();

        // The previewer gets a populated tile; runtime leaves the DataContext
        // to the wall's container recycling. See Design/PreviewData.cs.
        if (Avalonia.Controls.Design.IsDesignMode)
        {
            DataContext = Design.PreviewData.Tile;
        }
    }

    /// <summary>
    /// This container's own cover state, retargeted when the wall recycles it
    /// onto another game. The same tile appears on the wall and on a feed card
    /// at once, so recycling this container may only drop what this container
    /// is showing.
    /// </summary>
    public CoverPresenter Cover { get; } = new();

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _pointerInside = true;
        if (!Embedded) _preview.Show();
        ApplyInteractionState();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _pressedTile = null;
        _pointerInside = false;
        _preview.Exit();
        ApplyInteractionState();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        // A recycled container deliberately forgets its old hover. Avalonia
        // may not raise PointerEntered when the pointer is still over the same
        // visual after its data context changes, so the first subsequent move
        // re-establishes the new tile's reveal state. Derive it from geometry:
        // a pressed button can retain capture and route moves here after exit.
        var pointerInside = new Avalonia.Rect(Bounds.Size).Contains(e.GetPosition(this));
        if (_pointerInside != pointerInside)
        {
            _pointerInside = pointerInside;
            if (pointerInside && !Embedded) _preview.Show(); else _preview.Exit();
            ApplyInteractionState();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _pressedTile = null;
        if (Embedded || e.Handled || e.KeyModifiers.HasFlag(KeyModifiers.Control)
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        for (var visual = e.Source as Avalonia.Visual; visual is not null && visual != this; visual = visual.GetVisualParent())
        {
            if (visual is Button or Avalonia.Controls.Primitives.RangeBase) return;
        }
        _pressedTile = _bound;
        _pressPosition = e.GetPosition(this);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var pressed = _pressedTile;
        _pressedTile = null;
        var position = e.GetPosition(this);
        if (e.InitialPressMouseButton != MouseButton.Left || pressed is null || pressed != _bound
            || !new Avalonia.Rect(Bounds.Size).Contains(position)
            || Math.Abs(position.X - _pressPosition.X) > 8 || Math.Abs(position.Y - _pressPosition.Y) > 8) return;
        OpenDetails();
        e.Handled = true;
    }

    public void OpenDetails()
    {
        _preview.Suppress();
        if (_bound is { } tile && tile.OpenDetailsCommand?.CanExecute(tile) == true)
            tile.OpenDetailsCommand.Execute(tile);
    }

    protected override void OnPropertyChanged(Avalonia.AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == EmbeddedProperty) Lift.Classes.Set("embedded", change.NewValue is true);

        if (change.Property == InteractionActiveProperty) ApplyInteractionState();

        if (change.Property == IsKeyboardFocusWithinProperty && change.NewValue is false)
        {
            _keyboardActionFocus = false;
            ApplyInteractionState();
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        var outgoing = _bound;
        base.OnDataContextChanged(e);

        // Recycling swaps the data context without ever detaching, so this — not
        // OnDetachedFromVisualTree — is the common path that retargets the
        // presenter. Missing it leaves this container's presenter holding a
        // lease on the previous game's art, and the cache's memory bound stops
        // meaning anything.
        if (!ReferenceEquals(_bound, DataContext))
        {
            if (outgoing is not null)
            {
                outgoing.IsPointerOver = false;
            }

            // A focused action must never become the same button acting on a
            // different game after recycling. Move focus to the stable root and
            // let the next Tab deliberately enter the new tile.
            if (IsKeyboardFocusWithin)
            {
                TopLevel.GetTopLevel(this)?.Focus(NavigationMethod.Unspecified);
            }

            _bound = DataContext as GameTileViewModel;
            _pressedTile = null;
            RetargetPreview();
            _pointerInside = false;
            _keyboardActionFocus = false;
            Cover.Target(_bound?.CoverKey, _bound?.Leases, _bound?.Ramp);
            ApplyInteractionState();
        }

        if (this.GetVisualRoot() is not null)
        {
            RequestCover();
        }
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ClearDetachedActionState(PrimaryActionHost);
        ClearDetachedActionState(DetailsActionHost);
        _bound = DataContext as GameTileViewModel;
        RetargetPreview();
        _pointerInside = IsPointerOver;
        Cover.Target(_bound?.CoverKey, _bound?.Leases, _bound?.Ramp);
        ApplyInteractionState();
        RequestCover();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _preview.Target(null);
        _previewModel?.Dispose();
        _previewModel = null;
        _pointerInside = false;
        _pressedTile = null;
        _keyboardActionFocus = false;
        ApplyInteractionState();
        HideDetachedAction(PrimaryActionHost);
        HideDetachedAction(DetailsActionHost);
        Cover.Release();
        base.OnDetachedFromVisualTree(e);
    }

    private void RetargetPreview()
    {
        _preview.Target(null);
        _previewModel?.Dispose();
        _previewModel = _bound is null || Embedded ? null : new GamePreviewViewModel(_bound);
        _preview.Target(_previewModel);
    }

    private void RequestCover()
    {
        if (DataContext is not GameTileViewModel)
        {
            return;
        }

        // Display resolution, not source resolution (§5.4). The cover cache
        // snaps this to a bucket, so DPI and the density slider cannot start a
        // re-decode treadmill.
        var width = Bounds.Width > 0 ? Bounds.Width : NominalTileWidth;
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        Cover.Request(width * scaling);
    }

    private void OnDescendantGotFocus(object? sender, GotFocusEventArgs e)
    {
        // Pointer focus is not a reason to pin hover chrome after the pointer
        // leaves. Tab and directional navigation are: without the reveal their
        // focused action and focus ring would both be invisible.
        _keyboardActionFocus = e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional;
        ApplyInteractionState();
    }

    private void ApplyInteractionState()
    {
        if (DataContext is GameTileViewModel tile)
        {
            tile.IsPointerOver = _pointerInside || InteractionActive;
        }

        Lift.Classes.Set("actions-visible", _pointerInside || _keyboardActionFocus || InteractionActive);
    }

    private static void HideDetachedAction(Border host)
    {
        // Once detached there is no style owner to finish an opacity transition
        // or apply the class change. Pin the inert state until reattachment.
        host.Opacity = 0;
        host.IsHitTestVisible = false;
    }

    private static void ClearDetachedActionState(Border host)
    {
        host.ClearValue(OpacityProperty);
        host.ClearValue(IsHitTestVisibleProperty);
    }

    // Play / Install moved off this class in M3b. It is a command on the view
    // model now (GameTileViewModel.PrimaryActionCommand), because a launch has
    // to tell the session watcher WHICH GAME it is, and a Click handler holding
    // a URI cannot: it knows a string, not an ownership. The URI still reaches
    // the OS's own handler and the app still never shells out to steam.exe by
    // name — that moved to Services/TopLevelUriDispatcher, which is now the one
    // place a URI leaves this application.
}
