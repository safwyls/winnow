using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Xunit;

namespace Winnow.Tests.Enforcement;

/// <summary>
/// One rule: an accessible name must sit on an element a screen reader can
/// reach. Avalonia gives a Control that does not override
/// <c>OnCreateAutomationPeer</c> a <c>NoneAutomationPeer</c>, whose
/// <c>IsControlElementCore</c> is false, and the Win32 provider maps that onto
/// <c>UIA_IsControlElementPropertyId</c> — what the control view filters on. A
/// name set on a Border, a Panel, a Grid or a ContentPresenter is therefore
/// dropped before anything reads it.
///
/// <para>This is held mechanically because the failure mode is silent. The
/// element keeps its children, so the window looks right, nothing throws and no
/// other test notices; a name that has stopped reaching a screen reader is
/// indistinguishable from one that arrives. Every game tile in the library was
/// nameless that way, and stayed nameless.</para>
///
/// <para>The rule is asked of Avalonia's own metadata by reflection rather than
/// of a list kept here, so an Avalonia release that gives a type a peer relaxes
/// it on its own and nobody has to remember to. No control is instantiated,
/// which is what keeps these tests free of a dispatcher and an
/// <c>Application</c>.</para>
/// </summary>
public sealed class AutomationNameReachabilityTests
{
    /// <summary>
    /// The one hierarchy that has a peer and still cannot be named.
    /// <c>TextBlockAutomationPeer.GetNameCore</c> returns
    /// <c>Owner.Inlines?.Text ?? Owner.Text</c> and never calls base, so
    /// <c>AutomationProperties.Name</c> on a TextBlock is discarded even though
    /// the peer is a control element. A TextBlock says its own Text and nothing
    /// else, which is a second way to be silently wrong and needs its own
    /// failure message.
    /// </summary>
    private static readonly Type TextHierarchy = typeof(TextBlock);

    /// <summary>
    /// Where an element name is looked up: Avalonia's own controls, and
    /// Winnow's. Between them they hold every type the app's markup can put a
    /// name on; a tag that resolves in neither is reported rather than passed,
    /// because an undecidable case is how a rule quietly stops applying.
    /// </summary>
    private static readonly Assembly[] ControlAssemblies =
    [
        typeof(Border).Assembly,
        typeof(Winnow.App.ViewModels.GameTileViewModel).Assembly,
    ];

    /// <summary>
    /// The two values that buy an exemption.
    /// <c>ControlAutomationPeer.IsControlElementOverrideCore</c> consults
    /// <c>AutomationProperties.AccessibilityView</c> before falling back to the
    /// peer's own answer, so either of these puts an otherwise-pruned element
    /// back in the control view with its name intact. It is the escape hatch for
    /// a field with no peer-bearing control in it to move the name onto.
    /// </summary>
    private static readonly string[] ExposingViews = ["Control", "Content"];

