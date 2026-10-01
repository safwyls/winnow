using Winnow.Backend;
using Winnow.App.Services;
using System.Text.Json;

try
{
    if (args.Contains(Winnow.Activation.FrontendActivationHost.Argument))
        return await Winnow.Activation.FrontendActivationHost.RunAsync(
            WinnowDataLocation.ResolveFrom(args).Root,
            Winnow.Activation.FrontendActivationHost.ParentProcessIdFrom(args));
    if (args.Contains("--resolve-data-location"))
    {
        var location = WinnowDataLocation.ResolveFrom(args);
        Console.WriteLine(JsonSerializer.Serialize(location, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return 0;
    }
    await using var app = BackendApplication.Build(args);
    await app.RunAsync();
    return 0;
}
catch (DataDirectoryOverrideException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Winnow backend failed to start: {exception.Message}");
    return 3;
}
