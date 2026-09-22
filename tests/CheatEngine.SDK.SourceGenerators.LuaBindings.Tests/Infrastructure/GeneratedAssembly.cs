using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;

using Microsoft.CodeAnalysis.Emit;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.Infrastructure;

/// <summary>
///     Emits a generator run's output compilation (user code + generated code) and loads it into its own
///     <see cref="AssemblyLoadContext" />, where <c>CheatEngine.SDK.Lua</c> and the other SDK assemblies resolve to the
///     copies this
///     test process already runs: the generated code then talks to the same <c>LuaRuntime</c> the test attaches.
/// </summary>
/// <remarks>
///     The context is not collectible on purpose: the loaded code holds static <c>LuaRef</c> caches and its thunks are
///     referenced by function pointer from Lua closures; unloading it under a live state would be unsafe, and the
///     process is short-lived anyway.
/// </remarks>
internal sealed class GeneratedAssembly
{
	private const BindingFlags StaticMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

	private static int s_counter;

	private GeneratedAssembly(Assembly assembly)
	{
		Assembly = assembly;
	}

	/// <summary>The loaded assembly.</summary>
	public Assembly Assembly
	{
		get;
	}

	/// <summary>
	///     Compiles, emits and loads <paramref name="run" />'s output; fails the test when it does not compile clean or
	///     emit.
	/// </summary>
	public static GeneratedAssembly Load(GeneratorRun run)
	{
		run.AssertCompilesClean();

		using MemoryStream image = new();
		EmitResult result = run.OutputCompilation.Emit(image, cancellationToken: TestContext.Current.CancellationToken);
		Assert.True(result.Success, "The output compilation does not emit:\n" + string.Join('\n', result.Diagnostics));
		image.Position = 0;

		string name = string.Create(
			CultureInfo.InvariantCulture,
			$"CheatEngine.SDK.LuaBindings.Tests.{Interlocked.Increment(ref s_counter)}");
		AssemblyLoadContext context = new(name);
		return new GeneratedAssembly(context.LoadFromStream(image));
	}

	/// <summary>
	///     The static method <paramref name="methodName" /> of <paramref name="typeName" />;
	///     <paramref name="parameterTypes" /> picks an overload.
	/// </summary>
	public MethodInfo Method(string typeName, string methodName, Type[]? parameterTypes = null)
	{
		Type type = Assembly.GetType(typeName, true)!;
		MethodInfo? method = parameterTypes is null
			? type.GetMethod(methodName, StaticMembers)
			: type.GetMethod(methodName, StaticMembers, parameterTypes);
		return method ?? throw new MissingMethodException(typeName, methodName);
	}

	/// <summary>A delegate over a static method, for calls that must not allocate (reflection invocation does).</summary>
	public TDelegate Delegate<TDelegate>(string typeName, string methodName)
		where TDelegate : Delegate
	{
		ParameterInfo[] parameters = typeof(TDelegate).GetMethod("Invoke")!.GetParameters();
		Type[] parameterTypes = [.. parameters.Select(static parameter => parameter.ParameterType)];
		return Method(typeName, methodName, parameterTypes).CreateDelegate<TDelegate>();
	}

	/// <summary>
	///     Whether <paramref name="typeName" /> declares a static method named <paramref name="methodName" /> (any
	///     accessibility).
	/// </summary>
	public bool HasMethod(string typeName, string methodName)
	{
		return Assembly.GetType(typeName, true)!.GetMethods(StaticMembers)
			.Any(method => string.Equals(method.Name, methodName, StringComparison.Ordinal));
	}
}
