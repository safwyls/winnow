using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Winnow.App.Services;

namespace Winnow.Activation;

/// <summary>
/// Hosts only the current-user frontend mutex and activation pipe. Standard input and the
/// parent's process handle bind its lifetime; no database, API host or worker is constructed.
/// </summary>
public static class FrontendActivationHost
{
    public const string Argument = "--frontend-activation-helper";
    internal const int MaximumFrameBytes = 2048;
    internal const int MaximumResponseBytes = 128 * 1024;
    private const string Frontend = "Electron";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static int ParentProcessIdFrom(string[] args)
    {
        var indices = args.Select((value, index) => (value, index)).Where(x => x.value == "--parent-pid").ToArray();
        if (indices.Length != 1 || indices[0].index + 1 >= args.Length ||
            !int.TryParse(args[indices[0].index + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
            throw new ArgumentException("The activation helper requires its parent process ID.");
        return id;
    }

    public static async Task<int> RunAsync(string directory, int parentProcessId)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The activation helper is Windows-only.");
        if (parentProcessId != WindowsParentProcess.CurrentParentId())
            throw new ArgumentException("The activation helper parent does not match its launching process.");
        using var parent = Process.GetProcessById(parentProcessId);
        // Open the process handle before awaiting input; PID reuse cannot transfer ownership.
        _ = parent.SafeHandle;
        if (parent.HasExited) return 0;
        using var lifetime = new CancellationTokenSource();
        try
        {
            using var input = Console.OpenStandardInput();
            using var output = Console.OpenStandardOutput();
            var outputLock = new object();
            var outputFailure = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Publish(object frame)
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(frame, Json);
                if (bytes.Length > MaximumResponseBytes) throw new InvalidDataException("Activation response is too large.");
                try
                {
                    lock (outputLock)
                    {
                        output.Write(bytes);
                        output.WriteByte((byte)'\n');
                        output.Flush();
                    }
                }
                catch (IOException error) { outputFailure.TrySetException(error); throw; }
            }

            var parentEnded = parent.WaitForExitAsync(lifetime.Token);
            using var handshake = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            handshake.CancelAfter(TimeSpan.FromSeconds(5));
            var read = ReadFrameAsync(input, handshake.Token);
            if (await Task.WhenAny(parentEnded, read) == parentEnded) return 0;
            var request = DecodeRequest(await read ?? throw new InvalidDataException("Missing activation request."));
            using var guard = SingleInstanceGuard.TryAcquire(directory, Frontend);
            if (guard is null)
            {
                var accepted = await SingleInstanceActivation.RequestAsync(directory, request, frontend: Frontend);
                Publish(new { kind = "forwarded", accepted });
                return 0;
            }

            using var activation = new SingleInstanceActivation(directory, Frontend, parentProcessId);
            Publish(new
            {
                kind = "primary", root = Path.GetFullPath(directory), processId = Environment.ProcessId,
                parentProcessId, mutexName = SingleInstanceGuard.NameFor(directory, Frontend),
                pipeName = SingleInstanceGuard.ActivationNameFor(directory, Frontend)
            });
            activation.SetHandler(action => Publish(new { kind = "activation", activation = EncodeRequest(action) }));
            {
                // No command stream follows the handshake. EOF means the owning frontend closed.
                var inputEnded = ReadFrameAsync(input, lifetime.Token);
                var finished = await Task.WhenAny(parentEnded, inputEnded, activation.Completion, outputFailure.Task);
                if (finished == inputEnded && await inputEnded is not null)
                    throw new InvalidDataException("Unexpected activation helper input.");
                if (finished == activation.Completion)
                {
                    await activation.Completion;
                    throw new IOException("The activation listener stopped unexpectedly.");
                }
                if (finished == outputFailure.Task) await outputFailure.Task;
                return 0;
            }
        }
        finally { lifetime.Cancel(); }
    }

    internal static async Task<byte[]?> ReadFrameAsync(Stream stream, CancellationToken ct)
    {
        using var bytes = new MemoryStream();
        var current = new byte[1];
        while (await stream.ReadAsync(current, ct) != 0)
        {
            if (current[0] == (byte)'\n') return bytes.ToArray();
            if (bytes.Length >= MaximumFrameBytes) throw new InvalidDataException("Activation request is too large.");
            bytes.WriteByte(current[0]);
        }
        if (bytes.Length != 0) throw new InvalidDataException("Incomplete activation request.");
        return null;
    }

    internal static AppActivationRequest DecodeRequest(byte[] bytes)
    {
        using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
        var root = json.RootElement;
        RequireProperties(root, "version", "activation");
        if (root.GetProperty("version").GetInt32() != 1) throw new InvalidDataException("Unsupported activation protocol.");
        var action = root.GetProperty("activation");
        var kind = ReadString(action, "kind");
        switch (kind)
        {
            case "show": RequireProperties(action, "kind"); return AppActivationRequest.Activate;
            case "fullscreen": RequireProperties(action, "kind"); return AppActivationRequest.Fullscreen;
            case "game":
                RequireProperties(action, "kind", "ownershipId");
                var value = ReadString(action, "ownershipId");
                if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0 ||
                    value != id.ToString(CultureInfo.InvariantCulture))
                    throw new InvalidDataException("Invalid activation ownership ID.");
                return AppActivationRequest.ForGame(id);
            case "plugin":
                RequireProperties(action, "kind", "pluginId", "releaseTag");
                if (PluginInstallRequest.TryParseUri($"winnow://plugins/install?id={ReadString(action, "pluginId")}&release={ReadString(action, "releaseTag")}", out var plugin) && plugin is not null)
                    return AppActivationRequest.ForPlugin(plugin);
                break;
        }
        throw new InvalidDataException("Invalid activation request.");
    }

    private static string ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Invalid activation field type.");
        return value.GetString()!;
    }

    private static void RequireProperties(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid activation object.");
        var actual = element.EnumerateObject().Select(property => property.Name).ToArray();
        if (actual.Length != names.Length || actual.Distinct().Count() != names.Length || actual.Except(names).Any())
            throw new InvalidDataException("Invalid activation fields.");
    }

    internal static object EncodeRequest(AppActivationRequest request) => request.Kind switch
    {
        AppActivationKind.Activate => new { kind = "show" },
        AppActivationKind.Fullscreen => new { kind = "fullscreen" },
        AppActivationKind.LaunchGame => new { kind = "game", ownershipId = request.OwnershipId.ToString(CultureInfo.InvariantCulture) },
        AppActivationKind.InstallPlugin => new { kind = "plugin", pluginId = request.Plugin!.PluginId, releaseTag = request.Plugin.ReleaseTag },
        _ => throw new InvalidDataException("Invalid activation kind.")
    };
}
