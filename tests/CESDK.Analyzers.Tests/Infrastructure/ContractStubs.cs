using Microsoft.CodeAnalysis.Testing;

namespace CESDK.Analyzers.Tests.Infrastructure;

/// <summary>
///     Source stubs of the cross-project contract the rules are driven by: the plugin attribute of
///     <c>CESDK.Annotations</c> and the bootstrap types of <c>CESDK.Hosting</c>.
/// </summary>
/// <remarks>
///     The stubs are compiled into a separate project that the test project references, exactly like a plugin
///     references the SDK assemblies. Putting them in the test project itself would make every plugin test declare a
///     <c>CESDK.*</c> namespace, which is what CESDK0004 reports.
/// </remarks>
internal static class ContractStubs
{
    /// <summary>Name (and assembly name) of the referenced stub project.</summary>
    public const string ProjectName = "CESDK.ContractStubs";

    /// <summary>
    ///     The two stub sources concatenated, for a test that builds a raw <c>CSharpCompilation</c> in one file instead
    ///     of a <c>SolutionState</c> (<see cref="Plugin.PluginShapeParityTests" />).
    /// </summary>
    internal const string Combined = Annotations + "\n" + Hosting;

    private const string Annotations = """
                                       namespace CESDK.Annotations.Plugin
                                       {
                                           [System.AttributeUsage(System.AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
                                           public sealed class CheatEnginePluginAttribute : System.Attribute
                                           {
                                               public CheatEnginePluginAttribute(string name) => Name = name;

                                               public string Name { get; }
                                           }
                                       }
                                       """;

    private const string Hosting = """
                                   namespace CESDK.Hosting.Plugin
                                   {
                                       public abstract class CheatEnginePlugin
                                       {
                                           protected internal abstract void OnEnable();

                                           protected internal abstract void OnDisable();
                                       }

                                       public interface IPluginFactory
                                       {
                                           static abstract CheatEnginePlugin Create();

                                           static abstract System.ReadOnlySpan<byte> Utf8Name { get; }
                                       }
                                   }

                                   namespace CESDK.Hosting.Bootstrap
                                   {
                                       public static class PluginHost
                                       {
                                           public static int InitializeManaged<TFactory>(nint initRecord, int size)
                                               where TFactory : CESDK.Hosting.Plugin.IPluginFactory => 1;
                                       }
                                   }
                                   """;

    /// <summary>Adds the stub project to <paramref name="state" /> and references it from the test project.</summary>
    public static void AddTo(SolutionState state)
    {
        var contracts = state.AdditionalProjects[ProjectName];
        contracts.AdditionalReferences.AddRange(LocalFrameworkReferences.References);
        contracts.Sources.Add(("Annotations.cs", TestText.Normalize(Annotations)));
        contracts.Sources.Add(("Hosting.cs", TestText.Normalize(Hosting)));
        state.AdditionalProjectReferences.Add(ProjectName);
    }
}
