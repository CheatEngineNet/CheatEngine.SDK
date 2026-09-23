using System.Text;

using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.Infrastructure;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.EndToEnd;

/// <summary>
///     Qualification Q21 at C2: values at the 32-bit, 2^53 and 64-bit boundaries, produced by Lua itself, reach
///     <see cref="int" />, <see cref="long" /> and <see cref="nuint" /> results in every form, and exported function
///     arguments, with every bit kept or a refusal, never a value rounded through a <see cref="double" /> (audit A07-02,
///     A07-08, A12-08, A18-18). The stand-in is <see cref="FidelityBindingSources.NumericStandIns" />.
/// </summary>
[Collection(LuaRuntimeSuite.Name)]
[Trait("Category", "NativeLua")]
public sealed class LuaGlobalNumericBoundaryEndToEndTests(RoslynFixture roslyn) : IClassFixture<RoslynFixture>
{
	private const string NumbersType = "Demo.Numbers";

	/// <summary>A Lua expression, whether it fits 32 bits, whether it is an exact 64-bit integer, and that integer.</summary>
	public static TheoryData<string, bool, bool, long> Boundaries => new()
	{
		{ "2147483647", true, true, int.MaxValue },
		{ "-2147483648", true, true, int.MinValue },
		{ "2147483648", false, true, 2147483648L },
		{ "4294967296", false, true, 4294967296L },
		{ "9007199254740992", false, true, 9007199254740992L },
		{ "9007199254740993", false, true, 9007199254740993L },
		{ "math.maxinteger", false, true, long.MaxValue },
		{ "math.mininteger", false, true, long.MinValue },
		{ "2^53 - 1", false, true, 9007199254740991L },
		{ "2^31", false, true, 2147483648L },
		{ "2^53", false, false, 0L },
		{ "2^53 + 2", false, false, 0L },
		{ "-2^53", false, false, 0L },
		{ "2^63", false, false, 0L },
		{ "1.5", false, false, 0L }
	};

