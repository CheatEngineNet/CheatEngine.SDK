using System;
using System.Globalization;
using System.Runtime.CompilerServices;

using CheatEngine.SDK.Lua.Interop.Api;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Lua.Calls;

/// <summary>
///     Outcome of a protected operation: the status code of the Lua 5.3 C API (<c>LUA_OK</c>, <c>LUA_ERRRUN</c>, ...)
///     as a value type with a name and a success test. The primitive result of every <c>Try*</c> member of
///     <see cref="LuaState" /> that can run Lua code.
/// </summary>
/// <remarks>
///     Protocol: on <see cref="IsOk" /> the operation's documented results are on the stack; otherwise exactly one error
///     value is on top (a string in almost every case) and the caller either restores its frame, which discards it, or
///     reads it first with <see cref="LuaError.FromStack" />, or throws with <see cref="ThrowIfFailed" />. The
///     status carries no message itself, so returning one allocates nothing.
/// </remarks>
/// <remarks>Wraps a raw status code as returned by <c>lua_pcallk</c> or <c>luaL_loadbufferx</c>.</remarks>
/// <param name="code">The code; any value is accepted, unknown codes are treated as failures.</param>
public readonly struct LuaStatus(int code) : IEquatable<LuaStatus>
{
	/// <summary>Success (<c>LUA_OK</c>).</summary>
	public static LuaStatus Ok => new(LuaApi.LUA_OK);

	/// <summary>A coroutine yielded (<c>LUA_YIELD</c>). Not produced by this assembly's own operations.</summary>
	public static LuaStatus Yield => new(LuaApi.LUA_YIELD);

	/// <summary>
	///     A runtime error (<c>LUA_ERRRUN</c>): <c>error(...)</c>, the error channel of managed callbacks, and the one
	///     precondition this assembly reports as a status instead of throwing (
	///     <see cref="Callbacks.LuaCallback.TryRegister" /> on a
	///     released or stale callback), always with a message on the stack.
	/// </summary>
	public static LuaStatus RuntimeError => new(LuaApi.LUA_ERRRUN);

	/// <summary>The chunk did not compile (<c>LUA_ERRSYNTAX</c>).</summary>
	public static LuaStatus SyntaxError => new(LuaApi.LUA_ERRSYNTAX);

	/// <summary>Lua could not allocate memory (<c>LUA_ERRMEM</c>); the message handler is not run for it.</summary>
	public static LuaStatus MemoryError => new(LuaApi.LUA_ERRMEM);

	/// <summary>A <c>__gc</c> metamethod raised during a collector step (<c>LUA_ERRGCMM</c>).</summary>
	public static LuaStatus GcMetamethodError => new(LuaApi.LUA_ERRGCMM);

	/// <summary>The message handler itself raised (<c>LUA_ERRERR</c>).</summary>
	public static LuaStatus MessageHandlerError => new(LuaApi.LUA_ERRERR);

	/// <summary>A file could not be read (<c>LUA_ERRFILE</c>). Not produced by this assembly's own operations.</summary>
	public static LuaStatus FileError => new(LuaApi.LUA_ERRFILE);

	/// <summary>Gets the raw code (<c>LUA_OK</c> is 0).</summary>
	public int Code
	{
		get;
	} = code;

	/// <summary>Gets a value indicating whether the operation succeeded.</summary>
	public bool IsOk
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => Code == LuaApi.LUA_OK;
	}

	/// <summary>Compares two statuses by code.</summary>
	/// <param name="left">First status.</param>
	/// <param name="right">Second status.</param>
	public static bool operator ==(LuaStatus left, LuaStatus right)
	{
		return left.Code == right.Code;
	}

	/// <summary>Compares two statuses by code.</summary>
	/// <param name="left">First status.</param>
	/// <param name="right">Second status.</param>
	public static bool operator !=(LuaStatus left, LuaStatus right)
	{
		return left.Code != right.Code;
	}

	/// <inheritdoc />
	public bool Equals(LuaStatus other)
	{
		return Code == other.Code;
	}

	/// <inheritdoc />
	public override bool Equals(object? obj)
	{
		return obj is LuaStatus other && Equals(other);
	}

	/// <inheritdoc />
	public override int GetHashCode()
	{
		return Code;
	}

	/// <summary>The C API name of the code (<c>LUA_OK</c>, <c>LUA_ERRRUN</c>, ...), or the number for an unknown code.</summary>
	public override string ToString()
	{
		return Code switch
		{
			LuaApi.LUA_OK => "LUA_OK",
			LuaApi.LUA_YIELD => "LUA_YIELD",
			LuaApi.LUA_ERRRUN => "LUA_ERRRUN",
			LuaApi.LUA_ERRSYNTAX => "LUA_ERRSYNTAX",
			LuaApi.LUA_ERRMEM => "LUA_ERRMEM",
			LuaApi.LUA_ERRGCMM => "LUA_ERRGCMM",
			LuaApi.LUA_ERRERR => "LUA_ERRERR",
			LuaApi.LUA_ERRFILE => "LUA_ERRFILE",
			_ => Code.ToString(CultureInfo.InvariantCulture)
		};
	}

	/// <summary>
	///     Throws a <see cref="LuaException" /> built from the error value on top of <paramref name="state" />'s stack when
	///     the status is a failure; returns normally otherwise. The error value is left on the stack for the caller's
	///     frame to discard.
	/// </summary>
	/// <param name="state">The state the failed operation ran on.</param>
	/// <exception cref="LuaException">The status is not <see cref="Ok" />.</exception>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void ThrowIfFailed(LuaState state)
	{
		if (Code != LuaApi.LUA_OK)
		{
			LuaException.ThrowFromStack(state, this);
		}
	}
}
