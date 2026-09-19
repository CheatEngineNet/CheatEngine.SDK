namespace CheatEngine.SDK.SourceGenerators.EntryPoint.Tests.Infrastructure;

/// <summary>
///     Source stubs of the cross-project contract the generator targets:
///     <c>CheatEngine.SDK.Annotations.Plugin.CheatEnginePluginAttribute</c> and the <c>CheatEngine.SDK.Hosting</c> bootstrap types. They are
///     compiled into their own assembly and referenced by the test compilations, the way a real plugin references the
///     SDK. The real projects are deliberately not referenced: this generator may depend on the contract only.
/// </summary>
/// <remarks>
///     <c>PluginHost</c> is instrumented instead of being empty, so that the execution tests can observe what the
///     generated bootstrap handed over: it records the factory's UTF-8 name and the plugin instance, and throws when
///     <c>size</c> is negative to prove the generated catch-all.
/// </remarks>
internal static class ContractStubs
{
    public const string AssemblyName = "CheatEngine.SDK.ContractStubs";

    public const string Source = """
                                 #nullable enable
                                 namespace CheatEngine.SDK.Annotations.Plugin
                                 {
                                     [global::System.AttributeUsage(global::System.AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
                                     public sealed class CheatEnginePluginAttribute : global::System.Attribute
                                     {
                                         public CheatEnginePluginAttribute(string name) => Name = name;

                                         public string Name { get; }
                                     }
                                 }

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

                                         static abstract global::System.ReadOnlySpan<byte> Utf8Name { get; }
                                     }
                                 }

                                 namespace CheatEngine.SDK.Hosting.Bootstrap
                                 {
                                     public static class PluginHost
                                     {
                                         public static int CallCount;
                                         public static byte[]? LastUtf8Name;
                                         public static global::CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin? LastPlugin;

                                         public static int InitializeManaged<TFactory>(nint initRecord, int size)
                                             where TFactory : global::CheatEngine.SDK.Hosting.Plugin.IPluginFactory
                                         {
                                             CallCount++;
                                             if (size < 0)
                                             {
                                                 throw new global::System.InvalidOperationException("Stub failure requested by the test.");
                                             }

                                             LastUtf8Name = TFactory.Utf8Name.ToArray();
                                             LastPlugin = TFactory.Create();
                                             return 1;
                                         }
                                     }
                                 }
                                 """;
}
