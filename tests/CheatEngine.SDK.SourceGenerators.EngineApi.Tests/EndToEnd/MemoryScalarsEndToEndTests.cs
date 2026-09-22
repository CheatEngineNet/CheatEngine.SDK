using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.SourceGenerators.EngineApi.Tests.Infrastructure;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.SourceGenerators.EngineApi.Tests.EndToEnd;

/// <summary>
///     Proves the whole chain once: spec file -&gt; <see cref="EngineApiGenerator" /> (+ the shared
///     <c>LuaGlobalCallEmitter</c>) -&gt; a compiled wrapper, loaded and called through delegates against a real Lua 5.3
///     state whose stand-in globals (a small Lua chunk, in place of Cheat Engine's <c>readInteger</c>/<c>writeInteger</c>
///     /<c>readQword</c>/<c>writeQword</c>/<c>beep</c>) back two in-memory tables, asserting a correct read/write round
///     trip and zero allocation on the warm success path.
/// </summary>
[Collection(LuaRuntimeSuite.Name)]
[Trait("Category", "NativeLua")]
public sealed class MemoryScalarsEndToEndTests(RoslynFixture roslyn) : IClassFixture<RoslynFixture>
{
	private const string BindingsType = "Demo.EndToEnd.MemoryScalars";

	private static ReadOnlySpan<byte> StandIns => """
	                                              local mem32 = {}
	                                              local mem64 = {}
	                                              -- Cheat Engine returns unsigned 32-bit values unless its optional signed flag is true.
	                                              -- Preserve that contract here so the generated binding must supply the flag.
	                                              function readInteger(address, signed)
	                                                  local value = mem32[address]
	                                                  if value == nil or signed then return value end
	                                                  return value < 0 and value + 4294967296 or value
	                                              end
	                                              function writeInteger(address, value) mem32[address] = value; return true end
	                                              function readQword(address) return mem64[address] end
	                                              function writeQword(address, value) mem64[address] = value; return true end
	                                              beeps = 0
	                                              function beep() beeps = beeps + 1 end
	                                              """u8;

	[Fact]
	public void Write_then_read_round_trips_signed_32_bit_boundaries_and_a_missing_address_reads_as_false()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, StandIns);
		GeneratedAssembly assembly = LoadSuite(roslyn);
		WriteInt32Delegate write = assembly.Delegate<WriteInt32Delegate>(BindingsType, "WriteInt32");
		TryReadInt32Delegate tryRead = assembly.Delegate<TryReadInt32Delegate>(BindingsType, "TryReadInt32");

		Assert.True(write(0x1000, 42));
		Assert.True(tryRead(0x1000, out int value));
		Assert.Equal(42, value);

		foreach (int expected in new[] { int.MinValue, -7, -1 })
		{
			Assert.True(write(0x1000, expected));
			Assert.True(tryRead(0x1000, out int actual));
			Assert.Equal(expected, actual);
		}

		Assert.False(tryRead(0x2000, out int missing));
		Assert.Equal(0, missing);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Write_then_read_round_trips_a_64_bit_value_that_does_not_fit_32_bits()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, StandIns);
		GeneratedAssembly assembly = LoadSuite(roslyn);
		WriteInt64Delegate write = assembly.Delegate<WriteInt64Delegate>(BindingsType, "WriteInt64");
		TryReadInt64Delegate tryRead = assembly.Delegate<TryReadInt64Delegate>(BindingsType, "TryReadInt64");

		const long Large = 0x1_0000_0000L; // does not fit an int, proving the 64-bit wrapper reads it whole
		Assert.True(write(0x3000, Large));
		Assert.True(tryRead(0x3000, out long value));
		Assert.Equal(Large, value);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void The_two_scalar_widths_use_independent_storage()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, StandIns);
		GeneratedAssembly assembly = LoadSuite(roslyn);
		WriteInt32Delegate write32 = assembly.Delegate<WriteInt32Delegate>(BindingsType, "WriteInt32");
		TryReadInt64Delegate tryRead64 = assembly.Delegate<TryReadInt64Delegate>(BindingsType, "TryReadInt64");

		Assert.True(write32(0x4000, 1));
		Assert.False(tryRead64(0x4000, out long value)); // never written to the 64-bit table
		Assert.Equal(0, value);
	}

	[Fact]
	public void The_void_throwing_form_calls_the_global()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, StandIns);
		BeepDelegate beep = LoadSuite(roslyn).Delegate<BeepDelegate>(BindingsType, "Beep");

		beep();
		beep();
		beep();

		using LuaFrame frame = new(L);
		Assert.True(L.TryExecute("return beeps"u8, 1, "=test"u8).IsOk);
		Assert.True(L.TryReadInteger(-1, out long beeps));
		Assert.Equal(3, beeps);
	}

	[Fact]
	public void Wrappers_throw_while_the_runtime_is_detached()
	{
		LuaTest.RequireNativeLua();
		LuaRuntime.Detach();
		TryReadInt32Delegate tryRead = LoadSuite(roslyn).Delegate<TryReadInt32Delegate>(BindingsType, "TryReadInt32");

		Assert.Throws<InvalidOperationException>(() => tryRead(0x1000, out _));
	}

	[Fact]
	public void Warm_round_trip_allocates_nothing()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, StandIns);
		GeneratedAssembly assembly = LoadSuite(roslyn);
		WriteInt32Delegate write = assembly.Delegate<WriteInt32Delegate>(BindingsType, "WriteInt32");
		TryReadInt32Delegate tryRead = assembly.Delegate<TryReadInt32Delegate>(BindingsType, "TryReadInt32");
		long sink = 0;

		AllocationGate.AssertZero(() =>
		{
			if (!write(0x5000, 99))
			{
				throw new InvalidOperationException("write failed");
			}

			if (!tryRead(0x5000, out int value) || value != 99)
			{
				throw new InvalidOperationException("wrong value");
			}

			sink += value;
		});

		Assert.NotEqual(0, sink);
		Assert.Equal(0, L.Top);
	}

	private static GeneratedAssembly LoadSuite(RoslynFixture roslyn)
	{
		return GeneratedAssembly.Load(roslyn.Run("end-to-end.cheatengine-sdk-api.txt", SpecSources.EndToEnd));
	}

	// CheatEngine.SDK.Engine.Values.Address here is Infrastructure/Address.cs, not the real CheatEngine.SDK.Engine (see its own doc
	// comment): the public wrapper's address-typed surface (Emit/EngineApiFileEmitter.cs, EmitAddressTypedWrapper).
	private delegate bool TryReadInt32Delegate(Address address, out int value);

	private delegate bool WriteInt32Delegate(Address address, int value);

	private delegate bool TryReadInt64Delegate(Address address, out long value);

	private delegate bool WriteInt64Delegate(Address address, long value);

	private delegate void BeepDelegate();
}
