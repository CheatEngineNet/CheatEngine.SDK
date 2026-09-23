using System.Reflection;
using System.Runtime.CompilerServices;

using CheatEngine.SDK.Abi.Managed;

namespace CheatEngine.SDK.Abi.Tests.Support;

/// <summary>
///     The set of structures the assembly-wide gates inspect: every value type of <c>CheatEngine.SDK.Abi</c>, public,
///     internal or nested private alike, minus enumerations and compiler-generated types. Shared by
///     <see cref="AssemblyConformanceTests" /> (sizes and shape) and <c>FieldLayout.FieldLayoutContractTests</c>
///     (per-field
///     offsets and widths), so both gates always look at exactly the same structures.
/// </summary>
internal static class AbiStructures
{
	/// <summary>The assembly under test.</summary>
	public static Assembly AbiAssembly
	{
		get;
	} = typeof(PluginInitRecord).Assembly;

	/// <summary>
	///     Public and non-public structures alike: an internal helper structure is as much part of a layout as the
	///     public structure that embeds it. Compiler-generated types (static data blobs) are not ours to judge.
	/// </summary>
	public static IEnumerable<Type> All()
	{
		return AbiAssembly.GetTypes()
			.Where(static type => type.IsValueType && !type.IsEnum && !IsCompilerGenerated(type));
	}

	/// <summary>
	///     Whether <paramref name="type" /> or one of its declaring types was emitted by the compiler (name starting with
	///     <c>&lt;</c> or <see cref="CompilerGeneratedAttribute" />).
	/// </summary>
	public static bool IsCompilerGenerated(Type type)
	{
		for (Type? current = type; current is not null; current = current.DeclaringType)
		{
			if (current.Name.StartsWith('<') || current.IsDefined(typeof(CompilerGeneratedAttribute), false))
			{
				return true;
			}
		}

		return false;
	}
}
