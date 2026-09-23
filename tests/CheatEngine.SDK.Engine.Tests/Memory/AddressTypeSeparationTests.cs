using System.Reflection;

using CheatEngine.SDK.Engine.Memory;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.SDK.Engine.Tests.Memory;

/// <summary>
///     The three pointer-like value types stay apart (audit A07-01): a target <see cref="Address" />, a host
///     <see cref="HostAddress" /> and a <see cref="CEObject" /> handle expose no conversion operator to each other or
///     to a floating-point type, so a 64-bit address can never be rounded through a <see cref="double" /> implicitly.
/// </summary>
public sealed class AddressTypeSeparationTests
{
	// The 1.0.0 surface: the lossless raw conversions of Address, and nothing else.
	private static readonly string[] s_allowed =
	[
		"Address.op_Explicit(Address) -> UInt64",
		"Address.op_Implicit(UInt64) -> Address"
	];

	[Fact]
	[Trait("Qualification", "Q21")]
	public void Address_types_expose_no_conversion_to_each_other_or_to_double()
	{
		Type[] types = [typeof(Address), typeof(HostAddress), typeof(CEObject)];

		string[] conversions =
		[
			.. types.SelectMany(static type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
															   BindingFlags.Static | BindingFlags.DeclaredOnly))
				.Where(static method => method.Name is "op_Implicit" or "op_Explicit")
				.Select(Describe)
				.Order(StringComparer.Ordinal)
		];

		Assert.Equal(s_allowed, conversions);
		Assert.DoesNotContain(conversions, static conversion =>
			conversion.Contains("Double", StringComparison.Ordinal) ||
			conversion.Contains("Single", StringComparison.Ordinal) ||
			conversion.Contains("Decimal", StringComparison.Ordinal));
	}

	private static string Describe(MethodInfo method)
	{
		string parameters = string.Join(", ", method.GetParameters().Select(static p => p.ParameterType.Name));
		return method.DeclaringType!.Name + "." + method.Name + "(" + parameters + ") -> " + method.ReturnType.Name;
	}
}
