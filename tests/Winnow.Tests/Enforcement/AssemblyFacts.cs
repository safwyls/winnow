using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Winnow.Tests.Enforcement;

/// <summary>
/// Reads the built assemblies through <see cref="MetadataReader"/> so a
/// boundary can be asserted on what the compiler actually emitted rather than
/// on what the source appears to say.
///
/// <para>Metadata rather than a source scan because a source scan is defeated
/// by a using alias, an extension method or a generic, and metadata rather than
/// an IL-rewriting library because everything asserted here is a reference,
/// which lives in the tables the BCL already exposes. No package is added.</para>
/// </summary>
internal static class AssemblyFacts
{
    /// <summary>The directory the test assembly and every project's output sit in.</summary>
    private static string OutputDirectory =>
        Path.GetDirectoryName(typeof(AssemblyFacts).Assembly.Location)
        ?? throw new InvalidOperationException("The test assembly has no location on disk.");

    /// <summary>Every Winnow assembly in the build output, by simple name.</summary>
    internal static IReadOnlyList<string> WinnowAssemblies { get; } =
        [.. Directory
            .EnumerateFiles(OutputDirectory, "Winnow*.dll")
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .Where(n => !n.EndsWith(".Tests", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)];

    /// <summary>The path of one assembly in the build output.</summary>
    internal static string PathOf(string simpleName)
        => Path.Combine(OutputDirectory, simpleName + ".dll");

    /// <summary>
    /// The assemblies <paramref name="simpleName"/> references, by simple name.
    /// </summary>
    internal static IReadOnlyList<string> ReferencedAssemblies(string simpleName)
    {
        using var reader = Open(simpleName, out var metadata);

        return [.. metadata.AssemblyReferences
            .Select(h => metadata.GetString(metadata.GetAssemblyReference(h).Name))
            .OrderBy(n => n, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Every type this assembly declares, as <c>Namespace.Name</c>.
    /// </summary>
    internal static IReadOnlyList<string> DeclaredTypes(string simpleName)
    {
        using var reader = Open(simpleName, out var metadata);

        return [.. metadata.TypeDefinitions
            .Select(metadata.GetTypeDefinition)
            .Select(t => Join(metadata.GetString(t.Namespace), metadata.GetString(t.Name)))];
    }

    /// <summary>
    /// Every external member this assembly calls or touches, as
    /// <c>Namespace.Type::Member</c>. This is what a "does it reach X" question
    /// resolves to.
    /// </summary>
    internal static IReadOnlyList<string> ReferencedMembers(string simpleName)
    {
        using var reader = Open(simpleName, out var metadata);
        var found = new List<string>();

        foreach (var handle in metadata.MemberReferences)
        {
            var member = metadata.GetMemberReference(handle);
            var owner = DescribeParent(metadata, member.Parent);
            if (owner is null)
            {
                continue;
            }

            found.Add($"{owner}::{metadata.GetString(member.Name)}");
        }

        return found;
    }

    /// <summary>
    /// Every external type this assembly references, as
    /// <c>Namespace.Name</c>.
    /// </summary>
    internal static IReadOnlyList<string> ReferencedTypes(string simpleName)
    {
        using var reader = Open(simpleName, out var metadata);

        return [.. metadata.TypeReferences
            .Select(metadata.GetTypeReference)
            .Select(t => Join(metadata.GetString(t.Namespace), metadata.GetString(t.Name)))];
    }

    /// <summary>
    /// The declaring type of a member reference, when it has one that can be
    /// named. A reference through a type specification — a constructed generic
    /// — has no simple name and is skipped.
    /// </summary>
    private static string? DescribeParent(MetadataReader metadata, EntityHandle parent)
    {
        switch (parent.Kind)
        {
            case HandleKind.TypeReference:
            {
                var type = metadata.GetTypeReference((TypeReferenceHandle)parent);
                return Join(metadata.GetString(type.Namespace), metadata.GetString(type.Name));
            }

            case HandleKind.TypeDefinition:
            {
                var type = metadata.GetTypeDefinition((TypeDefinitionHandle)parent);
                return Join(metadata.GetString(type.Namespace), metadata.GetString(type.Name));
            }

            default:
                return null;
        }
    }

    private static string Join(string ns, string name)
        => string.IsNullOrEmpty(ns) ? name : ns + "." + name;

    private static PEReader Open(string simpleName, out MetadataReader metadata)
    {
        var path = PathOf(simpleName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"{simpleName}.dll is not in the build output at {OutputDirectory}. "
                + "The boundary tests read the built assemblies.", path);
        }

        var reader = new PEReader(File.OpenRead(path));
        metadata = reader.GetMetadataReader();
        return reader;
    }
}
