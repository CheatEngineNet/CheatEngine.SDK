namespace CheatEngine.SDK.SourceGenerators.EntryPoint.Tests.Infrastructure;

/// <summary>
///     Source stubs of the cross-project contract the generator targets:
///     <c>CheatEngine.SDK.Annotations.Plugin.CheatEnginePluginAttribute</c> and the <c>CheatEngine.SDK.Hosting</c>
///     bootstrap types. They are
///     compiled into two assemblies with the SDK's real assembly identities and referenced by the test compilations,
///     the way a real plugin references the SDK. The real projects are deliberately not referenced: this generator may
///     depend on the contract only.
/// </summary>
/// <remarks>
///     <c>PluginHost</c> is instrumented instead of being empty, so that the execution tests can observe what the
///     generated bootstrap handed over: it records the factory's UTF-8 name and the plugin instance, and throws when
///     <c>hostArgument</c> is negative to prove the generated catch-all.
/// </remarks>
internal static class ContractStubs
{
	public const string AnnotationsAssemblyName = "CheatEngine.SDK.Annotations";

	public const string HostingAssemblyName = "CheatEngine.SDK.Hosting";

	public const string AnnotationsSource = """
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
	                                        """;

	public const string HostingSource = """
	                                    #nullable enable
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
	                                            public static int LastHostArgument;
	                                            public static byte[]? LastUtf8Name;
	                                            public static global::CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin? LastPlugin;

	                                            public static int InitializeManaged<TFactory>(nint initRecord, int hostArgument)
	                                                where TFactory : global::CheatEngine.SDK.Hosting.Plugin.IPluginFactory
	                                            {
	                                                CallCount++;
	                                                LastHostArgument = hostArgument;
	                                                if (hostArgument < 0)
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
