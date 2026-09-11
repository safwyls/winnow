using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Winnow.App.ViewModels;

namespace Winnow.App.Views.Fullscreen;

public sealed class FullscreenArtworkOrderPage : FullscreenPage
{
    private readonly ArtworkOrderViewModel _model;
    private bool _disposed;
    public override string Title => "Artwork source order";

    public FullscreenArtworkOrderPage(FullscreenContext context) : base(context)
    {
        _model = context.Shared.EnrichmentSettings.ArtworkOrder;
        Render();
    }

    private void Render(string? restoreSource = null, int direction = 0)
    {
        var content = FullscreenUi.Stack(FullscreenUi.Text(Title, 64),
            FullscreenUi.Text(ArtworkOrderViewModel.Explanation, 28, "TextDim"));
        var focus = new List<Control[]>();
        Button? restore = null;
        foreach (var source in _model.Sources)
        {
            var up = FullscreenUi.Button("Move up", () => _ = MoveAsync(source.SourceId, -1));
            up.IsEnabled = source.MoveUpCommand.CanExecute(source.SourceId);
            AutomationProperties.SetName(up, source.MoveUpLabel);
            var down = FullscreenUi.Button("Move down", () => _ = MoveAsync(source.SourceId, 1));
            down.IsEnabled = source.MoveDownCommand.CanExecute(source.SourceId);
            AutomationProperties.SetName(down, source.MoveDownLabel);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 16, Margin = new Thickness(0, 12) };
            row.Children.Add(FullscreenUi.Text(source.Label, 28));
            Grid.SetColumn(up, 1); row.Children.Add(up);
            Grid.SetColumn(down, 2); row.Children.Add(down);
            content.Children.Add(row);
            focus.Add([up, down]);
            if (source.SourceId == restoreSource) restore = direction < 0
                ? source.MoveUpCommand.CanExecute(source.SourceId) ? up : down
                : source.MoveDownCommand.CanExecute(source.SourceId) ? down : up;
        }
        var status = FullscreenUi.Text("", 28, "TextDim");
        status.Bind(TextBlock.TextProperty, new Binding(nameof(_model.Status)) { Source = _model });
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        content.Children.Add(status);
        var back = FullscreenUi.Button("Back", Context.Back);
        content.Children.Add(back);
        focus.Add([back]);
        Content = FullscreenUi.Scroll(content);
        SetFocusRows(focus.ToArray());
        if (restore is not null) FocusControl(restore);
    }

    private async Task MoveAsync(string source, int direction)
    {
        var command = direction < 0 ? _model.MoveUpCommand : _model.MoveDownCommand;
        if (!command.CanExecute(source)) return;
        await command.ExecuteAsync(source);
        if (_disposed) return;
        Render(source, direction);
    }

    public override void Dispose()
    {
        _disposed = true;
        base.Dispose();
    }
}