	[Theory]
	[Trait("Qualification", "Q21")]
	[MemberData(nameof(Boundaries))]
	public void Integer_results_keep_every_bit_or_are_refused_never_rounded(string expression, bool fitsInt32,
		bool exact, long expected)
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, FidelityBindingSources.NumericStandIns);
		GeneratedAssembly assembly = LoadSuite(roslyn);

		AssertInt32Forms(assembly, expression, fitsInt32, (int) (fitsInt32 ? expected : 0));
		AssertInt64Forms(assembly, expression, exact, exact ? expected : 0);
		AssertAddressForms(assembly, expression, exact, exact ? unchecked((nuint) (ulong) expected) : 0);
		Assert.Equal(0, L.Top);
	}

	[Theory]
	[Trait("Qualification", "Q21")]
	[MemberData(nameof(Boundaries))]
	public void Function_arguments_refuse_floats_at_or_above_2_pow_53(string expression, bool fitsInt32, bool exact,
		long expected)
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaStatus registered = (LuaStatus) LoadSuite(roslyn).Method(NumbersType, "RegisterLuaFunctions")
			.Invoke(null, [L])!;
		Assert.True(registered.IsOk);

		AssertArgument(L, "n_int", expression, fitsInt32, expected);
		AssertArgument(L, "n_long", expression, exact, expected);
		AssertArgument(L, "n_addr", expression, exact, expected);
		Assert.Equal(0, L.Top);
	}

	private static void AssertInt32Forms(GeneratedAssembly assembly, string expression, bool accepted, int expected)
	{
		TryInt32Delegate tryForm = assembly.Delegate<TryInt32Delegate>(NumbersType, "TryInt32");
		OutcomeInt32Delegate outcome = assembly.Delegate<OutcomeInt32Delegate>(NumbersType, "OutcomeInt32");
		Int32Delegate throwing = assembly.Delegate<Int32Delegate>(NumbersType, "Int32");

		Assert.Equal(accepted, tryForm(expression, out int tried));
		Assert.Equal(expected, tried);
		Assert.Equal(Kind(accepted), outcome(expression, out int detailed).Kind);
		Assert.Equal(expected, detailed);
		AssertThrowing(accepted, expected, () => throwing(expression));
	}

	private static void AssertInt64Forms(GeneratedAssembly assembly, string expression, bool accepted, long expected)
	{
		TryInt64Delegate tryForm = assembly.Delegate<TryInt64Delegate>(NumbersType, "TryInt64");
		OutcomeInt64Delegate outcome = assembly.Delegate<OutcomeInt64Delegate>(NumbersType, "OutcomeInt64");
		Int64Delegate throwing = assembly.Delegate<Int64Delegate>(NumbersType, "Int64");

		Assert.Equal(accepted, tryForm(expression, out long tried));
		Assert.Equal(expected, tried);
		Assert.Equal(Kind(accepted), outcome(expression, out long detailed).Kind);
		Assert.Equal(expected, detailed);
		AssertThrowing(accepted, expected, () => throwing(expression));
	}

	private static void AssertAddressForms(GeneratedAssembly assembly, string expression, bool accepted, nuint expected)
	{
		TryAddressDelegate tryForm = assembly.Delegate<TryAddressDelegate>(NumbersType, "TryAddress");
		OutcomeAddressDelegate outcome = assembly.Delegate<OutcomeAddressDelegate>(NumbersType, "OutcomeAddress");
		AddressDelegate throwing = assembly.Delegate<AddressDelegate>(NumbersType, "Address");

		Assert.Equal(accepted, tryForm(expression, out nuint tried));
		Assert.Equal(expected, tried);
		Assert.Equal(Kind(accepted), outcome(expression, out nuint detailed).Kind);
		Assert.Equal(expected, detailed);
		AssertThrowing(accepted, expected, () => throwing(expression));
	}

	private static void AssertThrowing<T>(bool accepted, T expected, Func<T> call)
	{
		if (accepted)
		{
			Assert.Equal(expected, call());
			return;
		}

		Assert.Equal("The Lua global 'boundary' returned a number value, not an integer.",
			Assert.Throws<LuaException>(() => call()).Message);
	}

	private static void AssertArgument(LuaState L, string function, string expression, bool accepted, long expected)
	{
		string call = function + "(" + expression + ")";
		if (accepted)
		{
			// The value comes back as the same Lua integer: every bit kept, integer subtype.
			Assert.Equal("integer", LuaTest.RunForString(L, Encode("return math.type(" + call + ")")));
			Assert.Equal(expected, LuaTest.RunForInteger(L, Encode("return " + call)));
			return;
		}

		Assert.Equal("test:1: bad argument #1 (integer expected, got number)",
			LuaTest.RunForError(L, Encode("return pcall(function() " + call + " end)")));
	}

	private static LuaOperationStatusKind Kind(bool accepted)
	{
		return accepted ? LuaOperationStatusKind.Success : LuaOperationStatusKind.InvalidResult;
	}

	private static byte[] Encode(string source)
	{
		return Encoding.UTF8.GetBytes(source);
	}

	private static GeneratedAssembly LoadSuite(RoslynFixture roslyn)
	{
		return GeneratedAssembly.Load(roslyn.Run(FidelityBindingSources.NumericSuite));
	}

	private delegate bool TryInt32Delegate(string expression, out int value);

	private delegate bool TryInt64Delegate(string expression, out long value);

	private delegate bool TryAddressDelegate(string expression, out nuint value);

	private delegate LuaOperationStatus OutcomeInt32Delegate(string expression, out int value);

	private delegate LuaOperationStatus OutcomeInt64Delegate(string expression, out long value);

	private delegate LuaOperationStatus OutcomeAddressDelegate(string expression, out nuint value);

	private delegate int Int32Delegate(string expression);

	private delegate long Int64Delegate(string expression);

	private delegate nuint AddressDelegate(string expression);
}
