using System;
using System.Runtime.InteropServices;

namespace CheatEngine.SDK.Lua.Runtime;

/// <summary>
///     Identifies one usable Lua resource universe within this SDK load context: the host attachment that established it
///     and the generation of the Lua state inside that attachment.
/// </summary>
/// <remarks>
///     A matching attach epoch alone is insufficient after the host replaces its Lua state in place: registry slots,
///     callbacks and helper tables then belong to the old state. Persistent SDK Lua resources capture this complete
///     value and compare it before use or cleanup. Values are supplied by <see cref="LuaRuntime.CurrentStateIdentity" />;
///     callers do not manufacture an identity.
///     This identity deliberately contains no <c>lua_State*</c>: Cheat Engine can return distinct per-thread coroutine
///     pointers from one underlying Lua VM, whose registry and heap remain shared. A pointer identifies only the stack
///     borrowed by one synchronous operation; the attachment and generation identify the resource universe that owns
///     persistent SDK references and callbacks.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public readonly struct LuaStateIdentity : IEquatable<LuaStateIdentity>
{
	internal LuaStateIdentity(int attachEpoch, int stateGeneration)
	{
		AttachEpoch = attachEpoch;
		StateGeneration = stateGeneration;
	}

	/// <summary>Gets the attachment lifetime that established the state.</summary>
	public int AttachEpoch
	{
		get;
	}

	/// <summary>Gets the state replacement generation within the attachment lifetime.</summary>
	public int StateGeneration
	{
		get;
	}

	/// <summary>Compares both the attachment epoch and the state generation.</summary>
	public static bool operator ==(LuaStateIdentity left, LuaStateIdentity right)
	{
		return left.Equals(right);
	}

	/// <summary>Compares both the attachment epoch and the state generation.</summary>
	public static bool operator !=(LuaStateIdentity left, LuaStateIdentity right)
	{
		return !left.Equals(right);
	}

	/// <inheritdoc />
	public bool Equals(LuaStateIdentity other)
	{
		return AttachEpoch == other.AttachEpoch && StateGeneration == other.StateGeneration;
	}

	/// <inheritdoc />
	public override bool Equals(object? obj)
	{
		return obj is LuaStateIdentity other && Equals(other);
	}

	/// <inheritdoc />
	public override int GetHashCode()
	{
		return HashCode.Combine(AttachEpoch, StateGeneration);
	}

	/// <summary>Formats the identity for diagnostics only.</summary>
	public override string ToString()
	{
		return $"(attach epoch {AttachEpoch}, state generation {StateGeneration})";
	}
}
