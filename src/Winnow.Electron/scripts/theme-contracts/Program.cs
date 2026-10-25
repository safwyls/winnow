using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: ThemeContracts <path-to-Winnow.dll> <output.json>");
    return 2;
}

var assemblyPath = Path.GetFullPath(args[0]);
var assemblyDirectory = Path.GetDirectoryName(assemblyPath)!;
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    var dependency = Path.Combine(assemblyDirectory, name.Name + ".dll");
    return File.Exists(dependency)
        ? AssemblyLoadContext.Default.LoadFromAssemblyPath(dependency)
        : null;
};
var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
Type ThemeType(string name) => assembly.GetType("Winnow.App.Themes." + name, throwOnError: true)!;
var themes = (IEnumerable)ThemeType("WinnowThemes").GetField("All")!.GetValue(null)!;
var layouts = (IEnumerable)ThemeType("WinnowLayouts").GetProperty("All")!.GetValue(null)!;
var export = ThemeType("ThemeJson").GetMethod("Export")!;
var parse = ThemeType("ThemeJson").GetMethod("Parse")!;
var tokens = ThemeType("WinnowTheme").GetMethod("Tokens")!;
var documents = new List<object>();
var states = 0;
var comparisons = 0;
foreach (var theme in themes)
{
    var id = (string)theme.GetType().GetProperty("Id")!.GetValue(theme)!;
    var themeJson = (string)export.Invoke(null, [theme])!;
    var parsed = parse.Invoke(null, [id + ".json", themeJson])!;
    var loaded = parsed.GetType().GetField("Item1")!.GetValue(parsed)
        ?? throw new InvalidOperationException("Theme round trip failed: " + id);
    var matrix = new List<object>();
    foreach (var wall in new[] { false, true })
    foreach (var layout in layouts)
    for (var percent = 0; percent <= 100; percent += 5)
    {
        var expected = Colors(tokens.Invoke(theme, [percent / 100d, wall, layout])!);
        var actual = Colors(tokens.Invoke(loaded, [percent / 100d, wall, layout])!);
        if (expected.Count != actual.Count || expected.Any(pair => actual[pair.Key] != pair.Value))
            throw new InvalidOperationException($"Token round trip failed: {id}, {wall}, {layout}, {percent}");
        matrix.Add(new { wall, layout = layout.ToString()!.ToLowerInvariant(), percent, tokens = expected });
        states++;
        comparisons += expected.Count;
    }
    documents.Add(new { id, theme = JsonSerializer.Deserialize<JsonElement>(themeJson), matrix });
}

var output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, JsonSerializer.Serialize(new
{
    sourceRevision = "cf45d9f1127243a987d3cf6e664a32fc767ecb67",
    sourceMethod = "ThemeJsonTests.Every_token_matches_at_every_slider_position",
    colorEncoding = "#RRGGBBAA",
    themes = documents,
}, new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine($"Exported {documents.Count} themes, {states} states, {comparisons} exact color comparisons to {output}.");
return 0;

static SortedDictionary<string, string> Colors(object dictionary)
{
    var output = new SortedDictionary<string, string>(StringComparer.Ordinal);
    foreach (var pair in (IEnumerable)dictionary)
    {
        var type = pair.GetType();
        var name = (string)type.GetProperty("Key")!.GetValue(pair)!;
        var color = type.GetProperty("Value")!.GetValue(pair)!;
        byte Channel(string channel) => (byte)color.GetType().GetProperty(channel)!.GetValue(color)!;
        output.Add(name, $"#{Channel("R"):X2}{Channel("G"):X2}{Channel("B"):X2}{Channel("A"):X2}");
    }
    return output;
}
