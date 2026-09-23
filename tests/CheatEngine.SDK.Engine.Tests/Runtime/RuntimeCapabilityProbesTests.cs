using System.Text;

using CheatEngine.SDK.Engine.Processes;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Engine.Tests.Runtime;

/// <summary>
///     End-to-end behaviour of the wired <c>runtime-capabilities</c> EngineApi spec: spec file, generator and the
///     compiled <see cref="RuntimeCapabilityProbes" /> in <c>CheatEngine.SDK.Engine.dll</c>, called against the spike C3
///     stand-ins on the bundled Lua 5.3 library (C2 fixture; not a host run).
/// </summary>
[Trait("Category", "NativeLua")]
public sealed class RuntimeCapabilityProbesTests
{
	[Fact]
	public void generated_runtime_capability_probes_return_the_spike_x64_facts_and_restore_the_stack()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		FakeHost.InstallCe77HostFacts(scope.State);
		FakeHost.InstallCe77X64TargetFacts(scope.State, 45052);
		EngineTest.Run(scope.State, "function getCEVersion() return 7.7 end"u8);

		Assert.Equal(7.7d, RuntimeCapabilityProbes.GetCheatEngineVersion());
		Assert.Equal(1, RuntimeCapabilityProbes.GetSystemArchitectureCode());
		Assert.Equal(0, RuntimeCapabilityProbes.GetTargetAbiCode());
		Assert.True(RuntimeCapabilityProbes.IsCheatEngine64Bit());
		Assert.True(RuntimeCapabilityProbes.IsTarget64Bit());
		Assert.True(RuntimeCapabilityProbes.IsTargetX86());
		Assert.False(RuntimeCapabilityProbes.IsTargetArm());
		Assert.False(RuntimeCapabilityProbes.IsTargetAndroid());
		Assert.Equal(8, RuntimeCapabilityProbes.GetConfiguredPointerSizeBytes());
		Assert.Equal(0, RuntimeCapabilityProbes.GetOperatingSystemCode());
		Assert.False(RuntimeCapabilityProbes.IsConnectedToCEServer());
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	public void generated_configured_pointer_size_probe_returns_the_raw_integer()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		FakeHost.InstallCe77X64TargetFacts(scope.State, 45052);
		// Spike C3 D3b: setPointerSize(2) was accepted and read back.
		EngineTest.Run(scope.State, "rt_pointer_size = 2"u8);

		Assert.Equal(2, RuntimeCapabilityProbes.GetConfiguredPointerSizeBytes());
		Assert.True(RuntimeCapabilityProbes.IsTarget64Bit());
		Assert.Equal(0, scope.State.Top);
	}

	[Theory]
	[InlineData("getPointerSize")]
	[InlineData("isConnectedToCEServer")]
	[InlineData("targetIsAndroid")]
	[InlineData("getOperatingSystem")]
	public void generated_probe_on_an_absent_global_fails_as_the_emitter_documents(string global)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);

		LuaException failure = Assert.Throws<LuaException>(() => Call(global));

		Assert.Contains(global, failure.Message, StringComparison.Ordinal);
		Assert.Equal(0, scope.State.Top);
	}

	[Theory]
	[InlineData("getPointerSize", "return 'eight'")]
	[InlineData("getPointerSize", "return nil")]
	[InlineData("isConnectedToCEServer", "return nil")]
	[InlineData("targetIsAndroid", "return 0")]
	[InlineData("getOperatingSystem", "error('fixture failure')")]
	public void generated_probe_raises_a_lua_exception_for_a_wrong_type_or_a_raising_global_and_recovers(string global,
		string body)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		FakeHost.InstallCe77HostFacts(scope.State);
		FakeHost.InstallCe77X64TargetFacts(scope.State, 45052);
		EngineTest.Run(scope.State, Encoding.UTF8.GetBytes("function " + global + "() " + body + " end"));

		Assert.Throws<LuaException>(() => Call(global));
		Assert.Equal(0, scope.State.Top);
		// The next generated call on the same state still works.
		Assert.True(RuntimeCapabilityProbes.IsTarget64Bit());
		Assert.Equal(0, scope.State.Top);
	}

	[Theory]
	[InlineData("4.0")]
	[InlineData("'4'")]
	public void generated_int32_probe_converts_an_integral_float_or_numeral_string_that_the_structured_api_refuses(
		string luaValue)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		FakeHost.InstallCe77X64TargetFacts(scope.State, 45052);
		EngineTest.Run(scope.State, Encoding.UTF8.GetBytes("rt_pointer_size = " + luaValue));

		// The generated int32 wrapper reads through Int32Marshaller (lua_tointegerx), which converts an integral float
		// or an integer numeral string; the spec header documents that policy.
		Assert.Equal(4, RuntimeCapabilityProbes.GetConfiguredPointerSizeBytes());
		Assert.Equal(0, scope.State.Top);

		// The structured API requires the Lua integer subtype that Cheat Engine pushes and refuses the same value.
		ProcessOperationStatus status =
			RuntimeProcessOperations.TryGetConfiguredPointerSize(out int rawBytes, out PointerSize pointerSize);
		Assert.Equal(ProcessOperationStatusKind.InvalidResult, status.Kind);
		Assert.Equal(0, rawBytes);
		Assert.Equal(PointerSize.Unknown, pointerSize);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	public void generated_probes_throw_the_lifecycle_failure_while_no_runtime_is_attached()
	{
		LuaRuntime.Detach();

		Assert.Throws<InvalidOperationException>(() => RuntimeCapabilityProbes.GetConfiguredPointerSizeBytes());
		Assert.Throws<InvalidOperationException>(() => RuntimeCapabilityProbes.IsConnectedToCEServer());
	}

	private static object Call(string global)
	{
		return global switch
		{
			"getPointerSize" => RuntimeCapabilityProbes.GetConfiguredPointerSizeBytes(),
			"isConnectedToCEServer" => RuntimeCapabilityProbes.IsConnectedToCEServer(),
			"targetIsAndroid" => RuntimeCapabilityProbes.IsTargetAndroid(),
			"getOperatingSystem" => RuntimeCapabilityProbes.GetOperatingSystemCode(),
			_ => throw new ArgumentOutOfRangeException(nameof(global), global, "No generated probe for this global.")
		};
	}
}
