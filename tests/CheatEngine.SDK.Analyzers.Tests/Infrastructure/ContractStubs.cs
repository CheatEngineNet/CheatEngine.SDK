using Microsoft.CodeAnalysis.Testing;

namespace CheatEngine.SDK.Analyzers.Tests.Infrastructure;

/// <summary>
///     Source stubs of the cross-project contract the rules are driven by: the plugin attribute of
///     <c>CheatEngine.SDK.Annotations</c> and the bootstrap types of <c>CheatEngine.SDK.Hosting</c>.
/// </summary>
/// <remarks>
///     The stubs are compiled into a separate project that the test project references, exactly like a plugin
///     references the SDK assemblies. The compilation under test then holds plugin code only, and the contract types reach
///     the rules as metadata, as they do in a real plugin, never as declarations of the plugin assembly. The stubs live
///     under <c>CheatEngine.SDK</c>, which CESDK0004 does not reserve: it reserves only <c>CESDK</c>, the namespace of the
///     <c>CESDK.CESDK</c> type that Cheat Engine requires.
/// </remarks>
internal static class ContractStubs
{
    /// <summary>Name (and assembly name) of the referenced stub project.</summary>
    public const string ProjectName = "CheatEngine.SDK.ContractStubs";

    /// <summary>
    ///     The two stub sources concatenated, for a test that builds a raw <c>CSharpCompilation</c> in one file instead
    ///     of a <c>SolutionState</c> (<see cref="Plugin.PluginShapeParityTests" />).
    /// </summary>
    internal const string Combined = Annotations + "\n" + Hosting;

    private const string Annotations = """
                                       namespace CheatEngine.SDK.Annotations.Plugin
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
                                   namespace CheatEngine.SDK.Hosting.Plugin
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

                                   namespace CheatEngine.SDK.Hosting.Bootstrap
                                   {
                                       public static class PluginHost
                                       {
                                           public static int InitializeManaged<TFactory>(nint initRecord, int size)
                                               where TFactory : CheatEngine.SDK.Hosting.Plugin.IPluginFactory => 1;
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
