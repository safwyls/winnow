using System.Globalization;
using System.Text;
using Winnow.App.Themes;

namespace Winnow.App.Services;

/// <summary>
/// The folder of user themes: where it is, what is in it, and when it changed.
///
/// <para><b>The only type in the engine that touches a file.</b>
/// <c>ThemeJson</c> takes text and returns a theme or a list of complaints, and
/// keeping the IO out here is what makes "a theme file is data" checkable rather
/// than asserted — there is no path in the document for anything to
/// dereference, because the document never reaches anything that could.</para>
///
/// <para><b>Nothing here throws either.</b> A themes folder is a place a person
/// keeps files, so it can be missing, read-only, on a disconnected network
/// drive, or full of things that are not themes. All of those are conditions to
/// report, and none of them is a reason the settings screen should not
/// open.</para>
/// </summary>
public sealed class UserThemeStore : IDisposable
{
    /// <summary>How many files the folder is read from. A generous bound rather
    /// than a design limit: someone who has 64 themes has a directory listing
    /// problem, and the Appearance screen would be unusable long before
    /// here.</summary>
    public const int MaxThemes = 64;

    /// <summary>
    /// How long the watcher waits after the last write before re-reading.
    ///
    /// <para>An editor saving a file produces two to four events, and half of
    /// them arrive while the file is still truncated — reading on the first one
    /// reliably reports "the file is empty" a moment before it reports the
    /// theme. The debounce is what turns hot reload from a source of spurious
    /// diagnostics into the feature it is supposed to be.</para>
    /// </summary>
    private static readonly TimeSpan ReloadDelay = TimeSpan.FromMilliseconds(250);

    private readonly Lock _gate = new();
    private FileSystemWatcher? _watcher;
    private Timer? _debounce;
    private bool _disposed;

