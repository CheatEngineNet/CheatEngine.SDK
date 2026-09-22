using System;
using System.Collections.Generic;
using System.Threading;

using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Lua.Callbacks;

/// <summary>
///     The lifecycle-owned, LIFO registry for host subscriptions. It is internal infrastructure for a future qualified
///     mapping, not evidence that CE timer or hotkey registration is available to SDK consumers.
/// </summary>
internal static class LuaHostSubscriptionRegistry
{
	private static LuaHostSubscription? s_head;
	private static bool s_acceptRegistrations;

	// Deterministic pre-disable race seam used only by the SDK's friend test assembly. It runs after registrations are
	// closed and before the registry waits for callbacks that were already admitted.
	internal static Action? CallbackAdmissionClosedForTesting;

	internal static Lock Gate
	{
		get;
	} = new();

	internal static int Count
	{
		get
		{
			lock (Gate)
			{
				int count = 0;
				for (LuaHostSubscription? current = s_head; current is not null; current = current.Next)
				{
					count++;
				}

				return count;
			}
		}
	}

	internal static bool TryAdd(LuaHostSubscription subscription)
	{
		lock (Gate)
		{
			if (!s_acceptRegistrations
			    || !LuaRuntime.IsAttached
			    || LuaRuntime.CurrentStateIdentity != subscription.Identity)
			{
				return false;
			}

			subscription.Next = s_head;
			s_head?.Previous = subscription;
			s_head = subscription;
			subscription.IsLinked = true;
			subscription.Publish();
			return true;
		}
	}

	internal static void Remove(LuaHostSubscription subscription)
	{
		lock (Gate)
		{
			if (!subscription.IsLinked)
			{
				return;
			}

			if (subscription.Previous is null)
			{
				s_head = subscription.Next;
			}
			else
			{
				subscription.Previous.Next = subscription.Next;
			}

			subscription.Next?.Previous = subscription.Previous;
			subscription.Next = null;
			subscription.Previous = null;
			subscription.IsLinked = false;
		}
	}

	/// <summary>
	///     Closes registration and callback admission before plugin <c>OnDisable</c> destroys plugin-managed state. Host
	///     objects remain linked until the later state-owning lifecycle cleanup can unregister them.
	/// </summary>
	internal static void CloseCallbackAdmissionAndDrain()
	{
		List<LuaHostSubscription> subscriptions = [];
		lock (Gate)
		{
			s_acceptRegistrations = false;
			for (LuaHostSubscription? current = s_head; current is not null; current = current.Next)
			{
				subscriptions.Add(current);
			}
		}

		Volatile.Read(ref CallbackAdmissionClosedForTesting)?.Invoke();

		// A callback admitted before the boundary can dispose another owner or attempt a rejected registration. Neither
		// action may wait behind this drain, so retain only a linked-owner snapshot while holding Gate and wait outside it.
		foreach (LuaHostSubscription subscription in subscriptions)
		{
			subscription.CloseCallbackAdmissionAndDrain();
		}
	}

	/// <summary>
	///     Closes admission and makes one LIFO unregister attempt for every owner while <paramref name="state" /> is
	///     valid.
	/// </summary>
	internal static void DetachAll(LuaState state)
	{
		lock (Gate)
		{
			s_acceptRegistrations = false;
		}

		while (TryTakeHead(out LuaHostSubscription subscription))
		{
			subscription.ReleaseFromLifecycle(state);
		}
	}

	internal static void OpenRegistrationAdmission()
	{
		lock (Gate)
		{
			s_acceptRegistrations = true;
		}
	}

	private static bool TryTakeHead(out LuaHostSubscription subscription)
	{
		lock (Gate)
		{
			if (s_head is null)
			{
				subscription = null!;
				return false;
			}

			subscription = s_head;
			s_head = subscription.Next;
			s_head?.Previous = null;
			subscription.Next = null;
			subscription.Previous = null;
			subscription.IsLinked = false;
			return true;
		}
	}
}
