using Winnow.Update;

try
{
    if (args.Length == 0) throw new ArgumentException("Commands: stage, apply, recover, resume.");
    string Required(string name) => Value(name) ?? throw new ArgumentException("Missing " + name);
    string? Value(string name) { var index = Array.IndexOf(args, name); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null; }
    switch (args[0])
    {
        case "stage":
            Console.WriteLine(PortableUpdateEngine.Stage(Required("--archive"), Required("--sha256"), Required("--version"), Required("--runtime"), Required("--installation"), Required("--data-dir"), Required("--executable"), args.Where(a => a is "--fullscreen" or "--no-sync").ToArray()));
            break;
        case "apply":
            await PortableUpdateEngine.ApplyAsync(Required("--journal"), Value("--pid") is { } pid ? int.Parse(pid) : null, Value("--start-ticks") is { } ticks ? long.Parse(ticks) : null, expectedTransactionId: Value("--transaction"));
            break;
        case "recover":
            PortableUpdateEngine.Recover(Required("--journal"), args.Contains("--restore-backup"));
            break;
        case "resume":
            PortableUpdateEngine.Resume(Required("--journal"));
            break;
        default: throw new ArgumentException("Unknown command.");
    }
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}
