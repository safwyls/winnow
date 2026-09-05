using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Winnow.App.Services;

/// <summary>
/// Where the proposed title came from, ranked by confidence. Version-info
/// fields (<see cref="FileDescription"/>, <see cref="ProductName"/>) are
/// strong; path-derived guesses (<see cref="FolderName"/>,
/// <see cref="FileName"/>) are weak; <see cref="None"/> means the file said
/// nothing usable and the title has to be typed.
/// </summary>
public enum ExecutableTitleSource
{
    /// <summary>Nothing on the file was usable. The title must be typed.</summary>
    None,

    /// <summary>The Win32 FileDescription version-info field.</summary>
    FileDescription,

    /// <summary>The Win32 ProductName version-info field.</summary>
    ProductName,

    /// <summary>A folder name on the path to the executable.</summary>
    FolderName,

    /// <summary>The executable's own file name, without the extension.</summary>
    FileName,
}

/// <summary>
/// What can be learned from an executable's path and version info without
/// running it. Immutable. Produced by <see cref="Derive"/> (pure, path only)
/// or by <see cref="IExecutableInspector"/> (reads the file's resource section).
/// </summary>
public sealed record ExecutableFacts
{
    /// <summary>Full path to the executable the user chose.</summary>
    public required string ExecutablePath { get; init; }

    /// <summary>Parent directory of the executable, or null when the path has none.</summary>
    public string? InstallPath { get; init; }

    /// <summary>Best-effort title derived from the file, or null when nothing was usable.</summary>
    public string? Title { get; init; }

    /// <summary>Where <see cref="Title"/> came from. Determines how much confidence the copy conveys.</summary>
    public ExecutableTitleSource TitleSource { get; init; }

    /// <summary>Company name from version info, or null. Stated as corroboration in the UI.</summary>
    public string? Publisher { get; init; }

    /// <summary>The file name portion of <see cref="ExecutablePath"/>.</summary>
    public string FileName => Path.GetFileName(ExecutablePath);

    /// <summary>True when <see cref="Title"/> is present and non-blank.</summary>
    public bool HasTitle => !string.IsNullOrWhiteSpace(Title);

    /// <summary>
    /// Derives facts from a path and optional version-info strings. Pure: uses
    /// only <see cref="Path"/> string parsing and never touches the file system.
    /// </summary>
    public static ExecutableFacts Derive(
        string executablePath,
        string? fileDescription = null,
        string? productName = null,
        string? companyName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        var path = executablePath.Trim();
        var directory = DirectoryOf(path);

        var (title, source) = FirstUsableTitle(path, directory, fileDescription, productName);

        return new ExecutableFacts
        {
            ExecutablePath = path,
            InstallPath = directory,
            Title = title,
            TitleSource = source,
            Publisher = Usable(Clean(companyName)),
        };
    }

    private static (string? Title, ExecutableTitleSource Source) FirstUsableTitle(
        string path, string? directory, string? fileDescription, string? productName)
    {
        if (Usable(Clean(fileDescription)) is { } fromDescription)
        {
            return (fromDescription, ExecutableTitleSource.FileDescription);
        }

        if (Usable(Clean(productName)) is { } fromProduct)
        {
            return (fromProduct, ExecutableTitleSource.ProductName);
        }

        foreach (var folder in FolderNames(directory))
        {
            if (Usable(Clean(folder)) is { } fromFolder)
            {
                return (fromFolder, ExecutableTitleSource.FolderName);
            }
        }

        return Usable(Clean(Path.GetFileNameWithoutExtension(path))) is { } fromFileName
            ? (fromFileName, ExecutableTitleSource.FileName)
            : (null, ExecutableTitleSource.None);
    }

    private static string? DirectoryOf(string path)
    {
        string? directory;
        try
        {
            directory = Path.GetDirectoryName(path);
        }
        catch (ArgumentException)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(directory) ? null : directory;
    }

    // Walks up from the executable's own folder so a game buried under
    // Binaries\Win64 still reaches the folder that names it. Bounded at
    // MaxFolderDepth and stops at the drive root.
    private static IEnumerable<string> FolderNames(string? directory)
    {
        var current = directory;

        for (var depth = 0; depth < MaxFolderDepth && !string.IsNullOrWhiteSpace(current); depth++)
        {
            var trimmed = current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var root = Path.GetPathRoot(current)?
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (root is not null && string.Equals(trimmed, root, StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }

            var name = Path.GetFileName(trimmed);
            if (!string.IsNullOrWhiteSpace(name))
            {
                yield return name;
            }

            string? parent;
            try
            {
                parent = Path.GetDirectoryName(current);
            }
            catch (ArgumentException)
            {
                yield break;
            }

            if (string.Equals(parent, current, StringComparison.Ordinal))
            {
                yield break;
            }

            current = parent;
        }
    }

    // Cleaning, not validation: strips the extension, the build suffixes real
    // installers ship, and the underscores that stand in for spaces.
    private static string? Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var value = raw.Trim();

        if (value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^4];
        }

