using System.ComponentModel;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Core.Domain;

namespace Winnow.App.Views.Fullscreen;

public sealed class FullscreenArtworkPage : FullscreenPage
{
    private readonly ArtworkBrowserViewModel _model;
    private readonly List<ArtworkSourceViewModel> _observed = [];
    private bool _queued, _disposed;
    private readonly Dictionary<ArtworkSlot, Vector> _offsets = [];
    private ScrollViewer? _galleryScroll;
    private ArtworkSlot _gallerySlot;
    private bool _restoringScroll;
    public FullscreenArtworkPage(FullscreenContext context, ArtworkBrowserViewModel model) : base(context)
    {
        _model = model; model.PreviewFullscreen = true; model.PropertyChanged += ModelChanged;
        Build();
    }
    public override string Title => "Change artwork";
    public override string Hints => "A Preview or select   B Back";

    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ArtworkBrowserViewModel.Slot) or nameof(ArtworkBrowserViewModel.VisibleSources) or nameof(ArtworkBrowserViewModel.Current) or nameof(ArtworkBrowserViewModel.SourceLink) or nameof(ArtworkBrowserViewModel.PreviewFullscreen)) QueueBuild();
    }
    private void SourceChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName is nameof(ArtworkSourceViewModel.Message) or nameof(ArtworkSourceViewModel.HasMore) or nameof(ArtworkSourceViewModel.CanRetry)) QueueBuild(); }
    private void ItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => QueueBuild();
    private void QueueBuild()
    {
        if (_queued || _disposed) return;
        _queued = true;
        Dispatcher.UIThread.Post(() => { _queued = false; if (!_disposed) Build(); });
    }
    private void Build()
    {
        var restore = PreserveFocus();
        if (_galleryScroll is not null && !_restoringScroll) _offsets[_gallerySlot] = _galleryScroll.Offset;
        _gallerySlot = _model.Slot;
        foreach (var source in _observed) { source.PropertyChanged -= SourceChanged; source.Items.CollectionChanged -= ItemsChanged; }
        _observed.Clear();
        foreach (var source in _model.Sources)
        { source.PropertyChanged += SourceChanged; source.Items.CollectionChanged += ItemsChanged; _observed.Add(source); }

        var rows = new List<Control[]>();
        var root = new Grid { RowDefinitions = new("Auto,*,Auto"), RowSpacing = 20 };
        var header = FullscreenUi.Stack(FullscreenUi.Text(_model.Title, 32));
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        foreach (var slot in Enum.GetValues<ArtworkSlot>())
        {
            var tab = Action(slot == _model.Slot ? $"{slot} · Selected" : slot.ToString(), () => _model.ChooseSlotCommand.Execute(slot), $"slot:{slot}");
            tab.Bind(IsEnabledProperty, new Binding(nameof(ArtworkBrowserViewModel.CanEdit)) { Source = _model });
            tabs.Children.Add(tab);
        }
        rows.Add(tabs.Children.OfType<Control>().ToArray()); header.Children.Add(tabs);
        var sources = new WrapPanel { Orientation = Orientation.Horizontal };
        sources.Children.Add(Action(_model.SourceId == "all" ? "All sources · Selected" : "All sources", () => _model.ChooseSourceCommand.Execute("all"), "source:all"));
        foreach (var source in _model.Sources)
            sources.Children.Add(Action(_model.SourceId == source.Id ? $"{source.Name} · Selected" : source.Name, () => _model.ChooseSourceCommand.Execute(source.Id), $"source:{source.Id}"));
        header.Children.Add(sources); rows.Add(sources.Children.OfType<Control>().ToArray()); root.Children.Add(header);

        var body = new Grid { ColumnDefinitions = new("3*,2*"), ColumnSpacing = 32 };
        var gallery = new StackPanel { Spacing = 16 };
        if (_model.Current is { } current)
        { var button = Candidate(current); gallery.Children.Add(button); rows.Add([button]); }
        foreach (var source in _model.VisibleSources)
        {
            gallery.Children.Add(FullscreenUi.Text(source.Name, 24, "TextDim"));
            if (source.Message is { } message) gallery.Children.Add(FullscreenUi.Text(message, 24, source.CanRetry ? "AmberForeground" : "TextDim"));
            foreach (var chunk in source.Items.Chunk(3))
            {
                var line = new Grid { ColumnDefinitions = new("*,*,*"), ColumnSpacing = 16 };
                var controls = chunk.Select(Candidate).ToArray();
                for (var index = 0; index < controls.Length; index++) { Grid.SetColumn(controls[index], index); line.Children.Add(controls[index]); }
                gallery.Children.Add(line); rows.Add(controls);
            }
            if (source.CanRetry)
            { var retry = Action("Try again", () => source.RetryCommand.Execute(null), $"retry:{source.Id}"); gallery.Children.Add(retry); rows.Add([retry]); }
            if (source.HasMore)
            { var more = Action("Load more", () => source.MoreCommand.Execute(null), $"more:{source.Id}"); gallery.Children.Add(more); rows.Add([more]); }
        }
        _galleryScroll = FullscreenUi.Scroll(gallery); _galleryScroll.Name = "ArtworkCandidateScroll";
        body.Children.Add(_galleryScroll);
        var preview = FullscreenUi.Stack(FullscreenUi.Text("Preview", 24, "TextDim"));
        var description = FullscreenUi.Text("", 24); description.Bind(TextBlock.TextProperty, new Binding(nameof(ArtworkBrowserViewModel.PreviewDescription)) { Source = _model }); preview.Children.Add(description);
        if (_model.SourceLink is { } link)
        { var sourceLink = Action("Open artwork source", () => Context.OpenLink(link), "attribution"); preview.Children.Add(sourceLink); rows.Add([sourceLink]); }
        if (_model.IsHero)
        {
            var crops = new WrapPanel { Orientation = Orientation.Horizontal };
            var desktop = Action("Desktop crop", () => _model.DesktopCropCommand.Execute(null), "crop:desktop");
            var fullscreen = Action("Fullscreen crop", () => _model.FullscreenCropCommand.Execute(null), "crop:fullscreen");
            desktop.Classes.Set("current", !_model.PreviewFullscreen); fullscreen.Classes.Set("current", _model.PreviewFullscreen);
            desktop.FontSize = fullscreen.FontSize = 24;
            crops.Children.Add(desktop); crops.Children.Add(fullscreen); preview.Children.Add(crops); rows.Add([desktop, fullscreen]);
            preview.Children.Add(FullscreenUi.Text(_model.PreviewFullscreen ? "16:9 preview" : "4:3 example", 22, "TextDim"));
            preview.Children.Add(HeroImage(_model.PreviewFullscreen ? 16d / 9 : 4d / 3));
        }
        else if (_model.IsCover)
        {
            var image = PreviewImage(360, true); image.Width = 240; image.HorizontalAlignment = HorizontalAlignment.Center; preview.Children.Add(image);
        }
        else
        {
            preview.Children.Add(FullscreenUi.Text("Icon · 32 px", 22, "TextDim"));
            var small = PreviewImage(32, false); small.Width = 32; small.Stretch = Stretch.Uniform; small.HorizontalAlignment = HorizontalAlignment.Left; preview.Children.Add(small);
            preview.Children.Add(FullscreenUi.Text("Transparency on light and dark", 22, "TextDim"));
            var backgrounds = new Grid { ColumnDefinitions = new("*,*"), ColumnSpacing = 16 };
            for (var index = 0; index < 2; index++)
            {
                var border = new Border { Padding = new(16) }; border[!Border.BackgroundProperty] = new DynamicResourceExtension(index == 0 ? "Text" : "Well");
                var image = PreviewImage(160, false); image.Stretch = Stretch.Uniform; border.Child = image;
                Grid.SetColumn(border, index); backgrounds.Children.Add(border);
            }
            preview.Children.Add(backgrounds);
        }
        Control previewContent;
        if (_model.IsHero)
        {
            var crop = preview.Children[^1]; preview.Children.Remove(crop);
            var fitted = new Grid { RowDefinitions = new("Auto,*"), RowSpacing = 12 };
            fitted.Children.Add(preview);
            var box = new Viewbox { Child = crop, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            Grid.SetRow(box, 1); fitted.Children.Add(box); previewContent = fitted;
        }
        else previewContent = FullscreenUi.Scroll(preview);
        Grid.SetColumn(previewContent, 1); body.Children.Add(previewContent);
        Grid.SetRow(body, 1); root.Children.Add(body);

        var footer = new StackPanel { Spacing = 12 };
        var status = FullscreenUi.Text("", 22); status.Bind(TextBlock.TextProperty, new Binding(nameof(ArtworkBrowserViewModel.Status)) { Source = _model }); status.Bind(IsVisibleProperty, new Binding(nameof(ArtworkBrowserViewModel.HasStatus)) { Source = _model }); footer.Children.Add(status);
        var problem = FullscreenUi.Text("", 24, "AmberForeground"); problem.Bind(TextBlock.TextProperty, new Binding(nameof(ArtworkBrowserViewModel.Problem)) { Source = _model }); problem.Bind(IsVisibleProperty, new Binding(nameof(ArtworkBrowserViewModel.HasProblem)) { Source = _model }); footer.Children.Add(problem);
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        var apply = Action("Use artwork", () => _model.ApplyCommand.Execute(null), "apply"); apply.Bind(IsEnabledProperty, new Binding(nameof(ArtworkBrowserViewModel.CanApply)) { Source = _model }); actions.Children.Add(apply);
        var automatic = Action("Use automatic", () => _model.AutomaticCommand.Execute(null), "automatic"); actions.Children.Add(automatic);
        var file = Action("Choose file", async () =>
        {
            var path = await Context.PickFile("Choose artwork", [".png", ".jpg", ".jpeg", ".webp", ".bmp"]);
            if (!_disposed && path is not null) await _model.ImportFileAsync(path);
        }, "file"); actions.Children.Add(file);
        var url = Action("Import URL", () => _model.ImportFromUrlCommand.Execute(null), "url");
        actions.Children.Add(url);
        foreach (var action in new[] { automatic, file, url }) action.Bind(IsEnabledProperty, new Binding(nameof(ArtworkBrowserViewModel.CanEdit)) { Source = _model });
        rows.Add(actions.Children.OfType<Control>().ToArray()); footer.Children.Add(actions);
        var urlField = new TextBox { FontSize = 24, MinHeight = 64, Watermark = "Image URL" };
        AutomationProperties.SetName(urlField, "Artwork image URL"); AutomationProperties.SetAutomationId(urlField, "url-field");
        urlField.Bind(TextBox.TextProperty, new Binding(nameof(ArtworkBrowserViewModel.ImportUrl)) { Source = _model, Mode = BindingMode.TwoWay });
        urlField.Bind(IsEnabledProperty, new Binding(nameof(ArtworkBrowserViewModel.CanEdit)) { Source = _model });
        footer.Children.Insert(0, urlField); rows.Insert(rows.Count - 1, [urlField]);
        Grid.SetRow(footer, 2); root.Children.Add(footer);
        Content = root; SetFocusRows(rows.ToArray()); restore(); Changed();
        var scroll = _galleryScroll; var offset = _offsets.GetValueOrDefault(_gallerySlot);
        _restoringScroll = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || !ReferenceEquals(scroll, _galleryScroll)) return;
            UpdateLayout(); scroll.Offset = new(0, Math.Clamp(offset.Y, 0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height)));
            _restoringScroll = false;
        }, DispatcherPriority.Loaded);
    }

    private Button Candidate(ArtworkCandidateViewModel candidate)
    {
        var button = Action(candidate.Description, () => _model.SelectCommand.Execute(candidate), candidate.Id);
        var image = new Image { Height = candidate.IsCurrent ? 100 : 150, Stretch = Stretch.Uniform }; image.Bind(Image.SourceProperty, new Binding(nameof(ArtworkCandidateViewModel.Thumbnail)) { Source = candidate });
        var state = FullscreenUi.Text("", 22, "VoltForeground"); state.Bind(TextBlock.TextProperty, new Binding(nameof(ArtworkCandidateViewModel.StateLabel)) { Source = candidate });
        button.Content = FullscreenUi.Stack(image, state, FullscreenUi.Text(candidate.Description, 22));
        return button;
    }
    private Image PreviewImage(double height, bool cover)
    {
        var image = new Image { Height = height, Stretch = Stretch.UniformToFill, ClipToBounds = true };
        image.Bind(Image.SourceProperty, new Binding(nameof(ArtworkBrowserViewModel.Preview)) { Source = _model });
        if (cover) CoverPresentation.SetIsCover(image, true);
        return image;
    }
    private Image HeroImage(double ratio)
    {
        var image = PreviewImage(180, false);
        image.Width = 180 * ratio; image.HorizontalAlignment = HorizontalAlignment.Left;
        return image;
    }
    private static Button Action(string title, Action action, string id)
    { var button = FullscreenUi.Button(title, action); button.Margin = new(0, 0, 12, 0); AutomationProperties.SetAutomationId(button, id); return button; }
    public override bool Handle(GamepadButtons buttons)
    {
        if (buttons.HasFlag(GamepadButtons.Back)) { if (!_model.IsBusy) { _model.Close(); Context.Back(); } return true; }
        return base.Handle(buttons);
    }
    public override void Dispose()
    {
        _disposed = true; _model.PropertyChanged -= ModelChanged;
        foreach (var source in _observed) { source.PropertyChanged -= SourceChanged; source.Items.CollectionChanged -= ItemsChanged; }
        _model.Close(); base.Dispose();
    }
}
