using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.Infrastructure;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.EndToEnd;

/// <summary>
///     Generated <c>[LuaGlobal]</c> wrappers with <c>LuaOptional&lt;T&gt;</c> arguments run against stand-in globals that
///     report exactly what they received (<c>select('#', ...)</c>, the Lua type of every argument): an omitted argument
///     is not pushed, <c>Nil</c> is pushed as <c>nil</c>, and an omitted argument before a present one is refused before
///     Lua is touched (audit A19-07, A07-28, A07-29, A06-15; binding DoD A.1, A.2, A.8).
/// </summary>
[Collection(LuaRuntimeSuite.Name)]
[Trait("Category", "NativeLua")]
public sealed class LuaGlobalOptionalArgumentEndToEndTests(RoslynFixture roslyn) : IClassFixture<RoslynFixture>
{
	private const string OptionalsType = "Demo.Optionals";

	public static TheoryData<int> PresentCounts => [0, 1, 2];

	[Fact]
	public void Omitted_optional_argument_is_not_pushed_and_nil_is_pushed_as_nil()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, OptionalBindingSources.StandIns);
		GeneratedAssembly assembly = LoadSuite(roslyn);
		KindsDelegate kinds = assembly.Delegate<KindsDelegate>(OptionalsType, "Kinds");
		ArityDelegate arity = assembly.Delegate<ArityDelegate>(OptionalsType, "Arity");

		Assert.Equal("number", kinds(1, default));
		Assert.Equal("number,nil", kinds(1, LuaOptional.Nil<long>()));
		Assert.Equal("number,number", kinds(1, LuaOptional.Of(2L)));
		Assert.Equal(1, arity(1, LuaOptional.Omitted<long>(), default));
		Assert.Equal(2, arity(1, LuaOptional.Nil<long>(), default));
		Assert.Equal(3, arity(1, LuaOptional.Nil<long>(), LuaOptional.Nil<string>()));
		Assert.Equal(0, L.Top);
	}

	[Theory]
	[MemberData(nameof(PresentCounts))]
	public void Every_optional_arity_of_one_binding_reaches_lua_with_its_exact_argument_count(int present)
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, OptionalBindingSources.StandIns);
		GeneratedAssembly assembly = LoadSuite(roslyn);
		ArityDelegate arity = assembly.Delegate<ArityDelegate>(OptionalsType, "Arity");
		TryArityDelegate tryArity = assembly.Delegate<TryArityDelegate>(OptionalsType, "TryArity");
		ArityDetailedDelegate detailed = assembly.Delegate<ArityDetailedDelegate>(OptionalsType, "ArityDetailed");
		ArityOnDelegate arityOn = assembly.Delegate<ArityOnDelegate>(OptionalsType, "ArityOn");

		LuaOptional<long> second = present >= 1 ? LuaOptional.Of(5L) : default;
		LuaOptional<string> third = present >= 2 ? LuaOptional.Of("x") : default;

		Assert.Equal(1 + present, arity(1, second, third));
		Assert.True(tryArity(1, second, out long tryCount));
		Assert.Equal(present >= 1 ? 2 : 1, tryCount);
		LuaOperationStatus status = detailed(1, present >= 1 ? LuaOptional.Of(1.5) : default,
			present >= 2 ? LuaOptional.Of(true) : default, out long detailedCount);
		Assert.Equal(LuaOperationStatusKind.Success, status.Kind);
		Assert.Equal(1 + present, detailedCount);
		Assert.Equal(present >= 1 ? 1 : 0, arityOn(L, present >= 1 ? LuaOptional.Of((nuint) 0x1000) : default));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Omitted_optional_argument_followed_by_a_present_one_throws_before_touching_lua()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		GeneratedAssembly assembly = LoadSuite(roslyn);
		ArityDelegate arity = assembly.Delegate<ArityDelegate>(OptionalsType, "Arity");
		ReadBytesLikeDelegate readBytes = assembly.Delegate<ReadBytesLikeDelegate>(OptionalsType, "ReadBytesLike");

		// Detached: the argument check runs before the runtime is asked for an operation.
		LuaRuntime.Detach();
		ArgumentException detached = Assert.Throws<ArgumentException>(() => arity(1, default, LuaOptional.Of("x")));
		Assert.Equal("second", detached.ParamName);

		using RuntimeScope scope = new(state);
		LuaTest.Run(L, OptionalBindingSources.StandIns);
		ArgumentException attached = Assert.Throws<ArgumentException>(() => arity(1, default, LuaOptional.Of("x")));
		Assert.Equal("second", attached.ParamName);
		Assert.Contains("Lua global 'arity'", attached.Message, StringComparison.Ordinal);
		ArgumentException nilLater =
			Assert.Throws<ArgumentException>(() => readBytes(0x1000, default, LuaOptional.Nil<bool>()));
		Assert.Equal("count", nilLater.ParamName);
		Assert.Equal(0, LuaTest.RunForInteger(L, "return calls"u8));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Nil_and_omitted_select_different_overloads_of_a_readBytes_like_stand_in()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, OptionalBindingSources.StandIns);
		ReadBytesLikeDelegate readBytes =
			LoadSuite(roslyn).Delegate<ReadBytesLikeDelegate>(OptionalsType, "ReadBytesLike");

		Assert.Equal("one byte", readBytes(0x1000, default, default));
		Assert.Equal("values", readBytes(0x1000, LuaOptional.Of(4), default));
		Assert.Equal("nil flag", readBytes(0x1000, LuaOptional.Of(4), LuaOptional.Nil<bool>()));
		Assert.Equal("table", readBytes(0x1000, LuaOptional.Of(4), LuaOptional.Of(true)));
		Assert.Equal("values", readBytes(0x1000, LuaOptional.Of(4), LuaOptional.Of(false)));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Warm_optional_argument_form_allocates_nothing()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, OptionalBindingSources.StandIns);
		GeneratedAssembly assembly = LoadSuite(roslyn);
		TryArityDelegate tryArity = assembly.Delegate<TryArityDelegate>(OptionalsType, "TryArity");
		ArityDetailedDelegate detailed = assembly.Delegate<ArityDetailedDelegate>(OptionalsType, "ArityDetailed");
		long sink = 0;

		AllocationGate.AssertZero(() =>
		{
			if (!tryArity(1, LuaOptional.Of(2L), out long present) || present != 2
			                                                       || !tryArity(1, default, out long omitted) ||
			                                                       omitted != 1
			                                                       || !detailed(1, LuaOptional.Nil<double>(),
				                                                       LuaOptional.Of(true), out long nil).IsSuccess ||
			                                                       nil != 3)
			{
				throw new InvalidOperationException("wrong count");
			}

			sink += present + omitted + nil;
		});

		Assert.NotEqual(0, sink);
		Assert.Equal(0, L.Top);
	}

	private static GeneratedAssembly LoadSuite(RoslynFixture roslyn)
	{
		return GeneratedAssembly.Load(roslyn.Run(OptionalBindingSources.GlobalSuite));
	}

	private delegate long ArityDelegate(long first, LuaOptional<long> second, LuaOptional<string> third);

	private delegate string KindsDelegate(long first, LuaOptional<long> second);

	private delegate bool TryArityDelegate(long first, LuaOptional<long> second, out long count);

	private delegate LuaOperationStatus ArityDetailedDelegate(long first, LuaOptional<double> second,
		LuaOptional<bool> third, out long count);

	private delegate long ArityOnDelegate(LuaState state, LuaOptional<nuint> first);

	private delegate string ReadBytesLikeDelegate(nuint address, LuaOptional<int> count, LuaOptional<bool> asTable);
}
