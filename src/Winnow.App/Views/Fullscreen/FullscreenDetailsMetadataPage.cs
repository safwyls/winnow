using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Winnow.App.Services;
using Winnow.App.ViewModels;

namespace Winnow.App.Views.Fullscreen;

public sealed class FullscreenDetailsMetadataPage : FullscreenPage
{
    public FullscreenDetailsMetadataPage(FullscreenContext context, GameMetadataEditorViewModel editor) : base(context)
    {
        var content = FullscreenUi.Stack(FullscreenUi.Text(editor.SectionHeading, 32), FullscreenUi.Text(editor.Intro));
        var rows = new List<Control[]>();
        foreach (var field in editor.Rows)
        {
            var button = FullscreenUi.Button($"{field.Label} · {field.SourceLabel}", () =>
                Context.Push(new FullscreenDetailsFieldPage(Context, field)));
            content.Children.Add(button);
            rows.Add([button]);
        }
        var problem = FullscreenUi.Text("", 28, "Amber");
        problem.Bind(TextBlock.TextProperty, new Binding(nameof(GameMetadataEditorViewModel.Problem)) { Source = editor });
        content.Children.Add(problem);
        var back = FullscreenUi.Button("Back", Context.Back);
        content.Children.Add(back);
        rows.Add([back]);
        Content = FullscreenUi.Scroll(content);
        SetFocusRows(rows.ToArray());
    }

    public override string Title => "Edit details";
    public override string Hints => "A Edit field   B Back";
}

public sealed class FullscreenDetailsFieldPage : FullscreenPage
{
    private readonly MetadataFieldRowViewModel _field;
    private readonly TextBox _text;
    private readonly string _original;

    public FullscreenDetailsFieldPage(FullscreenContext context, MetadataFieldRowViewModel field) : base(context)
    {
        _field = field;
        _original = field.Draft;
        _text = new TextBox { FontSize = 28, AcceptsReturn = field.IsMultiline,
            TextWrapping = TextWrapping.Wrap, MinHeight = field.IsMultiline ? 240 : 64,
            Watermark = field.Watermark };
        _text.Bind(TextBox.TextProperty, new Binding(nameof(MetadataFieldRowViewModel.Draft)) { Source = field, Mode = BindingMode.TwoWay });
        AutomationProperties.SetName(_text, field.FieldAutomationName);
        var edit = FullscreenUi.Button("Edit value", () => Context.EditText(_text));
        var save = FullscreenUi.Button(field.SaveLabel, async () =>
        {
            await field.SaveCommand.ExecuteAsync(null);
            if (field.Problem is null) { Context.Back(); Context.Notify(field.Note ?? "Saved."); }
        });
        save.Bind(IsEnabledProperty, new Binding(nameof(MetadataFieldRowViewModel.CanSave)) { Source = field });
        var reset = FullscreenUi.Button(field.ResetLabel, () => Context.ShowActions($"Reset {field.Label}?",
            [new("Cancel", () => { }), new(field.ResetLabel, async () =>
            {
                await field.ResetCommand.ExecuteAsync(null);
                if (field.Problem is null) { Context.Back(); Context.Notify(field.Note ?? "Reset."); }
            })]));
        reset.Bind(IsEnabledProperty, new Binding(nameof(MetadataFieldRowViewModel.CanReset)) { Source = field });
        var cancel = FullscreenUi.Button("Cancel", Cancel);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24 };
        actions.Children.Add(save);
        actions.Children.Add(reset);
        actions.Children.Add(cancel);
        var error = FullscreenUi.Text("", 28, "Amber");
        error.Bind(TextBlock.TextProperty, new Binding(nameof(MetadataFieldRowViewModel.Problem)) { Source = field });
        var content = FullscreenUi.Stack(FullscreenUi.Text(field.Label, 32),
            FullscreenUi.Text(field.SourceTooltip, 24, "TextDim"), _text, edit, error, actions);
        List<Control[]> focusRows = [[edit]];
        if (field is MetadataArtRowViewModel art)
        {
            var file = FullscreenUi.Button(art.ChooseFileLabel, async () =>
            {
                var path = await Context.PickFile($"Choose {field.Label}", [".png", ".jpg", ".jpeg", ".webp", ".bmp"]);
                if (path is null) return;
                await art.ImportFileAsync(path);
                if (art.Problem is null) { Context.Back(); Context.Notify(art.Note ?? "Image saved."); }
            });
            content.Children.Insert(4, file);
            focusRows.Add([file]);
        }
        focusRows.Add([save, reset, cancel]);
        Content = FullscreenUi.Scroll(content);
        SetFocusRows(focusRows.ToArray());
    }

    public override string Title => _field.Label;
    public override string Hints => "A Select   Y Keyboard   B Cancel";
    public override bool Handle(GamepadButtons buttons)
    {
        if (buttons.HasFlag(GamepadButtons.Keyboard)) { Context.EditText(_text); return true; }
        if (buttons.HasFlag(GamepadButtons.Back)) { Cancel(); return true; }
        return base.Handle(buttons);
    }

    private void Cancel()
    {
        _field.Draft = _original;
        Context.Back();
    }
}
