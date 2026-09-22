using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using CheatEngine.SDK.Abi;
using CheatEngine.SDK.Hosting.Bootstrap;
using CheatEngine.SDK.Hosting.Context;
using CheatEngine.SDK.Hosting.Plugin;
using CheatEngine.SDK.Hosting.Threading;
using CheatEngine.SDK.Lua.Callbacks;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Hosting.Tests.Support;

/// <summary>
///     The plugin under test. Records what it observes at each lifecycle point, throws where a test told it to, and
///     makes a nested lifecycle call from inside <see cref="OnEnable" /> or <see cref="OnDisable" /> when a test installs
///     one (static switches, because the host constructs the instance itself).
/// </summary>
internal sealed unsafe class RecordingPlugin : CheatEnginePlugin
{
	public RecordingPlugin()
	{
		ConstructorCalls++;
		RuntimeAttachedInConstructor = LuaRuntime.IsAttached;
		HostEnabledInConstructor = PluginHost.IsEnabled;
		LastConstructed = this;
		if (ThrowInConstructor)
		{
			throw new InvalidOperationException("constructor failure requested by the test");
		}
	}

	public static bool ThrowInConstructor
	{
		get;
		set;
	}

	public static bool ThrowInOnEnable
	{
		get;
		set;
	}

	public static bool CreateCallbacksInOnEnable
	{
		get;
		set;
	}

	public static bool ThrowInOnDisable
	{
		get;
		set;
	}

	/// <summary>Signals after <see cref="OnEnable" /> has attached and observed the lifecycle state; null for none.</summary>
	public static ManualResetEventSlim? OnEnableEntered
	{
		get;
		set;
	}

	/// <summary>Blocks <see cref="OnEnable" /> after <see cref="OnEnableEntered" /> was signalled; null for none.</summary>
	public static ManualResetEventSlim? ContinueOnEnable
	{
		get;
		set;
	}

	/// <summary>
	///     A lifecycle call to make from inside <see cref="OnEnable" />, as a host re-entering through the message pump
	///     would; null for none. Runs once, then clears itself.
	/// </summary>
	public static Func<Bool32>? NestedCallInOnEnable
	{
		get;
		set;
	}

	/// <summary>A lifecycle call to make from inside <see cref="OnDisable" />; null for none. Runs once, then clears itself.</summary>
	public static Func<Bool32>? NestedCallInOnDisable
	{
		get;
		set;
	}

	public static int ConstructorCalls
	{
		get;
		private set;
	}

	public static bool RuntimeAttachedInConstructor
	{
		get;
		private set;
	}

	public static bool HostEnabledInConstructor
	{
		get;
		private set;
	}

	public static RecordingPlugin? LastConstructed
	{
		get;
		private set;
	}

	public int EnableCalls
	{
		get;
		private set;
	}

	public int DisableCalls
	{
		get;
		private set;
	}

	public int EnableThreadId
	{
		get;
		private set;
	}

	public bool RuntimeAttachedInOnEnable
	{
		get;
		private set;
	}

	public bool HostEnabledInOnEnable
	{
		get;
		private set;
	}

	public bool MainThreadInOnEnable
	{
		get;
		private set;
	}

	public PluginContext? ContextInOnEnable
	{
		get;
		private set;
	}

	public long LuaResultInOnEnable
	{
		get;
		private set;
	}

	public bool RuntimeAttachedInOnDisable
	{
		get;
		private set;
	}

	public bool HostEnabledInOnDisable
	{
		get;
		private set;
	}

	public LuaCallback<object>? CallbackOne
	{
		get;
		private set;
	}

	public LuaCallback<object>? CallbackTwo
	{
		get;
		private set;
	}

	/// <summary>What the nested call installed in <see cref="NestedCallInOnEnable" /> returned; null when none ran.</summary>
	public Bool32? NestedResultInOnEnable
	{
		get;
		private set;
	}