    [Fact]
    public void Every_accessible_name_sits_on_an_element_that_reaches_the_control_view()
    {
        var failures = new List<string>();

        foreach (var file in RepositoryTree.Files("src/Winnow.App", "*.axaml"))
        {
            var text = Blanked(RepositoryTree.Read(file));

            foreach (Match match in Regex.Matches(text, @"AutomationProperties\.Name\s*="))
            {
                var tag = StartTagAt(text, match.Index);
                var line = RepositoryTree.LineAt(text, match.Index);

                if (tag is null)
                {
                    failures.Add($"{file}:{line} — the element carrying the name could not be read.");
                    continue;
                }

                var (element, markup) = tag.Value;
                var type = Resolve(element);

                if (type is null)
                {
                    failures.Add(
                        $"{file}:{line} — <{element}> did not resolve to an Avalonia Control, so "
                        + "whether its name reaches a screen reader cannot be decided here.");
                    continue;
                }

                if (TextHierarchy.IsAssignableFrom(type))
                {
                    failures.Add(
                        $"{file}:{line} — <{element}> is a TextBlock. Its peer returns the Text and "
                        + "never consults AutomationProperties.Name, so the name is discarded. Bind "
                        + "Text, or move the name to the control around it.");
                    continue;
                }

                if (HasOwnPeer(type) || IsExposedByHand(markup))
                {
                    continue;
                }

                failures.Add(
                    $"{file}:{line} — <{element}> does not override OnCreateAutomationPeer, so "
                    + "Avalonia gives it a NoneAutomationPeer and UIA drops it from the control "
                    + "view. The name never reaches a screen reader. Move the name to a control "
                    + "that has a peer of its own, or add "
                    + "AutomationProperties.AccessibilityView=\"Control\" to this element.");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// The two surfaces the library is made of, pinned by the element that
    /// carries their name. The rule above only says that a name which exists is
    /// reachable; a name that was deleted, or moved back down onto the Border it
    /// came off, is the same silence to a reader. These two are the tiles on the
    /// wall and the cards in the feed, so between them they are most of what the
    /// app shows.
    /// </summary>
    [Theory]
    [InlineData("src/Winnow.App/Views/GameTileView.axaml", "UserControl")]
    [InlineData("src/Winnow.App/Views/FeedCardView.axaml", "Button")]
    public void The_tile_and_the_card_name_themselves_on_their_outermost_control(
        string file, string element)
    {
        var text = Blanked(RepositoryTree.Read(file));
        var match = Regex.Match(text, @"AutomationProperties\.Name\s*=");

        Assert.True(match.Success, $"{file} sets no accessible name at all.");

        var tag = StartTagAt(text, match.Index);
        Assert.NotNull(tag);
        Assert.Equal(element, tag!.Value.Element);
    }

    /// <summary>
    /// The same rule, asked of Avalonia rather than read off the markup. The two
    /// tests above reason from metadata; this one builds the host control each
    /// view now names — a UserControl for the tile, a Button for the card — sets
    /// the accessible name the way the compiled binding does, creates the
    /// control's real automation peer, and reads <c>IsControlElement()</c> and
    /// <c>GetName()</c> back off it. The first of those is what the Win32
    /// provider hands to UIA as <c>UIA_IsControlElementPropertyId</c>, and the
    /// control view it gates is the tree a screen reader walks.
    ///
    /// <para>The Border is the element the tile's name sat on until this task,
    /// and it carries the point: its peer still holds the string — which is
    /// exactly what let the bug survive — while reporting itself outside the
    /// control view. The name is dropped on the way out, not on the way in.</para>
    ///
    /// <para>This is the layer directly beneath a screen reader, and as close to
    /// one as a test can get without a running window. It does not replace a
    /// pass with NVDA or Narrator.</para>
    /// </summary>
    [Fact]
    public void Avalonias_own_peers_hand_back_the_name_from_the_new_host_and_not_from_the_old()
    {
        var tile = Winnow.Tests.TileFixture.Tile(
            new DateTime(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc),
            title: "Deep Rock Galactic",
            bucket: Winnow.Core.Queries.LibraryBuckets.NeverPlayed);

        var name = tile.AutomationName;

        // The tile's host, as GameTileView.axaml now declares it.
        var host = new UserControl();
        AutomationProperties.SetName(host, name);

        Assert.True(PeerFor(host).IsControlElement());
        Assert.Equal(name, PeerFor(host).GetName());

        // The feed card's host, as FeedCardView.axaml now declares it.
        var card = new Button();
        AutomationProperties.SetName(card, name);

        Assert.True(PeerFor(card).IsControlElement());
        Assert.Equal(name, PeerFor(card).GetName());

        // The element the name came off. The peer still holds the string — that
        // is the trap — but it reports itself out of the control view, so the
        // name is dropped before a reader ever asks for it.
        var pruned = new Border();
        AutomationProperties.SetName(pruned, name);

        Assert.False(PeerFor(pruned).IsControlElement());
    }

    /// <summary>
    /// The peer the control would really be given.
    /// <c>OnCreateAutomationPeer</c> is invoked directly rather than through
    /// <c>Control.GetOrCreateAutomationPeer</c>, which calls
    /// <c>VerifyAccess</c> and would demand a dispatcher this suite starts no
    /// <c>Application</c> for and has no reason to. It is the protected virtual
    /// the framework itself calls, so the peer that comes back is the same
    /// object the control would have cached.
    /// </summary>
    private static AutomationPeer PeerFor(Control control)
    {
        var factory = typeof(Control).GetMethod(
            "OnCreateAutomationPeer", BindingFlags.Instance | BindingFlags.NonPublic)!;

        return (AutomationPeer)factory.Invoke(control, null)!;
    }

    /// <summary>
    /// Comments blanked to spaces rather than removed, line breaks kept, so
    /// every offset and every reported line number still matches the file on
    /// disk. A comment that mentions AutomationProperties.Name — several in
    /// these views do, since that is what they are explaining — would otherwise
    /// be read as markup and judged against whatever tag stands above it.
    /// </summary>
    private static string Blanked(string text) =>
        Regex.Replace(
            text,
            "<!--.*?-->",
            m => new string([.. m.Value.Select(c => c == '\n' || c == '\r' ? c : ' ')]),
            RegexOptions.Singleline);

    /// <summary>
    /// The element an attribute belongs to is the nearest start tag before it.
    /// The whole tag comes back rather than just the name, because the
    /// exemption is a sibling attribute and has to be read off the same tag.
    /// Quotes are tracked so a '&gt;' inside an attribute value does not end the
    /// tag early.
    /// </summary>
    private static (string Element, string Markup)? StartTagAt(string text, int offset)
    {
        var open = -1;
        for (var i = offset; i >= 0; i--)
        {
            if (text[i] == '<' && i + 1 < text.Length && (char.IsLetter(text[i + 1]) || text[i + 1] == '_'))
            {
                open = i;
                break;
            }
        }

        if (open < 0)
        {
            return null;
        }

        var quote = '\0';
        for (var i = open; i < text.Length; i++)
        {
            var c = text[i];

            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '>')
            {
                var markup = text[open..(i + 1)];
                var name = Regex.Match(markup, @"^<([A-Za-z_][A-Za-z0-9_.:-]*)").Groups[1].Value;
                var local = name[(name.IndexOf(':') + 1)..];
                return (local, markup);
            }
        }

        return null;
    }

    /// <summary>
    /// The tag's local name to a type. The search is restricted to Control
    /// subclasses so that a non-visual type sharing a name cannot answer for a
    /// control and hand out a pass the control itself would not get.
    /// </summary>
    private static Type? Resolve(string element)
    {
        foreach (var assembly in ControlAssemblies)
        {
            foreach (var type in assembly.GetExportedTypes())
            {
                if (type.Name == element && typeof(Control).IsAssignableFrom(type))
                {
                    return type;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Whether the type answers <c>OnCreateAutomationPeer</c> itself instead of
    /// inheriting <c>Control</c>'s, which returns a <c>NoneAutomationPeer</c>.
    /// Asked of Avalonia's own metadata rather than of a list of type names kept
    /// here, so an Avalonia release that gives a type a peer relaxes the rule
    /// without anyone editing this file. Nothing is instantiated — a
    /// <c>MethodInfo</c> is the whole answer — which is what lets these tests
    /// run without a dispatcher or an <c>Application</c>.
    /// </summary>
    private static bool HasOwnPeer(Type type)
    {
        var method = type.GetMethod(
            "OnCreateAutomationPeer",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        return method is not null && method.DeclaringType != typeof(Control);
    }

    /// <summary>
    /// Whether the tag claims its exemption on its own face. The attribute has
    /// to be on the same element as the name, because that is the element whose
    /// peer reads it. See <see cref="ExposingViews"/>.
    /// </summary>
    private static bool IsExposedByHand(string markup) =>
        ExposingViews.Any(v => markup.Contains(
            $"AutomationProperties.AccessibilityView=\"{v}\"", StringComparison.Ordinal));
}
