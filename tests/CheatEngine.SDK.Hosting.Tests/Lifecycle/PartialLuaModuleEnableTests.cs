using CheatEngine.SDK.Abi;
using CheatEngine.SDK.Abi.Managed;
using CheatEngine.SDK.Hosting.Bootstrap;
using CheatEngine.SDK.Hosting.Tests.Support;
using CheatEngine.SDK.Lua.Interop.Api;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Hosting.Tests.Lifecycle;

/// <summary>
///     Q11 through the whole enable path: the module resolver seam hands the host a copy of the Lua fixture that lacks
///     one export (<see cref="PartialLuaModule" />). The enable must fail before any plugin code and before the host
///     asks for its Lua state, and the log must name the missing export. The copy is loaded in this test process only.
/// </summary>
public sealed unsafe class PartialLuaModuleEnableTests
{
	private static nint s_partialModule;

	[Fact]
	[Trait("Category", "NativeLua")]
	[Trait("Qualification", "Q11")]
	public void Enable_with_a_partial_lua_module_fails_before_any_plugin_code_and_names_the_missing_export()
	{
		HostingTest.RequireNativeLua();
		CapturingLogSink sink = HostingTest.Reset();
		using PartialLuaModule module = PartialLuaModule.Load(NativeLuaLibrary.LibraryPath!);
		nint boundBefore = LuaApi.ModuleHandle;
		s_partialModule = module.Handle;
		try
		{
			LuaModuleLocator.Resolver = &ResolvePartialModule;
			using HostSimulator host = new();
			HostingTest.Bootstrap(host);
			ManagedExportedFunctions exports = FakeExports.Create();

			Bool32 result = host.CallEnable(&exports, 1);

			Assert.False(result.IsTrue);
			Assert.False(PluginHost.IsEnabled);
			Assert.False(LuaRuntime.IsAttached);
			Assert.Equal(0, RecordingPlugin.ConstructorCalls);
			Assert.Equal(0, FakeExports.GetLuaStateCalls);
			Assert.NotEmpty(sink.Errors(PartialLuaModule.RemovedExport));
			Assert.Equal(boundBefore, LuaApi.ModuleHandle);
		}
		finally
		{
			LuaModuleLocator.Resolver = null;
			s_partialModule = 0;
		}
	}

	private static nint ResolvePartialModule()
	{
		return s_partialModule;
	}
}