	/// <summary>What the nested call installed in <see cref="NestedCallInOnDisable" /> returned; null when none ran.</summary>
	public Bool32? NestedResultInOnDisable
	{
		get;
		private set;
	}

	/// <summary><see cref="PluginHost.IsEnabled" /> right after the nested call returned, inside the outer callback.</summary>
	public bool HostEnabledAfterNestedCall
	{
		get;
		private set;
	}

	/// <summary><see cref="LuaRuntime.IsAttached" /> right after the nested call returned, inside the outer callback.</summary>
	public bool RuntimeAttachedAfterNestedCall
	{
		get;
		private set;
	}

	public static void Reset()
	{
		ThrowInConstructor = false;
		ThrowInOnEnable = false;
		CreateCallbacksInOnEnable = false;
		ThrowInOnDisable = false;
		OnEnableEntered = null;
		ContinueOnEnable = null;
		NestedCallInOnEnable = null;
		NestedCallInOnDisable = null;
		ConstructorCalls = 0;
		RuntimeAttachedInConstructor = false;
		HostEnabledInConstructor = false;
		LastConstructed = null;
	}

	protected internal override void OnEnable()
	{
		EnableCalls++;
		EnableThreadId = Environment.CurrentManagedThreadId;
		RuntimeAttachedInOnEnable = LuaRuntime.IsAttached;
		HostEnabledInOnEnable = PluginHost.IsEnabled;
		MainThreadInOnEnable = MainThread.IsMainThread;
		ContextInOnEnable = Context;
		OnEnableEntered?.Set();
		ContinueOnEnable?.Wait(Context.ShutdownToken);

		// The Lua layer must be usable here: run a chunk on the state the host hands out.
		LuaState L = LuaRuntime.AcquireState();
		using (LuaFrame frame = new(L))
		{
			if (L.TryExecute("return 40 + 2"u8, 1).IsOk && L.TryReadInteger(-1, out long value))
			{
				LuaResultInOnEnable = value;
			}
		}

		if (CreateCallbacksInOnEnable)
		{
			if (!LuaCallback.TryCreate(L, new LuaNativeFunction(&NoOpThunk), new object(),
					out LuaCallback<object>? first).IsOk
				|| first is null)
			{
				throw new InvalidOperationException("first callback creation failed");
			}

			if (!LuaCallback.TryCreate(L, new LuaNativeFunction(&NoOpThunk), new object(),
					out LuaCallback<object>? second).IsOk
				|| second is null)
			{
				throw new InvalidOperationException("second callback creation failed");
			}

			CallbackOne = first;
			CallbackTwo = second;
		}

		Func<Bool32>? nested = NestedCallInOnEnable;
		if (nested is not null)
		{
			NestedCallInOnEnable = null;
			NestedResultInOnEnable = nested();
			HostEnabledAfterNestedCall = PluginHost.IsEnabled;
			RuntimeAttachedAfterNestedCall = LuaRuntime.IsAttached;
		}

		if (ThrowInOnEnable)
		{
			throw new InvalidOperationException("OnEnable failure requested by the test");
		}
	}

	protected internal override void OnDisable()
	{
		DisableCalls++;
		RuntimeAttachedInOnDisable = LuaRuntime.IsAttached;
		HostEnabledInOnDisable = PluginHost.IsEnabled;

		Func<Bool32>? nested = NestedCallInOnDisable;
		if (nested is not null)
		{
			NestedCallInOnDisable = null;
			NestedResultInOnDisable = nested();
			HostEnabledAfterNestedCall = PluginHost.IsEnabled;
			RuntimeAttachedAfterNestedCall = LuaRuntime.IsAttached;
		}

		if (ThrowInOnDisable)
		{
			throw new InvalidOperationException("OnDisable failure requested by the test");
		}
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
	private static int NoOpThunk(nint handle)
	{
		return 0;
	}
}
