using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Winnow.App.Services;
using Winnow.App.ViewModels;

namespace Winnow.App.Views.Fullscreen;

/// <summary>Session-linked drafts keep the shared validation and storage commands.</summary>
public sealed class FullscreenDetailsJournalPage : FullscreenPage
{
    private readonly JournalEntryViewModel _entry;
    private readonly TextBox _note;
    private bool _disposed;

    public FullscreenDetailsJournalPage(FullscreenContext context, JournalEntryViewModel entry) : base(context)
    {
        _entry = entry;
        entry.EditCommand.Execute(null);
        _note = new TextBox { FontSize = 28, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            MinHeight = 240, MaxHeight = 420, Watermark = "What were you doing?" };
        _note.Bind(TextBox.TextProperty, new Binding(nameof(JournalEntryViewModel.DraftNote)) { Source = entry, Mode = BindingMode.TwoWay });
        _note.Bind(IsEnabledProperty, new Binding(nameof(JournalEntryViewModel.CanEdit)) { Source = entry });
        AutomationProperties.SetName(_note, "Journal note");
        var edit = FullscreenUi.Button("Edit note", () => Context.EditText(_note));
        var stars = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        var choices = new List<Control>();
        for (var i = 1; i <= 5; i++)
        {
            var rating = i.ToString();
            var button = FullscreenUi.Button($"{i} / 5", () => entry.RateCommand.Execute(rating));
            var property = $"IsDraftRated{i}";
            button.Bind(Button.TagProperty, new Binding(property) { Source = entry });
            button.Bind(IsEnabledProperty, new Binding(nameof(JournalEntryViewModel.CanEdit)) { Source = entry });
            stars.Children.Add(button);
            choices.Add(button);
        }
        var currentRating = FullscreenInformation.Metadata("");
        currentRating.Bind(TextBlock.TextProperty, new Binding(nameof(JournalEntryViewModel.DraftRatingText)) { Source = entry });
        var problem = FullscreenInformation.Text("", brush: "Amber");
        problem.Bind(TextBlock.TextProperty, new Binding(nameof(JournalEntryViewModel.Problem)) { Source = entry });
        var save = FullscreenUi.Button("Save", async () =>
        {
            await entry.SaveCommand.ExecuteAsync(null);
            if (!_disposed && !entry.IsEditing) { Context.Back(); Context.Notify("Journal entry saved."); }
        });
        save.Bind(IsEnabledProperty, new Binding(nameof(JournalEntryViewModel.CanEdit)) { Source = entry });
        var cancel = FullscreenUi.Button("Cancel", Cancel);
        var delete = FullscreenUi.Button("Delete entry", () => Context.ShowActions("Delete this journal entry?",
            [new("Cancel", () => { }), new("Delete entry", async () =>
            {
                entry.RequestDeleteCommand.Execute(null);
                await entry.DeleteCommand.ExecuteAsync(null);
                if (!_disposed && entry.Problem is null) { Context.Back(); Context.Notify("Journal entry deleted."); }
            })]));
        foreach (var button in new[] { edit, cancel, delete })
            button.Bind(IsEnabledProperty, new Binding(nameof(JournalEntryViewModel.CanEdit)) { Source = entry });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24 };
        actions.Children.Add(save);
        actions.Children.Add(cancel);
        actions.Children.Add(delete);
        var body = FullscreenInformation.Column("FullscreenJournalEditor");
        body.Children.Add(FullscreenInformation.Heading("Journal"));
        body.Children.Add(FullscreenInformation.Metadata(entry.DateText));
        body.Children.Add(FullscreenInformation.Title("What were you doing?"));
        body.Children.Add(_note);
        body.Children.Add(edit);
        FullscreenInformation.AddSection(body, "Rating");
        body.Children.Add(FullscreenInformation.Title("How was that?"));
        body.Children.Add(stars);
        body.Children.Add(currentRating);
        body.Children.Add(problem);
        body.Children.Add(FullscreenInformation.Rule());
        body.Children.Add(actions);
        Content = FullscreenUi.Scroll(body);
        SetFocusRows([edit], choices.ToArray(), [save, cancel, delete]);
    }

    public override string Title => "Edit journal entry";
    public override string Hints => "A Select   Y Keyboard   B Cancel";

    public override bool Handle(GamepadButtons buttons)
    {
        if (buttons.HasFlag(GamepadButtons.Keyboard)) { if (_entry.CanEdit) Context.EditText(_note); return true; }
        if (buttons.HasFlag(GamepadButtons.Back)) { Cancel(); return true; }
        return base.Handle(buttons);
    }

    private void Cancel()
    {
        if (_entry.IsSaving) return;
        _entry.CancelEditCommand.Execute(null);
        Context.Back();
    }

    public override void Dispose() { _disposed = true; base.Dispose(); }
}
