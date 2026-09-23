using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;

using CheatEngine.SDK.Lua.Interop.Api;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Lua.References;

/// <summary>
///     A reference to a Lua value held in an SDK-private registry table, stamped with the
///     <see cref="LuaRuntime.CurrentStateIdentity" /> it was created in. The way this SDK keeps a Lua value alive and
///     reachable across calls without leaving it on the stack: cached global functions, host objects owned by managed
///     code, bound method closures.
/// </summary>
/// <remarks>
///     <para>
///         <b>Identity invalidation.</b> The registry belongs to one Lua state. When the host detaches and re-attaches,
///         its attach epoch changes; when the SDK prepares a supported in-place state replacement, its state generation
///         changes. In either case every older reference is <i>stale</i>: <see cref="IsCurrent" /> is
///         <see langword="false" />,
///         <see cref="LuaState.TryPushRef" />
///         pushes nothing, and releasing it does nothing, because its slot number may now designate another value in
///         another registry. Code that caches a reference re-resolves it when it finds it stale.
///     </para>
///     <para>
///         <b>Ownership.</b> The holder of a <see cref="LuaRef" /> owns one registry slot and releases it explicitly with
///         <see cref="Release" /> (or <see cref="Dispose" />, which acquires the ambient state); there is no finalizer, a
///         forgotten reference keeps its value alive until the state dies. Releasing is idempotent and thread-agnostic as
///         far
///         as this type is concerned; the state passed to it follows the usual rule (the calling thread's state).
///     </para>
///     <para>
///         <b>Representation.</b> Slot number and complete identity are published together in one immutable binding, so a
///         reader on another thread never sees a slot from one state paired with a stamp from another. Unresolved and
///         released references hold <c>LUA_NOREF</c>. Pushing and releasing use the same gate; creating one allocates
///         this object and a registry slot, which is why references are created once and reused, never per call.
///     </para>
/// </remarks>
public sealed class LuaRef : IDisposable
{
	private const int NoReference = LuaApi.LUA_NOREF;

	// A reference write publishes slot and both identity components together. The hot path only reads the immutable
	// object; rebinding and releasing are already cold, serialized paths.
	private LuaRefBinding? _binding;

	/// <summary>
	///     Creates an unresolved reference: <see cref="IsResolved" /> is <see langword="false" /> until code binds it
	///     (see <c>CheatEngine.SDK.Lua.CompilerServices.LuaGlobalFunctions</c>).
	/// </summary>
	/// <remarks>Runs no Lua code, so it may be a static field initializer of a class that binds globals lazily.</remarks>
	public LuaRef()
	{
		_binding = null;
	}

	internal LuaRef(int reference, LuaStateIdentity identity)
	{
		_binding = new LuaRefBinding(reference, identity);
	}

	/// <summary>
	///     Gets the slot in the SDK's private reference table, or <c>LUA_NOREF</c> (-2) when unresolved or released.
	///     <c>LUA_REFNIL</c> (-1) is a
	///     valid reference to <c>nil</c>.
	/// </summary>
	public int Reference => Volatile.Read(ref _binding)?.Reference ?? NoReference;

	/// <summary>
	///     Gets the complete Lua state identity the reference was created in, or the default identity for an unresolved
	///     or released reference.
	/// </summary>
	public LuaStateIdentity Identity => Volatile.Read(ref _binding)?.Identity ?? default;

	/// <summary>
	///     Gets the attach epoch component of <see cref="Identity" />; 0 for an unresolved or released reference.
	/// </summary>
	public int Epoch => Identity.AttachEpoch;

	/// <summary>
	///     Gets the state generation component of <see cref="Identity" />; 0 for an unresolved or released reference.
	/// </summary>
	public int StateGeneration => Identity.StateGeneration;

	/// <summary>
	///     Gets a value indicating whether the reference holds a slot at all (resolved and not released), whatever its
	///     identity.
	/// </summary>
	public bool IsResolved => Reference != NoReference;

	/// <summary>
	///     Gets a value indicating whether the reference holds a slot created in the current
	///     <see cref="LuaRuntime.CurrentStateIdentity" />: the only state in which it may be pushed or released.
	/// </summary>
	public bool IsCurrent
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => TryGetCurrent(out _);
	}

	/// <summary>
	///     <see cref="Release" /> with the state acquired from <see cref="LuaRuntime" />. When the runtime is detached the
	///     slot cannot be reached and the reference is only marked released. Prefer <see cref="Release" /> where a state is at
	///     hand.
	/// </summary>
	public void Dispose()
	{
		if (!LuaRuntime.TryAcquireOperation(out LuaRuntimeOperation operation))
		{
			Release(default);
			return;
		}

		using (operation)
		{
			Release(operation.State);
		}
	}

	/// <summary>Reads the slot when the reference is current. One binding read and one identity comparison.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal bool TryGetCurrent(out int reference)
	{
		LuaRefBinding? binding = Volatile.Read(ref _binding);
		reference = binding?.Reference ?? NoReference;
		return binding is not null && reference != NoReference && binding.Identity == LuaRuntime.CurrentStateIdentity;
	}

	/// <summary>
	///     Points the reference at a slot of the given state identity. The previous slot, if any, is not released: the
	///     caller decides whether it still belongs to a live state.
	/// </summary>
	internal void Rebind(int reference, LuaStateIdentity identity)
	{
		lock (LuaReferences.Gate)
		{
			Volatile.Write(ref _binding, new LuaRefBinding(reference, identity));
		}
	}

	// Temporary compatibility for call sites being migrated to the complete identity. New code must capture and
	// revalidate CurrentStateIdentity around any operation that can run Lua before using the overload above.
	internal void Rebind(int reference, int epoch)
	{
		Rebind(reference, new LuaStateIdentity(epoch, LuaRuntime.StateGeneration));
	}

	/// <summary>
	///     Releases the registry slot (<c>luaL_unref</c>) when the reference is current, and marks the reference released
	///     in every case. Safe to call on an unresolved, stale or already released reference: nothing happens then.
	/// </summary>
	/// <param name="state">A state of the Lua universe the reference was created in; the calling thread's state.</param>
	public void Release(LuaState state)
	{
		LuaRuntimeOperation operation = state.IsNull ? default : LuaRuntime.EnterStateOperation(state);
		try
		{
			lock (LuaReferences.Gate)
			{
				LuaRefBinding? binding = Interlocked.Exchange(ref _binding, null);
				if (binding is not null && binding.Reference != NoReference &&
					binding.Identity == LuaRuntime.CurrentStateIdentity &&
					!state.IsNull)
				{
					LuaReferences.Release(state, binding.Reference);
				}
			}
		}
		finally
		{
			operation.Dispose();
		}
	}

	/// <summary><c>LuaRef(slot, attach epoch N, state generation M)</c>, or <c>LuaRef(unresolved)</c>.</summary>
	public override string ToString()
	{
		LuaRefBinding? binding = Volatile.Read(ref _binding);
		return binding is null || binding.Reference == NoReference
			? "LuaRef(unresolved)"
			: string.Create(CultureInfo.InvariantCulture,
				$"LuaRef({binding.Reference}, attach epoch {binding.Identity.AttachEpoch}, state generation {binding.Identity.StateGeneration})");
	}

	private sealed class LuaRefBinding(int reference, LuaStateIdentity identity)
	{
		internal int Reference
		{
			get;
		} = reference;

		internal LuaStateIdentity Identity
		{
			get;
		} = identity;
	}
}
