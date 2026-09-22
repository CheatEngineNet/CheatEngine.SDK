using System;
using System.Threading;

using CheatEngine.SDK.Lua.Interop.Types;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Lua.Callbacks;

/// <summary>
///     The set of live <see cref="LuaCallback" />s of this load context, as an intrusive doubly linked list, so that
///     <see cref="LuaRuntime.Detach" /> can neutralize every closure and free every handle while the host's state can
///     still be reached. Adding and removing allocate nothing beyond the callback itself.
/// </summary>
internal static class LuaCallbackRegistry
{
	private static LuaCallback? s_head;

	// Deterministic cleanup-failure seam used only by the SDK's friend test assembly. It runs after a callback was
	// fully released, so a thrown test exception leaves the remaining callbacks linked for a retry.
	internal static Action? AfterReleaseForTesting;

	/// <summary>Serializes callback list changes and handle release.</summary>
	internal static Lock Gate
	{
		get;
	} = new();

	/// <summary>Number of live callbacks; for tests and diagnostics.</summary>
	internal static int Count
	{
		get
		{
			lock (Gate)
			{
				int count = 0;
				for (LuaCallback? current = s_head; current is not null; current = current.Next)
				{
					count++;
				}

				return count;
			}
		}
	}

	internal static void Add(LuaCallback callback)
	{
		lock (Gate)
		{
			callback.Next = s_head;
			s_head?.Previous = callback;

			s_head = callback;
			callback.IsLinked = true;
		}
	}

	/// <summary>
	///     Unlinks <paramref name="callback" />, or does nothing when it is not linked. Takes the gate itself, and
	///     <see cref="Lock" /> is reentrant, so a caller that already holds it pays next to nothing.
	/// </summary>
	internal static void Remove(LuaCallback callback)
	{
		lock (Gate)
		{
			if (!callback.IsLinked)
			{
				return;
			}

			if (callback.Previous is null)
			{
				s_head = callback.Next;
			}
			else
			{
				callback.Previous.Next = callback.Next;
			}

			callback.Next?.Previous = callback.Previous;

			callback.Next = null;
			callback.Previous = null;
			callback.IsLinked = false;
		}
	}

	/// <summary>
	///     Releases every live callback with a state acquired from <paramref name="services" /> on the calling thread. When
	///     the provider yields no state the closures cannot be neutralized, and the callbacks are abandoned instead:
	///     marked released with their managed state kept alive, which leaks but cannot crash.
	/// </summary>
	internal static unsafe void DetachAll(LuaHostServices services)
	{
		lock (Gate)
		{
			if (s_head is null)
			{
				return;
			}

			lua_State* l = services.Provider();
			LuaState state = new(l);
			// Release unlinks the head it is called on, so s_head is re-read on every iteration and the loop
			// ends when the list is empty. Keep the explicit re-read: a "condition is always true" IDE quick-fix once
			// turned this loop into while (true), which ended every Detach with a NullReferenceException.
			for (LuaCallback? head = s_head; head is not null; head = s_head)
			{
				head.Release(state);
				Volatile.Read(ref AfterReleaseForTesting)?.Invoke();
			}
		}
	}
}
