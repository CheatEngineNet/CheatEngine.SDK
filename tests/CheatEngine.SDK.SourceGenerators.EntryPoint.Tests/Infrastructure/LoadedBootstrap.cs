using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;

namespace CheatEngine.SDK.SourceGenerators.EntryPoint.Tests.Infrastructure;

/// <summary>
///     Emits a generator run's output compilation, loads it next to the compiled contract stubs in a collectible
///     <see cref="AssemblyLoadContext" /> and calls the generated entry point the way the host does: by type and method
///     name, through a <c>(IntPtr, int) -> int</c> delegate.
/// </summary>
internal sealed class LoadedBootstrap : IDisposable
{
    private readonly AssemblyLoadContext _context;
    private readonly Func<IntPtr, int, int> _initialize;
    private readonly Type _pluginHost;

    private LoadedBootstrap(AssemblyLoadContext context, Assembly hosting, Assembly plugin)
    {
        _context = context;
        _pluginHost = hosting.GetType("CheatEngine.SDK.Hosting.Bootstrap.PluginHost", true)!;

        // Same lookup as the host: type 'CESDK.CESDK' in the plugin assembly, static method 'CEPluginInitialize'.
        var entryPoint = plugin.GetType("CESDK.CESDK", true)!;
        var method = entryPoint.GetMethod("CEPluginInitialize", BindingFlags.Public | BindingFlags.Static)
                     ?? throw new MissingMethodException("CESDK.CESDK", "CEPluginInitialize");
        _initialize = method.CreateDelegate<Func<IntPtr, int, int>>();
    }

    /// <summary>Number of calls that reached the stub <c>PluginHost.InitializeManaged</c>.</summary>
    public int HostCallCount => (int)ReadHostField("CallCount")!;

    /// <summary>The opaque host value handed to the hosting runtime by the generated bootstrap.</summary>
    public int LastHostArgument => (int)ReadHostField("LastHostArgument")!;

    /// <summary><c>TFactory.Utf8Name</c> as seen by the stub host.</summary>
    public byte[]? LastUtf8Name => (byte[]?)ReadHostField("LastUtf8Name");

    /// <summary>Full type name of the instance returned by <c>TFactory.Create()</c>.</summary>
    public string? LastPluginTypeName => ReadHostField("LastPlugin")?.GetType().FullName;

    public void Dispose()
    {
        _context.Unload();
    }

    public static LoadedBootstrap Load(RoslynEnvironment environment, Compilation outputCompilation)
    {
        using MemoryStream pluginImage = new();
        var result = outputCompilation.Emit(pluginImage, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Success, "The plugin compilation does not emit:\n" + string.Join('\n', result.Diagnostics));
        pluginImage.Position = 0;

        AssemblyLoadContext context = new("CheatEngine.SDK.EntryPoint.Tests.Bootstrap", true);
        using MemoryStream annotationsImage = new([.. environment.AnnotationsImage]);
        var annotations = context.LoadFromStream(annotationsImage);
        using MemoryStream hostingImage = new([.. environment.HostingImage]);
        var hosting = context.LoadFromStream(hostingImage);
        context.Resolving += (_, name) =>
            string.Equals(name.Name, ContractStubs.AnnotationsAssemblyName, StringComparison.Ordinal)
                ? annotations
                : string.Equals(name.Name, ContractStubs.HostingAssemblyName, StringComparison.Ordinal)
                    ? hosting
                    : null;
        var plugin = context.LoadFromStream(pluginImage);

        return new LoadedBootstrap(context, hosting, plugin);
    }

    /// <summary>Calls the generated <c>CESDK.CESDK.CEPluginInitialize</c>.</summary>
    public int Initialize(IntPtr args, int opaqueArgument
    )
    {
        return
            _initialize
            (
                args
                ,
                opaqueArgument);
    }

    private object? ReadHostField(string
        name
    )
    {
        return _pluginHost.GetField(name, BindingFlags.Public | BindingFlags.Static)!.GetValue(null);
    }
}
