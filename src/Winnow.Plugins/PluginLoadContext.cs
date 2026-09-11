using System.Reflection;
using System.Runtime.Loader;
using Winnow.PluginSdk;

namespace Winnow.Plugins;

internal sealed class PluginLoadContext(string entryPath) : AssemblyLoadContext(isCollectible: true)
{
    private readonly AssemblyDependencyResolver _resolver = new(entryPath);
    private readonly string _directory = Path.GetDirectoryName(Path.GetFullPath(entryPath))! + Path.DirectorySeparatorChar;

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var sdk = typeof(IPlugin).Assembly;
        if (assemblyName.Name == sdk.GetName().Name)
        {
            if (assemblyName.Version?.Major != sdk.GetName().Version?.Major)
                throw new FileLoadException("The plugin SDK assembly version is incompatible.");
            return sdk;
        }
        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(RequirePackagePath(path));
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? 0 : LoadUnmanagedDllFromPath(RequirePackagePath(path));
    }

    private string RequirePackagePath(string path)
    {
        var absolute = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!absolute.StartsWith(_directory, comparison))
            throw new FileLoadException("A plugin dependency resolves outside its package directory.");
        return absolute;
    }
}
