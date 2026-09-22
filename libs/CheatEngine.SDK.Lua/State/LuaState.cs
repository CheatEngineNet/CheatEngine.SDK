using System;
using System.Globalization;

using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Interop.Api;
using CheatEngine.SDK.Lua.Interop.Types;
using CheatEngine.SDK.Lua.Runtime;

namespace CheatEngine.SDK.Lua.State;

/// <summary>
///     A borrowed view of one <c>lua_State*</c>: the value every operation of this assembly starts from. Pointer-sized,
///     immutable, never allocates, owns nothing.
/// </summary>
/// <remarks>
///     <para>
///         <b>Where it comes from.</b> <see cref="LuaRuntime.AcquireState" /> once per operation, or the state a Lua
///         callback
///         received (<c>new LuaState(handle)</c> on the thunk's argument). Never store one in a static field or a
///         long-lived
///         object: Cheat Engine hands out one Lua thread per OS thread, so a state is only meaningful on the thread and
///         for
///         the operation it was obtained for. Tests and benchmarks wrap a state they own with
///         <see cref="LuaState(nint)" />.
///     </para>
///     <para>
///         <b>Three families of members.</b>
///         <i>Raw</i> members (stack, type tests, scalar pushes, reads and nonallocating table access) map to one C API
///         call each and never run Lua code. Allocating operations use the tiny native protection bridge, which puts both
///         the operation and <c>lua_pcallk</c> below the managed stack before Lua can <c>longjmp</c>.
///         <i>Protected</i> members (<c>Try*</c> returning <see cref="LuaStatus" />) cover everything that can run a
///         metamethod or raise; they always go through <c>lua_pcallk</c> and report failure as a status with the error
///         value
///         on the stack. There is deliberately no unprotected <c>GetField</c>, <c>SetField</c> or <c>Call</c>.
///         <i>Reference</i> members create and push <see cref="References.LuaRef" /> handles in an SDK-private table.
///     </para>
///     <para>
///         <b>Indices</b> follow the C API: positive from the bottom, negative from the top, pseudo-indices for the
///         registry
///         and upvalues. As in C, an index that is not acceptable, or a value of the wrong kind where a member states a
///         precondition, is undefined behaviour inside the native library; Debug builds assert the preconditions that are
///         cheap to check.
///     </para>
///     <para>
///         <b>Thread affinity.</b> Use a state only on the thread it was acquired on. The type itself is a value and can
///         be
///         copied freely.
///     </para>
/// </remarks>
public readonly unsafe partial struct LuaState : IEquatable<LuaState>
{
	/// <summary>Value for the result count of <see cref="TryCall(int, int)" />: keep every result (<c>LUA_MULTRET</c>).</summary>
	public const int MultipleResults = LuaApi.LUA_MULTRET;

	/// <summary>
	///     Slots that are free when Lua enters a C function (<c>LUA_MINSTACK</c>). Beyond that, call
	///     <see cref="TryEnsureStack" />.
	/// </summary>
	public const int MinimumFreeSlots = LuaApi.LUA_MINSTACK;

	/// <summary>Pseudo-index of the registry (<c>LUA_REGISTRYINDEX</c>), usable with the <c>Raw*</c> table members.</summary>
	public const int RegistryIndex = LuaApi.LUA_REGISTRYINDEX;

	/// <summary>Wraps a native <c>lua_State*</c> given as an integer handle.</summary>
	/// <param name="handle">
	///     The address of a live Lua state: the argument of a <c>lua_CFunction</c> thunk, or a state owned by a test.
	///     It is not validated; zero gives the <see cref="IsNull" /> view, on which no member may be called.
	/// </param>
	/// <remarks>
	///     Constructing a view is a pure value operation; it is the members that need
	///     <c>CheatEngine.SDK.Lua.Interop.Api.LuaApi</c> to be bound.
	/// </remarks>
	public LuaState(nint handle)
	{
		Pointer = (lua_State*) handle;
	}

	internal LuaState(lua_State* pointer)
	{
		Pointer = pointer;
	}

	/// <summary>
	///     Gets the address of the native state, for code that talks to <c>CheatEngine.SDK.Lua.Interop</c> directly.
	/// </summary>
	public nint Handle => (nint) Pointer;

	/// <summary>Gets a value indicating whether this is the default view over no state.</summary>
	public bool IsNull => Pointer is null;

	internal lua_State* Pointer
	{
		get;
	}

	/// <summary>Compares two views for identity of the native state.</summary>
	/// <param name="left">First view.</param>
	/// <param name="right">Second view.</param>
	/// <returns><see langword="true" /> when both refer to the same <c>lua_State*</c>.</returns>
	public static bool operator ==(LuaState left, LuaState right)
	{
		return left.Pointer == right.Pointer;
	}

	/// <summary>Compares two views for identity of the native state.</summary>
	/// <param name="left">First view.</param>
	/// <param name="right">Second view.</param>
	/// <returns><see langword="true" /> when they refer to different states.</returns>
	public static bool operator !=(LuaState left, LuaState right)
	{
		return left.Pointer != right.Pointer;
	}

	/// <inheritdoc />
	public bool Equals(LuaState other)
	{
		return Pointer == other.Pointer;
	}

	/// <inheritdoc />
	public override bool Equals(object? obj)
	{
		return obj is LuaState other && Equals(other);
	}

	/// <inheritdoc />
	public override int GetHashCode()
	{
		return ((nint) Pointer).GetHashCode();
	}

	/// <summary>Formats the address of the native state, for diagnostics.</summary>
	/// <returns><c>lua_State@0x...</c>.</returns>
	public override string ToString()
	{
		return "lua_State@0x" + ((nint) Pointer).ToString("X", CultureInfo.InvariantCulture);
	}

	// A relative index keeps designating the same slot after 'pushed' more values were pushed above it.
	// Absolute indices and pseudo-indices (registry, upvalues) do not move.
	internal static int Shift(int index, int pushed)
	{
		return index is < 0 and > LuaApi.LUA_REGISTRYINDEX ? index - pushed : index;
	}
}
