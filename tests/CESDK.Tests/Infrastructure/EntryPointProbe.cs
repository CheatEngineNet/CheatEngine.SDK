using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace CESDK.Tests.Infrastructure;

/// <summary>
///     Looks for the host-mandated <c>CESDK.CESDK.CEPluginInitialize</c> entry point in a built consumer assembly by
///     reading its metadata directly with <see cref="System.Reflection.Metadata" /> - no <c>Assembly.Load</c>, so the
///     file is never locked and a plugin assembly whose own dependencies are not on this test's probing path (they are
///     on the consumer's own restored path, not this one) still reads cleanly.
/// </summary>
internal static class EntryPointProbe
{
    private const string TypeNamespace = "CESDK";
    private const string TypeName = "CESDK";
    private const string MethodName = "CEPluginInitialize";

    /// <summary>Whether the <c>CESDK.CESDK</c> type exists, and whether it declares a two-parameter <c>CEPluginInitialize</c>.</summary>
    public static (bool TypeExists, bool MethodExists) Probe(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using PEReader peReader = new(stream);
        var reader = peReader.GetMetadataReader();

        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            if (!string.Equals(reader.GetString(type.Namespace), TypeNamespace, StringComparison.Ordinal)
                || !string.Equals(reader.GetString(type.Name), TypeName, StringComparison.Ordinal))
                continue;

            foreach (var methodHandle in type.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (string.Equals(reader.GetString(method.Name), MethodName, StringComparison.Ordinal) &&
                    method.GetParameters().Count == 2) return (TypeExists: true, MethodExists: true);
            }

            return (TypeExists: true, MethodExists: false);
        }

        return (TypeExists: false, MethodExists: false);
    }
}
