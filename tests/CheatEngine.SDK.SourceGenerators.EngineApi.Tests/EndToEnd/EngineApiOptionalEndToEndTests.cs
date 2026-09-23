using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.SourceGenerators.EngineApi.Tests.Infrastructure;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.SourceGenerators.EngineApi.Tests.EndToEnd;

/// <summary>
///     The <c>opt:</c>, <c>opt-result:</c>, <c>rest:</c> and <c>form: outcome</c> grammar, compiled and run against
///     stand-in globals on the bundled Lua 5.3 (audit F10, A06-14, A06-15): an omitted optional argument is not pushed,
///     <c>Nil</c> is pushed as <c>nil</c>, an optional Address keeps its state through the facade, zero results are not
///     <c>nil</c>, and an absent global is <see cref="LuaOperationStatusKind.GlobalUnavailable" />. Every test asserts
///     <c>L.Top == 0</c>.
/// </summary>
[Collection(LuaRuntimeSuite.Name)]
[Trait("Category", "NativeLua")]
public sealed class EngineApiOptionalEndToEndTests(RoslynFixture roslyn) : IClassFixture<RoslynFixture>
{
	private const string BindingsType = "Demo.Optional.OptionalProbes";

	private static readonly string Spec = SpecSources.Ce77Header("Demo.Optional", "OptionalProbes") + """
		global: allocateMemory
		method: TryAllocate
		form: try
		arg: size:int64
		opt: preferredBaseAddress:address
		opt: protection:int32
		result: address:address
		nil: expected-failure
		doc: Allocates target memory.

		global: shape
		method: Shape
		form: outcome
		arg: mode:int32
		opt-result: first:address
		nil: absence
		doc: Returns zero, one nil or one value.

		global: missing
		method: Missing
		form: outcome
		result: value:int32
		nil: none
		doc: Calls a global that does not exist.

		global: seq
		method: Sequence
		form: outcome
		arg: n:int32
		rest: values:int64
		nil: none
		doc: Returns n values.
		""";

	private static ReadOnlySpan<byte> StandIns => """
	                                              function allocateMemory(...)
	                                                local n = select('#', ...)
	                                                local size, base, protection = ...
	                                                if n == 1 then return 0x10000 + size end
	                                                if base == nil then return 0x20000 + n end
	                                                if n == 2 then return base end
	                                                return base + protection
	                                              end
	                                              function shape(mode)
	                                                if mode == 0 then return end
	                                                if mode == 1 then return nil end
	                                                return 0x100000000
	                                              end
	                                              function seq(n)
	                                                local t = {}
	                                                for i = 1, n do t[i] = i end
	                                                return table.unpack(t, 1, n)
	                                              end
	                                              """u8;

	[Fact]
	public void Optional_argument_is_omitted_nil_or_pushed()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, StandIns);
		TryAllocateDelegate allocate = LoadSuite(roslyn).Delegate<TryAllocateDelegate>(BindingsType, "TryAllocate");

		Assert.True(allocate(0x10, default, default, out Address omitted));
		Assert.Equal(0x10010UL, omitted.ToUInt64());
		Assert.True(allocate(0x10, LuaOptional.Nil<Address>(), LuaOptional.Of(4), out Address nilBase));
		Assert.Equal(0x20003UL, nilBase.ToUInt64());
		Assert.True(allocate(0x10, LuaOptional.Of(new Address(0x1_0000_0000UL)), default, out Address based));
		Assert.Equal(0x1_0000_0000UL, based.ToUInt64());
		Assert.True(allocate(0x10, LuaOptional.Of(new Address(0x5000UL)), LuaOptional.Of(4), out Address both));
		Assert.Equal(0x5004UL, both.ToUInt64());
		Assert.Throws<ArgumentException>(() => allocate(0x10, default, LuaOptional.Of(4), out _));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Optional_result_distinguishes_zero_results_from_nil()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, StandIns);
		ShapeDelegate shape = LoadSuite(roslyn).Delegate<ShapeDelegate>(BindingsType, "Shape");

		Assert.Equal(LuaOperationStatusKind.Success, shape(0, out LuaOptional<Address> none).Kind);
		Assert.True(none.IsOmitted);
		Assert.Equal(LuaOperationStatusKind.Success, shape(1, out LuaOptional<Address> nil).Kind);
		Assert.True(nil.IsNil);
		Assert.Equal(LuaOperationStatusKind.Success, shape(2, out LuaOptional<Address> value).Kind);
		Assert.Equal(0x1_0000_0000UL, value.Value.ToUInt64());
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Absent_global_is_global_unavailable_in_the_outcome_form()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, StandIns);
		MissingDelegate missing = LoadSuite(roslyn).Delegate<MissingDelegate>(BindingsType, "Missing");

		LuaOperationStatus status = missing(out int value);

		Assert.Equal(LuaOperationStatusKind.GlobalUnavailable, status.Kind);
		Assert.Equal(0, value);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Rest_result_copies_every_value()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, StandIns);
		SequenceDelegate sequence = LoadSuite(roslyn).Delegate<SequenceDelegate>(BindingsType, "Sequence");
		long[] values = new long[4];

		Assert.Equal(LuaOperationStatusKind.Success, sequence(3, values, out int count).Kind);
		Assert.Equal(3, count);
		Assert.Equal([1L, 2L, 3L], values[..3]);
		Assert.Equal(LuaOperationStatusKind.ResultCapacityExceeded, sequence(5, values, out int needed).Kind);
		Assert.Equal(5, needed);
		Assert.Equal(0, L.Top);
	}

	private static GeneratedAssembly LoadSuite(RoslynFixture roslyn)
	{
		return GeneratedAssembly.Load(roslyn.Run("optional.cheatengine-sdk-api.txt", Spec));
	}

	private delegate bool TryAllocateDelegate(long size, LuaOptional<Address> preferredBaseAddress,
		LuaOptional<int> protection, out Address address);

	private delegate LuaOperationStatus ShapeDelegate(int mode, out LuaOptional<Address> first);

	private delegate LuaOperationStatus MissingDelegate(out int value);

	private delegate LuaOperationStatus SequenceDelegate(int n, Span<long> values, out int valuesCount);
}