    public UserThemeStore(string? directory = null)
    {
        Directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Winnow",
            "themes");
    }

    /// <summary>Raised on a background thread after a file in the folder
    /// changed and settled. The subscriber marshals to the UI thread.</summary>
    public event EventHandler? Changed;

    /// <summary>Where the themes live. Printed on the Appearance screen, because
    /// a folder nobody can find is a feature nobody has.</summary>
    public string Directory { get; }

    /// <summary>
    /// Creates the folder if it is missing, and puts a working theme in it if it
    /// is empty.
    ///
    /// <para><b>An empty folder is the wrong first thing to show an author.</b>
    /// It says the feature exists and nothing else - not the shape of a file,
    /// not which fields are seeds, not that <c>edge</c> is a contrast ratio. So
    /// the first thing in the folder is the default theme exported through the
    /// same code path a user theme is read by, with a header explaining the two
    /// kinds of field. Copy it, change the id, change eight colours.</para>
    ///
    /// <para>Only when the folder is EMPTY. It is a folder the user owns; a
    /// file re-appearing after they deleted it is the app arguing with
    /// them.</para>
    /// </summary>
    public IReadOnlyList<ThemeDiagnostic> EnsureSeeded()
    {
        try
        {
            if (!System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.CreateDirectory(Directory);
            }

            if (System.IO.Directory.EnumerateFiles(Directory, "*.json").Any())
            {
                return [];
            }

            File.WriteAllText(
                Path.Combine(Directory, "example.json"),
                ExampleText(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return
            [
                new ThemeDiagnostic(
                    ThemeSeverity.Warning,
                    "themes folder",
                    string.Empty,
                    $"could not be created or written to ({ex.GetType().Name}). User themes are off until it can be: {Directory}"),
            ];
        }
    }

    /// <summary>
    /// Reads every <c>*.json</c> in the folder.
    ///
    /// <para>Top level only, and never recursive: a themes folder with a
    /// <c>node_modules</c> in it is not a case worth being clever about, and a
    /// recursive scan of a folder the user controls is a scan of whatever they
    /// happened to drop there.</para>
    /// </summary>
    public (IReadOnlyList<WinnowTheme> Themes, IReadOnlyList<ThemeDiagnostic> Diagnostics) Load()
    {
        var themes = new List<WinnowTheme>();
        var log = new List<ThemeDiagnostic>();

        string[] files;
        try
        {
            if (!System.IO.Directory.Exists(Directory))
            {
                return (themes, log);
            }

            files = [.. System.IO.Directory
                .EnumerateFiles(Directory, "*.json", SearchOption.TopDirectoryOnly)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Add(new ThemeDiagnostic(
                ThemeSeverity.Warning, "themes folder", string.Empty,
                $"could not be listed ({ex.GetType().Name}): {Directory}"));
            return (themes, log);
        }

        if (files.Length > MaxThemes)
        {
            log.Add(new ThemeDiagnostic(
                ThemeSeverity.Warning, "themes folder", string.Empty,
                $"holds {files.Length} json files; only the first {MaxThemes} by name are read."));
            files = [.. files.Take(MaxThemes)];
        }

        var claimed = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var path in files)
        {
            var name = Path.GetFileName(path);
            string text;

            try
            {
                var info = new FileInfo(path);
                if (info.Length > ThemeJson.MaxFileBytes)
                {
                    log.Add(new ThemeDiagnostic(
                        ThemeSeverity.Error, name, string.Empty,
                        $"is {info.Length / 1024} KB. A theme file is a few kilobytes; anything over {ThemeJson.MaxFileBytes / 1024} KB is not read."));
                    continue;
                }

                text = File.ReadAllText(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.Add(new ThemeDiagnostic(
                    ThemeSeverity.Error, name, string.Empty,
                    $"could not be read ({ex.GetType().Name})."));
                continue;
            }

            var (theme, diagnostics) = ThemeJson.Parse(name, text);
            log.AddRange(diagnostics);

            if (theme is null)
            {
                continue;
            }

            // Two files claiming one id is not a merge to resolve — the setting
            // stores the id, so the second one could never be selected and
            // picking a winner silently would make which theme you get depend on
            // directory order.
            if (claimed.TryGetValue(theme.Id, out var owner))
            {
                log.Add(new ThemeDiagnostic(
                    ThemeSeverity.Error, name, "id",
                    $"\"{theme.Id}\" is already used by {owner}. Two files cannot share an id - the setting stores it, so only one of them could ever be picked."));
                continue;
            }

            claimed[theme.Id] = name;
            themes.Add(theme);
        }

        return (themes, log);
    }

    /// <summary>
    /// Writes a theme into the folder as a starting template, without
    /// overwriting anything.
    /// </summary>
    /// <returns>The file name written, or <c>null</c> with a diagnostic.</returns>
    public (string? FileName, ThemeDiagnostic? Problem) Export(WinnowTheme theme)
    {
        try
        {
            if (!System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.CreateDirectory(Directory);
            }

            var stem = theme.Id;
            var name = $"{stem}.json";
            for (var n = 2; File.Exists(Path.Combine(Directory, name)) && n < 100; n++)
            {
                name = string.Create(CultureInfo.InvariantCulture, $"{stem}-{n}.json");
            }

            File.WriteAllText(
                Path.Combine(Directory, name),
                ThemeJson.Export(theme),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return (name, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return (null, new ThemeDiagnostic(
                ThemeSeverity.Error, "themes folder", string.Empty,
                $"could not be written to ({ex.GetType().Name}): {Directory}"));
        }
    }

    /// <summary>
    /// Starts watching the folder, so an author editing a palette sees it
    /// without restarting.
    ///
    /// <para><b>Cheap enough to be worth it, and this is the whole cost.</b> One
    /// <see cref="FileSystemWatcher"/> on one folder, a debounce timer, and a
    /// re-read of a few kilobytes. The reload path is the load path — nothing
    /// about applying a theme is different because it arrived from a file that
    /// changed — and the repaint is the one <c>ThemeService</c> already does by
    /// writing colours onto the brushes the views resolved, which costs an
    /// invalidation rather than a rebuild.</para>
    ///
    /// <para>A watcher that cannot be created is not an error: the feature
    /// degrades to "reload after restart", which is what it would have been
    /// anyway.</para>
    /// </summary>
    public void Watch()
    {
        lock (_gate)
        {
            if (_disposed || _watcher is not null || !System.IO.Directory.Exists(Directory))
            {
                return;
            }

            try
            {
                _debounce = new Timer(_ => Changed?.Invoke(this, EventArgs.Empty));

                var watcher = new FileSystemWatcher(Directory, "*.json")
                {
                    NotifyFilter = NotifyFilters.LastWrite
                        | NotifyFilters.FileName
                        | NotifyFilters.Size
                        | NotifyFilters.CreationTime,
                    IncludeSubdirectories = false,
                };

                watcher.Changed += OnFileEvent;
                watcher.Created += OnFileEvent;
                watcher.Deleted += OnFileEvent;
                watcher.Renamed += OnFileEvent;

                // A watcher whose buffer overflows stops reporting. Saying so
                // is better than a hot reload that quietly stopped working.
                watcher.Error += (_, _) => Nudge();

                watcher.EnableRaisingEvents = true;
                _watcher = watcher;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                _debounce?.Dispose();
                _debounce = null;
                _watcher = null;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            if (_watcher is not null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
                _watcher = null;
            }

            _debounce?.Dispose();
            _debounce = null;
        }
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e) => Nudge();

    private void Nudge()
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                _debounce?.Change(ReloadDelay, Timeout.InfiniteTimeSpan);
            }
        }
    }

    /// <summary>
    /// The file written into an empty folder: an explanation, then the default
    /// theme exported through the same code the reader parses.
    ///
    /// <para>The header is comments rather than a separate README because a
    /// README is a second file that goes stale; this one is beside the fields it
    /// describes, and the fields under it were generated from the same tables
    /// the parser validates against, so the example cannot describe a format the
    /// build does not have.</para>
    /// </summary>
    private static string ExampleText()
    {
        var body = new StringBuilder();
        body.AppendLine("// A Winnow theme. Copy this file, change the id, and edit.");
        body.AppendLine("//");
        body.AppendLine("// SEEDS are the eight colours that ARE the theme - nothing else in it can be");
        body.AppendLine("// guessed from anything else. Two build the room (ground is the field the covers");
        body.AppendLine("// hang in, surface is the chrome, and the jump between them is most of what makes");
        body.AppendLine("// one theme a different room from another). One is the ink. Five are the roles:");
        body.AppendLine("//");
        body.AppendLine("//   flare   unread updates, and the bucket counting them. NOTHING else, ever.");
        body.AppendLine("//   volt    selection and recency - the room at full voltage.");
        body.AppendLine("//   amber   \"you have been here a lot\".");
        body.AppendLine("//   azure   informational, links, secondary counts.");
        body.AppendLine("//   danger  the one destructive affordance.");
        body.AppendLine("//");
        body.AppendLine("// A theme may change which colour plays a role. It may not change what a role");
        body.AppendLine("// MEANS, and it may not spend one role's colour on a second job. Winnow warns");
        body.AppendLine("// about that rather than refusing - it is your theme - and prints what it costs.");
        body.AppendLine("//");
        body.AppendLine("// STRUCTURE and TRANSLUCENCY are proportions, and every one of them has a house");
        body.AppendLine("// default: delete the blocks entirely and the theme still works.");
        body.AppendLine("//");
        body.AppendLine("//   elevation    how far a hover or selection lifts a surface.");
        body.AppendLine("//   wellDepth    where the deepest tone sits, as a fraction of ground.");
        body.AppendLine("//   edge         Line's CONTRAST RATIO against surface. The most expressive");
        body.AppendLine("//                number here: 1.38 is felt where nothing has a hard boundary,");
        body.AppendLine("//                2.46 is glass with the layout scribed on it.");
        body.AppendLine("//   dimValue     the metadata ink's brightness, and");
        body.AppendLine("//   dimChroma    its share of the room's own colour.");
        body.AppendLine("//   voltInkContrast");
        body.AppendLine("//                how readable the ink ON a Volt fill has to be, as a contrast");
        body.AppendLine("//                ratio against Volt. A ratio, so changing volt does not mean");
        body.AppendLine("//                re-picking the label colour that goes on it.");
        body.AppendLine("//   faintValue   the same pair for the quietest ink that is still ink.");
        body.AppendLine("//   faintChroma");
        body.AppendLine("//");
        body.AppendLine("//   chromeInk    how much darker the chrome's ink goes as the window opens up,");
        body.AppendLine("//   groundInk    and the art field's. Alpha coming off LIGHTENS a surface, so");
        body.AppendLine("//   dimLift      the inks pay for it - darker chrome, brighter metadata. These");
        body.AppendLine("//   faintLift    four are that compensation. Leave them alone unless a reading");
        body.AppendLine("//                is wrong; the Appearance screen measures the result live.");
        body.AppendLine("//");
        body.AppendLine("// DEFAULTS is what this theme asks the rest of the Appearance screen to be set");
        body.AppendLine("// to when it is picked. Optional, applied once, and yours to change afterwards.");
        body.AppendLine("//   transparency  0-100.  backdrop  \"acrylic\" | \"mica\"");
        body.AppendLine("//   reach  \"chrome\" | \"chrome-and-wall\"    layout  \"flush\" | \"floating\"");
        body.AppendLine("//");
        body.AppendLine("// OVERRIDES are derived colours you would rather state outright. The sixteen");
        body.AppendLine("// names are case-sensitive:");
        body.AppendLine("//");

        foreach (var chunk in ThemeDerivation.DerivedFields.Chunk(4))
        {
            body.AppendLine("//   " + string.Join(", ", chunk));
        }

        body.AppendLine("//");
        body.AppendLine("// The ones below are the values Winnow's own default theme was hand-tuned to");
        body.AppendLine("// against six hundred real capsules - the places where a person overruled the");
        body.AppendLine("// derivation. Delete any of them to see what the seeds produce on their own.");
        body.AppendLine("//");
        body.AppendLine("// schemaVersion is required and is checked before anything else is read. A file");
        body.AppendLine("// written to a version this build does not know is refused rather than read as");
        body.AppendLine("// best it can be, because a field that moved would load at its default and give");
        body.AppendLine("// you a theme you did not write.");
        body.AppendLine();
        body.Append(ThemeJson.Export(WinnowThemes.Default));

        return body.ToString();
    }
}
