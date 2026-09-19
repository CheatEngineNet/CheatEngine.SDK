using System.Reflection;
using System.Runtime.Loader;

namespace CheatEngine.SDK.SourceGenerators.EngineApi.Tests.Infrastructure;

/// <summary>
///     Emits a generator run's output compilation (the generated wrappers) and loads it into its own
///     <see cref="AssemblyLoadContext" />, where <c>CheatEngine.SDK.Lua</c> and the other SDK assemblies resolve to the copies this
///     test process already runs: the generated code then talks to the same <c>LuaRuntime</c> the test attaches.
/// </summary>
internal sealed class GeneratedAssembly
{
    private const BindingFlags StaticMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    private static int s_counter;

    private GeneratedAssembly(Assembly assembly)
    {
        Assembly = assembly;
    }

    /// <summary>The loaded assembly.</summary>
    public Assembly Assembly { get; }

    /// <summary>
    ///     Compiles, emits and loads <paramref name="run" />'s output; fails the test when it does not compile clean or
    ///     emit.
    /// </summary>
    public static GeneratedAssembly Load(GeneratorRun run)
    {
        run.AssertCompilesClean();

        using MemoryStream image = new();
        var result = run.OutputCompilation.Emit(image, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Success, "The output compilation does not emit:\n" + string.Join('\n', result.Diagnostics));
        image.Position = 0;

        AssemblyLoadContext context = new("CheatEngine.SDK.EngineApi.Tests." + Interlocked.Increment(ref s_counter));
        return new GeneratedAssembly(context.LoadFromStream(image));
    }

    /// <summary>A delegate over a static method, for calls that must not allocate (reflection invocation does).</summary>
    public TDelegate Delegate<TDelegate>(string typeName, string methodName)
        where TDelegate : Delegate
    {
        var parameters = typeof(TDelegate).GetMethod("Invoke")!.GetParameters();
        Type[] parameterTypes = [.. parameters.Select(static parameter => parameter.ParameterType)];
        var type = Assembly.GetType(typeName, true)!;
        var method = type.GetMethod(methodName, StaticMembers, parameterTypes) ??
                     throw new MissingMethodException(typeName, methodName);
        return method.CreateDelegate<TDelegate>();
    }
}
