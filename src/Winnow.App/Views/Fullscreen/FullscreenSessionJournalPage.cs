using Avalonia.Automation;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Winnow.App.Services;
using Winnow.App.ViewModels;

namespace Winnow.App.Views.Fullscreen;

/// <summary>The shared session prompt gets a TV presentation without a second session subscription.</summary>
public sealed class FullscreenSessionJournalPage : FullscreenPage
{
    private readonly JournalPromptViewModel _prompt;
    private readonly TextBox _note;

    public FullscreenSessionJournalPage(FullscreenContext context, JournalPromptViewModel prompt) : base(context)
    {
        _prompt = prompt;
        var title = FullscreenUi.Text(prompt.Title, 64);
        title.Bind(TextBlock.TextProperty, new Binding(nameof(JournalPromptViewModel.Title)) { Source = prompt });
        var duration = FullscreenUi.Text(prompt.DurationText, 28, "TextDim");
        duration.Bind(TextBlock.TextProperty, new Binding(nameof(JournalPromptViewModel.DurationText)) { Source = prompt });
        _note = new TextBox { FontSize = 28, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 240,
            Watermark = "What were you doing? (helps when you come back)" };
        AutomationProperties.SetName(_note, "Journal note");
        _note.Bind(TextBox.TextProperty, new Binding(nameof(JournalPromptViewModel.Note)) { Source = prompt, Mode = BindingMode.TwoWay });
        var edit = FullscreenUi.Button("Edit note", () => Context.EditText(_note));
        var stars = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        var choices = new List<Control>();
        for (var i = 1; i <= 5; i++)
        {
            var rating = i.ToString();
            var button = FullscreenUi.Button($"{i} / 5", () => prompt.RateCommand.Execute(rating));
            stars.Children.Add(button);
            choices.Add(button);
        }
        var current = FullscreenUi.Text("", 28, "TextDim");
        current.Bind(TextBlock.TextProperty, new Binding(nameof(JournalPromptViewModel.Rating)) { Source = prompt, StringFormat = "Rating: {0} / 5" });
        var problem = FullscreenUi.Text("", 28, "Amber");
        problem.Bind(TextBlock.TextProperty, new Binding(nameof(JournalPromptViewModel.Problem)) { Source = prompt });
        var save = FullscreenUi.Button("Save", async () => await prompt.SaveCommand.ExecuteAsync(null));
        save.Bind(ContentControl.ContentProperty, new Binding(nameof(JournalPromptViewModel.SaveLabel)) { Source = prompt });
        var dismiss = FullscreenUi.Button("Dismiss", () => prompt.DismissCommand.Execute(null));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24 };
        actions.Children.Add(save);
        actions.Children.Add(dismiss);
        Content = FullscreenUi.Scroll(FullscreenUi.Stack(title, duration, _note, edit,
            FullscreenUi.Text("How was that?"), stars, current, problem, actions));
        SetFocusRows([edit], choices.ToArray(), [save, dismiss]);
        prompt.PropertyChanged += PromptChanged;
    }
    private void PromptChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(JournalPromptViewModel.IsSaving))
        {
            IsEnabled = !_prompt.IsSaving;
            Changed();
        }
    }
    public override void Dispose()
    {
        _prompt.PropertyChanged -= PromptChanged;
        base.Dispose();
    }

    public override string Title => "Your last session";
    public override string Hints => _prompt.IsSaving ? "Saving…" : "A Select   Y Keyboard   B Dismiss";
    public override bool Handle(GamepadButtons buttons)
    {
        if (_prompt.IsSaving) return true;
        if (buttons.HasFlag(GamepadButtons.Keyboard)) { Context.EditText(_note); return true; }
        if (buttons.HasFlag(GamepadButtons.Back)) { _prompt.DismissCommand.Execute(null); return true; }
        return base.Handle(buttons);
    }
}