        bool stripped;
        do
        {
            stripped = false;
            foreach (var suffix in BuildSuffixes)
            {
                if (value.Length > suffix.Length
                    && value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    value = value[..^suffix.Length];
                    stripped = true;
                }
            }
        }
        while (stripped);

        value = Collapse(value.Replace('_', ' ')).Trim(' ', '-');

        return value.Length == 0 ? null : value;
    }

    private static string Collapse(string value)
    {
        var builder = new StringBuilder(value.Length);
        var space = false;

        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch))
            {
                space = builder.Length > 0;
                continue;
            }

            if (space)
            {
                builder.Append(' ');
                space = false;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    // Rejects values that would make a bad title: generic directory names,
    // engine names, launcher names and pure digits. The next candidate is
    // tried, and no candidate at all leaves the form to be typed by hand.
    private static string? Usable(string? cleaned)
    {
        if (cleaned is null)
        {
            return null;
        }

        var key = Normalise(cleaned);

        if (key.Length < 2
            || key.All(char.IsAsciiDigit)
            || Stubs.Contains(key)
            || key.StartsWith("unrealengine", StringComparison.Ordinal))
        {
            return null;
        }

        return cleaned;
    }

    private static string Normalise(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return builder.ToString();
    }

    private const int MaxFolderDepth = 5;

    private static readonly string[] BuildSuffixes =
    [
        "-Win64-Shipping", "-Win32-Shipping", "-WinGDK-Shipping", "-Shipping",
        "-Win64", "-Win32", "_Win64", "_Win32",
        "-x64", "-x86", "_x64", "_x86", " x64", " x86",
        " (64-bit)", " (32-bit)", " (64 bit)", " (32 bit)",
        " Launcher", "-Launcher", "_Launcher",
        "_Data",
    ];

    private static readonly HashSet<string> Stubs = new(StringComparer.Ordinal)
    {
        "app", "appdata", "application", "apps", "bin", "binaries", "binary",
        "build", "builds", "client", "common", "content", "data", "debug",
        "desktop", "dist", "documents", "downloads", "engine", "executable",
        "exe", "files", "game", "gamedata", "games", "install", "installer",
        "launch", "launcher", "local", "main", "newfolder", "output", "play",
        "program", "programfiles", "programfilesx86", "programs", "redist",
        "release", "roaming", "run", "setup", "shipping", "src", "start",
        "startup", "steamapps", "system32", "temp", "tmp", "uninstall",
        "uninstaller", "unins000", "users", "win", "win32", "win64", "windows",
        "x64", "x86",

        // Engines, runtimes and launchers that name themselves in version info
        // instead of naming the game.
        "defaultcompany", "dosbox", "electron", "emulator", "epicgames",
        "galaxy", "gamemaker", "godot", "gog", "java", "javaw", "node",
        "ue4", "ue5",
        "nodejs", "python", "pythonw", "retroarch", "rpgmaker", "scummvm",
        "steam", "unity", "unityplayer", "wine",
    };
}

/// <summary>
/// Reads an executable's version-info resource section and derives facts from
/// it. Read-only; never runs the file.
/// </summary>
public interface IExecutableInspector
{
    /// <summary>
    /// Inspects the file at <paramref name="executablePath"/> and returns what
    /// can be learned from it. Never executes the file.
    /// </summary>
    ExecutableFacts Inspect(string executablePath);
}

/// <summary>
/// <see cref="IExecutableInspector"/> backed by
/// <see cref="FileVersionInfo.GetVersionInfo"/>. Reads the PE resource section;
/// a file that is not a PE image answers with nulls rather than throwing.
/// </summary>
public sealed class FileVersionInfoExecutableInspector : IExecutableInspector
{
    private readonly ILogger<FileVersionInfoExecutableInspector> _log;

    public FileVersionInfoExecutableInspector(
        ILogger<FileVersionInfoExecutableInspector>? log = null)
        => _log = log ?? NullLogger<FileVersionInfoExecutableInspector>.Instance;

    /// <inheritdoc/>
    public ExecutableFacts Inspect(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        string? description = null;
        string? product = null;
        string? company = null;

        try
        {
            // Reads the resource section only. A non-PE file answers with
            // nulls; a missing or locked file throws, and the path alone is
            // still enough to derive from.
            var info = FileVersionInfo.GetVersionInfo(executablePath);

            description = info.FileDescription;
            product = info.ProductName;
            company = info.CompanyName;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            _log.LogDebug(
                ex,
                "No version info could be read from {Path}.",
                executablePath);
        }

        return ExecutableFacts.Derive(executablePath, description, product, company);
    }
}
