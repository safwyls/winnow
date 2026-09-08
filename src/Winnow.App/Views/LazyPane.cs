using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Winnow.App.Views;

/// <summary>
/// A screen that is not built until the first time it is shown, and is kept
/// afterwards.
///
/// <para>Every pane in <c>MainWindow.axaml</c> used to be instantiated at
/// startup and hidden with <c>IsVisible</c>, which charged a launch for
/// screens the user may never open: the heap dump in
/// <c>docs/spikes/memory-footprint.md</c> §2.3 found about 5,600 live
/// controls, 40k <c>StyleInstance</c>s and 27k <c>DynamicResourceExpression</c>s
/// at rest, roughly 20 MB of a 59 MB managed heap, plus a composition visual
/// for each one. A pane declared here costs one <see cref="Decorator"/> until
/// it is shown.</para>
///
/// <para>The pane's content is an ordinary <c>DataTemplate</c>: it inherits the
/// container's DataContext and its visibility, so the bindings inside it are
/// written exactly as they were when the view sat in the window's own tree, and
/// a pane that watches its own <c>IsVisible</c> still sees it turn off and on
/// again — never at birth, for the reason at <c>Materialize</c>.
/// What it does not inherit is the window's name scope — a template is its own
/// — so code-behind reaches a built pane through <see cref="Pane"/> rather than
/// through a generated field, and wires its events in
/// <see cref="Materialized"/>.</para>
/// </summary>
/// <remarks>
/// Two things decide when the pane is built, and both are needed.
/// <see cref="IsVisible"/> turning true builds it there and then, so a caller
/// that shows a pane and focuses it in the same turn finds something to focus.
/// The first measure taken while it is visible builds it too, which covers the
/// pane whose visibility binding resolves to true without ever changing — no
/// change event, so nothing else would. There is no third signal available:
/// Avalonia's <c>IsEffectivelyVisible</c>, which is what a nested pane inside a
/// hidden parent would want to watch, is a plain CLR property with no
/// notification of its own (checked against Avalonia 11.3.20), which is why
/// every pane here carries the whole of its own visibility condition.
/// </remarks>
public class LazyPane : Decorator
{
    /// <summary>Defines the <see cref="PaneTemplate"/> property.</summary>
    public static readonly StyledProperty<IDataTemplate?> PaneTemplateProperty =
        AvaloniaProperty.Register<LazyPane, IDataTemplate?>(nameof(PaneTemplate));

    static LazyPane()
    {
        // Hidden until told otherwise, which is the opposite of a Control's own
        // default. A pane's visibility is a binding, and a binding that has not
        // resolved yet — no DataContext at construction, which is the normal
        // order in App.OnFrameworkInitializationCompleted — reads as
        // UnsetValue, so the property falls back to its default. Defaulting to
        // true would build every pane on the first layout pass and undo the
        // whole point of this control.
        IsVisibleProperty.OverrideDefaultValue<LazyPane>(false);
    }

    /// <summary>What to build the first time this pane is shown.</summary>
    public IDataTemplate? PaneTemplate
    {
        get => GetValue(PaneTemplateProperty);
        set => SetValue(PaneTemplateProperty, value);
    }

    /// <summary>The built pane, or null while it has never been shown.</summary>
    public Control? Pane { get; private set; }

    /// <summary>Raised once, immediately after <see cref="Pane"/> is built.</summary>
    public event EventHandler? Materialized;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (IsVisible)
        {
            Materialize();
        }

        return base.MeasureOverride(availableSize);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsVisibleProperty && change.GetNewValue<bool>())
        {
            Materialize();
        }
    }

    private void Materialize()
    {
        if (Pane is not null || PaneTemplate is null)
        {
            return;
        }

        if (PaneTemplate.Build(DataContext) is not { } pane)
        {
            return;
        }

        Pane = pane;
        Child = pane;

        // The pane's own IsVisible follows this container's, because two panes
        // read that property rather than a command: the lightbox arms its focus
        // trap when it becomes visible and announces its close when it stops
        // being, and the modal refuses to hand focus back while it is itself on
        // the way out. A pane is BORN at the moment it is first shown, so it is
        // born visible and sees no first change — anything a pane must do on
        // its first appearance belongs in its attach, and the lightbox does
        // exactly that. Feeding it a false first would be worse: the lightbox
        // would announce a close it never had, and the modal would hand focus
        // back to the thumbnail the user had just opened.
        pane.Bind(IsVisibleProperty, this.GetObservable(IsVisibleProperty));

        Materialized?.Invoke(this, EventArgs.Empty);
    }
}
