using CheatEngine.SDK.Abi.Managed;
using CheatEngine.SDK.Hosting.Bootstrap;
using CheatEngine.SDK.Hosting.Diagnostics;
using CheatEngine.SDK.Hosting.Threading;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Hosting.Tests.Support;

/// <summary>
///     What every test shares: a reset of the process-wide host state to "never bootstrapped", a capturing log sink,
///     and the module resolvers that stand in for the loaded-module lookup. The teardown at the end of a test is
///     <see cref="HostSimulator.Dispose" />, which runs while the fixture state is still open.
/// </summary>
internal static unsafe class HostingTest
{
	/// <summary>Returns the host, the runtime and the doubles to their initial state and installs a fresh capturing sink.</summary>
	public static CapturingLogSink Reset()
	{
		// Whatever state a previous test handed out is closed by now: the provider is cleared first, so that a host
		// still enabled here (a test that did not dispose its simulator) abandons its callbacks instead of releasing
		// them on a dangling state. The regular teardown is the simulator's disposal, which releases them properly.
		FakeExports.UseState(null);
		PluginHost.ResetForTests();
		LuaRuntime.Detach();
		MainThreadDispatcher.ResetForTests();
		RecordingPlugin.Reset();
		LuaModuleLocator.Resolver = null;
		HostLog.ResetForTests();
		CapturingLogSink sink = new();
		HostLog.Sink = sink;
		HostLog.MinimumLevel = HostLogLevel.Trace;
		return sink;
	}

	/// <summary>Skips the calling test, with the fixture's reason, when no Lua 5.3 library is available.</summary>
	public static void RequireNativeLua()
	{
		Assert.SkipUnless(NativeLuaLibrary.IsAvailable, NativeLuaLibrary.UnavailableReason);
	}

	/// <summary>Makes the host bind to the fixture's module (whatever its file name) and hand out <paramref name="state" />.</summary>
	public static void UseFixture(NativeLuaState state)
	{
		LuaModuleLocator.Resolver = &ResolveFixtureModule;
		FakeExports.UseState(state.L);
	}

	/// <summary>Makes the module lookup fail, as in a process that has no Lua library at all.</summary>
	public static void UseNoModule()
	{
		LuaModuleLocator.Resolver = &ResolveNoModule;
	}

	/// <summary>Bootstraps <see cref="RecordingPluginFactory" /> into <paramref name="host" /> and asserts success.</summary>
	public static void Bootstrap(HostSimulator host)
	{
		Assert.Equal(1, host.Initialize<RecordingPluginFactory>());
	}

	/// <summary>Bootstraps and enables with the fixture; returns the plugin the host constructed.</summary>
	public static RecordingPlugin Enable(HostSimulator host, NativeLuaState state, uint pluginId = 7)
	{
		UseFixture(state);
		Bootstrap(host);
		ManagedExportedFunctions exports = FakeExports.Create();
		Assert.True(host.CallEnable(&exports, pluginId).IsTrue);
		RecordingPlugin? plugin = RecordingPlugin.LastConstructed;
		Assert.NotNull(plugin);
		return plugin;
	}

	private static nint ResolveFixtureModule()
	{
		return NativeLuaLibrary.Handle;
	}

	private static nint ResolveNoModule()
	{
		return 0;
	}
}
