using Avalonia;
using Avalonia.Controls;

namespace Winnow.App.Views.Fullscreen;

/// <summary>Sizes the bottom shelf from the measured hero before arranging a visible frame.</summary>
internal sealed class FullscreenHomeLayout(Action<Size> sizeShelf) : Grid
{
    protected override Size MeasureOverride(Size availableSize)
    {
        sizeShelf(availableSize);
        return base.MeasureOverride(availableSize);
    }
}
