using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace Winnow.Plugin.Xbox;

public sealed record XboxLocalPackage(XboxLocalGame Game, bool IsGame);

/// <summary>Reads caller-owned snapshots. XML cannot resolve external resources or expand DTDs.</summary>
public static class XboxLocalPackageParser
{
    public const int MaximumFileBytes = 2 * 1024 * 1024;

    public static XboxLocalPackage? Parse(string familyName, string installPath, byte[] appxManifest,
        byte[]? gameConfig = null, byte[]? xboxServicesConfig = null, Func<string, string?>? resolveResource = null)
    {
        if (!IsPackageFamilyName(familyName) || !Path.IsPathFullyQualified(installPath))
            throw new FormatException("Invalid package identity or install path.");

        var manifest = ReadXml(appxManifest, "Package");
        var identity = Single(manifest, "Identity") ?? throw new FormatException("Missing package identity.");
        if (!familyName.StartsWith(Attribute(identity, "Name") + "_", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Package identity does not match its registration.");

        var applications = Single(manifest, "Applications")?.Elements().Where(e => e.Name.LocalName == "Application")
            .Where(e => !string.Equals(Attribute(Single(e, "VisualElements"), "AppListEntry"), "none", StringComparison.OrdinalIgnoreCase))
            .ToList() ?? [];
        var packageTitle = Single(Single(manifest, "Properties"), "DisplayName")?.Value;
        string? gameTitle = null;
        string? titleId = null;
        string? storeId = null;
        var isGame = false;
        if (gameConfig is not null)
        {
            var config = ReadXml(gameConfig, "Game");
            var configIdentity = Single(config, "Identity");
            if (Attribute(configIdentity, "Name") != Attribute(identity, "Name"))
                throw new FormatException("Game identity does not match its package.");
            // DLC and development executables do not establish a playable PC installation.
            if (Single(config, "TargetDeviceFamilyForDLC") is not null || Single(config, "AllowedProducts") is not null)
                return null;
            var executables = Single(config, "ExecutableList")?.Elements()
                .Where(e => e.Name.LocalName == "Executable")
                .Where(e => !string.Equals(Attribute(e, "IsDevOnly"), "true", StringComparison.OrdinalIgnoreCase))
                .Where(e => Attribute(e, "TargetDeviceFamily") is null or "PC")
                .ToList() ?? [];
            foreach (var executable in executables)
                _ = ResolveExecutable(installPath, Attribute(executable, "Name"));
            if (executables.Count == 0) return null;
            applications = applications.Where(app => executables.Any(exe => Attribute(exe, "Id") is { } id
                ? id == Attribute(app, "Id")
                : string.Equals(Attribute(exe, "Name"), Attribute(app, "Executable"), StringComparison.OrdinalIgnoreCase))).ToList();
            isGame = true;
            gameTitle = Attribute(Single(config, "ShellVisuals"), "DefaultDisplayName");
            var rawTitleId = Single(config, "TitleId")?.Value.Trim();
            if (rawTitleId is not null)
            {
                if (rawTitleId.Length != 8 || !uint.TryParse(rawTitleId, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var numeric)
                    || numeric is 0 or uint.MaxValue)
                    throw new FormatException("Invalid Xbox title identity.");
                titleId = numeric.ToString(CultureInfo.InvariantCulture);
            }
            storeId = Single(config, "StoreId")?.Value.Trim();
            if (storeId is not null && !IsStoreId(storeId)) throw new FormatException("Invalid Store identity.");
            storeId = storeId?.ToUpperInvariant();
        }

        if (xboxServicesConfig is not null)
        {
            CheckLength(xboxServicesConfig);
            using var services = JsonDocument.Parse(xboxServicesConfig, new JsonDocumentOptions { MaxDepth = 16 });
            if (services.RootElement.ValueKind != JsonValueKind.Object
                || services.RootElement.EnumerateObject().Count(p => p.Name == "TitleId") != 1
                || !services.RootElement.TryGetProperty("TitleId", out var id)
                || !TryDecimalTitleId(id, out var numeric))
                throw new FormatException("Invalid Xbox service identity.");
            var serviceTitleId = numeric.ToString(CultureInfo.InvariantCulture);
            if (titleId is not null && titleId != serviceTitleId) throw new FormatException("Conflicting Xbox title identities.");
            titleId = serviceTitleId;
            isGame = true;
        }

        if (applications.Count == 0 && !isGame) return null;
        string? aumid = null;
        string? executablePath = null;
        string? applicationTitle = null;
        foreach (var app in applications)
        {
            if (!IsApplicationId(Attribute(app, "Id"))) throw new FormatException("Invalid application identity.");
            _ = ResolveExecutable(installPath, Attribute(app, "Executable"));
        }
        // Multiple visible applications have no safe implicit default.
        if (applications.Count == 1)
        {
            aumid = familyName + "!" + Attribute(applications[0], "Id");
            executablePath = ResolveExecutable(installPath, Attribute(applications[0], "Executable"));
            applicationTitle = Attribute(Single(applications[0], "VisualElements"), "DisplayName");
        }
        var title = ResolveTitle(applicationTitle, resolveResource)
            ?? ResolveTitle(gameTitle, resolveResource)
            ?? ResolveTitle(packageTitle, resolveResource);
        var provisional = title is null;
        title ??= Attribute(identity, "Name");

        return new XboxLocalPackage(new XboxLocalGame(familyName, title!)
        {
            InstallPath = Path.GetFullPath(installPath), StoreId = storeId, TitleId = titleId,
            AppUserModelId = aumid, ExecutablePath = executablePath, IsTitleProvisional = provisional
        }, isGame);
    }

    private static string? ResolveTitle(string? title, Func<string, string?>? resolveResource)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        var value = title.Trim();
        if (value.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)) value = resolveResource?.Invoke(value)?.Trim();
        return value is { Length: > 0 and <= 256 } && !value.Any(char.IsControl)
            && !value.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase) ? value : null;
    }

    public static bool IsPackageFamilyName(string? value)
    {
        if (value is not { Length: > 14 and <= 255 }) return false;
        var separator = value.LastIndexOf('_');
        return separator > 0 && value.Length - separator - 1 == 13
            && value[..separator].All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-')
            && value[(separator + 1)..].All(char.IsAsciiLetterOrDigit);
    }

    public static bool IsApplicationId(string? value)
        => value is { Length: > 0 and <= 64 } && char.IsAsciiLetter(value[0])
            && value.Split('.').All(part => part.Length > 0 && char.IsAsciiLetter(part[0]) && part.All(char.IsAsciiLetterOrDigit));

    public static bool IsStoreId(string? value)
        => value is { Length: 12 } && value.All(char.IsAsciiLetterOrDigit);

    private static string? ResolveExecutable(string root, string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative)) return null;
        if (relative.Any(c => char.IsControl(c) || c is ':' or '"' or '<' or '>' or '|' or '?' or '*')
            || relative.StartsWith('/') || relative.StartsWith('\\')
            || relative.Split(['/', '\\']).Any(part => part is ".." or "." or ""))
            throw new FormatException("Invalid package executable path.");
        var path = Path.GetFullPath(Path.Combine(root, relative.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar)));
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Invalid package executable path.");
        return path;
    }

    private static bool TryDecimalTitleId(JsonElement value, out uint id)
    {
        id = 0;
        var valid = value.ValueKind == JsonValueKind.Number ? value.TryGetUInt32(out id)
            : value.ValueKind == JsonValueKind.String && uint.TryParse(value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out id);
        return valid && id is > 0 and < uint.MaxValue;
    }

    private static XElement ReadXml(byte[] bytes, string root)
    {
        CheckLength(bytes);
        using var stream = new MemoryStream(bytes, false);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = MaximumFileBytes, IgnoreComments = true
        });
        while (reader.Read())
            if (reader.Depth > 64) throw new FormatException("Package XML is too deeply nested.");
        stream.Position = 0;
        using var documentReader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumFileBytes
        });
        var document = XDocument.Load(documentReader);
        if (document.Root?.Name.LocalName != root) throw new FormatException("Unexpected package document.");
        return document.Root;
    }

    private static void CheckLength(byte[] bytes)
    {
        if (bytes.Length > MaximumFileBytes) throw new FormatException("Package document is too large.");
    }

    private static XElement? Single(XElement? parent, string name)
    {
        var elements = parent?.Elements().Where(e => e.Name.LocalName == name).Take(2).ToArray() ?? [];
        if (elements.Length > 1) throw new FormatException("Duplicate package field.");
        return elements.FirstOrDefault();
    }

    private static string? Attribute(XElement? element, string name) => element?.Attribute(name)?.Value;
}
