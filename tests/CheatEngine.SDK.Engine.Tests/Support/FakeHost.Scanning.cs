using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using CheatEngine.SDK.Lua.Callbacks;
using CheatEngine.SDK.Lua.Interop.Types;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Tests.Support;

// The scanning part of the fake host: a Lua-callable function that runs managed test code, so that a Lua stand-in
// can model Cheat Engine running queued main-thread work (CE's synchronize, which MainThread.Invoke uses) from inside
// one of its own calls, for example while waitTillDone pumps CheckSynchronize.
internal static unsafe partial class FakeHost
{
	private static ManagedHookScope? s_activeManagedHook;

	/// <summary>
	///     Installs the Lua global <c>managed_hook</c>, a bare C function that runs <paramref name="hook" /> and returns no
	///     value. A stand-in calls it from inside a CE-shaped call, for example with
	///     <c>scan_wait_hook = managed_hook</c>. An exception thrown by the hook never crosses the native boundary: it is
	///     recorded in <see cref="ManagedHookScope.Failure" />.
	/// </summary>
	public static ManagedHookScope InstallManagedHook(LuaState L, Action hook)
	{
		ArgumentNullException.ThrowIfNull(hook);
		if (s_activeManagedHook is not null)
		{
			throw new InvalidOperationException("Only one fake-host managed hook can be active.");
		}

		using LuaFrame frame = new(L);
		L.PushUncheckedFunction(new LuaNativeFunction((nint) (delegate* unmanaged[Cdecl]<lua_State*, int>) &RunManagedHook));
		Assert.True(L.TrySetGlobal("managed_hook"u8).IsOk);
		ManagedHookScope scope = new(hook);
		s_activeManagedHook = scope;
		return scope;
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
	private static int RunManagedHook(lua_State* state)
	{
		s_activeManagedHook?.Run();
		return 0;
	}

	/// <summary>The active managed hook; disposing it makes <c>managed_hook</c> a no-op.</summary>
	internal sealed class ManagedHookScope : IDisposable
	{
		private readonly Action _hook;

		internal ManagedHookScope(Action hook)
		{
			_hook = hook;
		}

		/// <summary>Gets how many times Lua called <c>managed_hook</c>.</summary>
		public int CallCount
		{
			get;
			private set;
		}

		/// <summary>Gets the first exception the hook threw, if any.</summary>
		public Exception? Failure
		{
			get;
			private set;
		}

		/// <inheritdoc />
		public void Dispose()
		{
			if (s_activeManagedHook == this)
			{
				s_activeManagedHook = null;
			}
		}

		internal void Run()
		{
			CallCount++;
			try
			{
				_hook();
			}
			catch (Exception exception)
			{
				Failure ??= exception;
			}
		}
	}
}
