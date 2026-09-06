using System.Xml.Linq;
using Xunit;

namespace Winnow.Tests.Enforcement;

public sealed class InteractiveControlNameTests
{
    [Fact]
    public void List_and_filter_names_follow_counts_and_list_renames()
    {
        var list = new Winnow.App.ViewModels.Lists.GameListViewModel(Winnow.Core.Domain.GameList.Manual("Co-op"));
        var changed = new List<string?>();
        list.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        list.Count = 2;
        Assert.Equal("Co-op, 2 games", list.AutomationName);
        Assert.Contains(nameof(list.AutomationName), changed);
        changed.Clear();
        list.Name = "Weekend";
        Assert.Equal("Weekend, 2 games", list.AutomationName);
        Assert.Contains(nameof(list.AutomationName), changed);
        list.Count = 1;
        Assert.Equal("Weekend, 1 game", list.AutomationName);

        var option = new Winnow.App.ViewModels.Filters.FilterOptionViewModel("steam", "Steam", _ => { });
        changed.Clear();
        option.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        option.Count = 1;
        Assert.Equal("Steam, 1 matching title", option.AutomationName);
        Assert.Contains(nameof(option.AutomationName), changed);
        option.Count = 2;
        Assert.Equal("Steam, 2 matching titles", option.AutomationName);
    }

    [Fact]
    public void Interactive_controls_have_a_name_or_plain_text_content()
    {
        string[] interactive = ["Button", "CheckBox", "RadioButton", "ToggleButton", "TextBox", "Slider", "ListBox", "ComboBox", "MenuItem", "Expander"];
        var failures = new List<string>();
        foreach (var file in RepositoryTree.Files("src/Winnow.App/Views", "*.axaml"))
        {
            var document = XDocument.Parse(RepositoryTree.Read(file), LoadOptions.SetLineInfo);
            foreach (var element in document.Descendants().Where(e => interactive.Contains(e.Name.LocalName)
                || ((string?)e.Attribute("Focusable") == "True" && e.Name.LocalName is not "TextBlock" and not "SelectableTextBlock")))
            {
                // Native peers can use plain Content/Header. Composed content is
                // a layout object, whose ToString is not the visible label.
                var name = (string?)element.Attribute("AutomationProperties.Name")
                    ?? (string?)element.Attribute("Content")
                    ?? (string?)element.Attribute("Header");
                if (string.IsNullOrWhiteSpace(name))
                    failures.Add($"{file}:{((System.Xml.IXmlLineInfo)element).LineNumber}: {element.Name.LocalName} needs an accessible name.");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
