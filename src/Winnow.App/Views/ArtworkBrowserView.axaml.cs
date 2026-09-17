using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia;
using Avalonia.Threading;
using System.ComponentModel;
using Winnow.Core.Domain;
using Winnow.App.ViewModels;

namespace Winnow.App.Views;

public partial class ArtworkBrowserView : UserControl
{
    private ArtworkBrowserViewModel? _observed;
    private readonly Dictionary<ArtworkSlot, Vector> _offsets = [];
    public ArtworkBrowserView()
    {
        InitializeComponent();
        PreviewPanel.SizeChanged += (_, _) =>
        {
            var card = this.FindAncestorOfType<GameDetailsView>()?.FindControl<Border>("Card");
            var desktopRatio = card is { Bounds.Height: > 0 } ? card.Bounds.Width / card.Bounds.Height : 4d / 3;
            DesktopHeroPreview.Height = 400 / desktopRatio;
        };
        SizeChanged += (_, _) =>
        {
            var narrow = Bounds.Width < 610;
            BrowserBody.ColumnDefinitions = new(narrow ? "*" : "3*,2*");
            BrowserBody.RowDefinitions = new(narrow ? "2*,3*" : "*");
            Grid.SetColumn(PreviewPanel, narrow ? 0 : 1);
            Grid.SetRow(CandidateScroll, narrow ? 1 : 0);
        };
    }
    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_observed is not null) { _observed.PropertyChanging -= BeforeModelChanged; _observed.PropertyChanged -= ModelChanged; }
        base.OnDataContextChanged(e);
        _observed = DataContext as ArtworkBrowserViewModel; _offsets.Clear();
        if (_observed is not null) { _observed.PropertyChanging += BeforeModelChanged; _observed.PropertyChanged += ModelChanged; }
    }
    private void BeforeModelChanged(object? sender, PropertyChangingEventArgs e)
    {
        if (e.PropertyName != nameof(ArtworkBrowserViewModel.Slot) || _observed is null) return;
        _offsets[_observed.Slot] = CandidateScroll.Offset;
    }
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ArtworkBrowserViewModel.Slot) || _observed is not { } model) return;
        var slot = model.Slot;
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(model, _observed) || model.Slot != slot) return;
            UpdateLayout();
            var offset = _offsets.GetValueOrDefault(slot);
            CandidateScroll.Offset = new(0, Math.Clamp(offset.Y, 0, Math.Max(0, CandidateScroll.Extent.Height - CandidateScroll.Viewport.Height)));
        }, DispatcherPriority.Loaded);
    }
    private void OnCandidatePressed(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ArtworkBrowserViewModel model && sender is Control { DataContext: ArtworkCandidateViewModel candidate }) model.SelectCommand.Execute(candidate);
    }
    private void OnSourcePressed(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ArtworkBrowserViewModel model && sender is Control { DataContext: ArtworkSourceViewModel source }) model.ChooseSourceCommand.Execute(source.Id);
    }
    private async void OnSourceLinkPressed(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ArtworkBrowserViewModel { SourceLink: { } link } model) return;
        try
        {
            if (this.FindAncestorOfType<GameDetailsView>()?.DataContext is GameDetailsViewModel details
                && await details.OpenReadingLinkAsync(link))
            {
                model.Problem = details.LinkStatus;
                return;
            }
            if (TopLevel.GetTopLevel(this)?.Launcher is not { } launcher || !await launcher.LaunchUriAsync(new Uri(link.Uri)))
                model.Problem = "Could not open the artwork source. Try again.";
        }
        catch (Exception) { model.Problem = "Could not open the artwork source. Try again."; }
    }
}
