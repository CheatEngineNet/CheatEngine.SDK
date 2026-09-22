using System.Runtime.CompilerServices;

using CheatEngine.SDK.Lua.Interop.Types;

namespace CheatEngine.SDK.Lua.Interop.Api;

// lua.h "'load' and 'call' functions" plus lua_error.
public static unsafe partial class LuaApi
{
	/// <summary>
	///     <c>void lua_callk (lua_State *L, int nargs, int nresults, lua_KContext ctx, lua_KFunction k)</c>. Unprotected
	///     call: the function sits below its <paramref name="nargs" /> arguments; all of them are replaced by the results.
	/// </summary>
	/// <param name="L">The state.</param>
	/// <param name="nargs">Number of arguments on the stack.</param>
	/// <param name="nresults">Number of results to keep, or <see cref="LUA_MULTRET" />.</param>
	/// <param name="ctx">Value handed to <paramref name="k" />; 0 without a continuation.</param>
	/// <param name="k">Continuation for a yield across the call, or null.</param>
	/// <remarks>
	///     Stack: -(nargs+1) +nresults. Raises: any. An error in the callee propagates to the enclosing protected call
	///     with <c>longjmp</c>, across the frame that made this call: from managed code use <see cref="lua_pcallk" />.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void lua_callk(lua_State* L, int nargs, int nresults, lua_KContext ctx, lua_KFunction k)
	{
		s_table.lua_callk(L, nargs, nresults, ctx, k);
	}

	/// <summary>
	///     <c>int lua_pcallk (lua_State *L, int nargs, int nresults, int errfunc, lua_KContext ctx, lua_KFunction k)</c>.
	///     Protected call. Returns <see cref="LUA_OK" /> with the results on the stack, or an error status
	///     (<see cref="LUA_ERRRUN" />, <see cref="LUA_ERRMEM" />, <see cref="LUA_ERRERR" />, <see cref="LUA_ERRGCMM" />) with
	///     exactly one error value in place of the function and its arguments.
	/// </summary>
	/// <param name="L">The state.</param>
	/// <param name="nargs">Number of arguments on the stack, above the function.</param>
	/// <param name="nresults">Number of results to keep, or <see cref="LUA_MULTRET" />.</param>
	/// <param name="errfunc">
	///     0, or the stack index of a message handler that receives the error value and returns the value to
	///     report.
	/// </param>
	/// <param name="ctx">Value handed to <paramref name="k" />; 0 without a continuation.</param>
	/// <param name="k">Continuation for a yield across the call, or null.</param>
	/// <remarks>
	///     Stack: -(nargs+1) +(nresults|1). Raises: never. The native function takes six parameters; a shorter declaration
	///     leaves <paramref name="ctx" /> and <paramref name="k" /> to whatever the registers hold.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int lua_pcallk(lua_State* L, int nargs, int nresults, int errfunc, lua_KContext ctx, lua_KFunction k)
	{
		return s_table.lua_pcallk(L, nargs, nresults, errfunc, ctx, k);
	}

	/// <summary>
	///     <c>int lua_load (lua_State *L, lua_Reader reader, void *dt, const char *chunkname, const char *mode)</c>.
	///     Compiles a chunk delivered piecewise by <paramref name="reader" /> and pushes the resulting function, or an
	///     error message. Returns <see cref="LUA_OK" />, <see cref="LUA_ERRSYNTAX" />, <see cref="LUA_ERRMEM" /> or
	///     <see cref="LUA_ERRGCMM" />.
	/// </summary>
	/// <param name="L">The state.</param>
	/// <param name="reader">
	///     Returns the next piece and its size, or null / size 0 at the end. The piece must stay valid until
	///     the next reader call.
	/// </param>
	/// <param name="dt">Opaque pointer passed to <paramref name="reader" />.</param>
	/// <param name="chunkname">NUL-terminated chunk name for messages and debug info ("=name" or "@file"), or null.</param>
	/// <param name="mode">NUL-terminated "t", "b" or "bt"; null means both text and binary.</param>
	/// <remarks>Stack: -0 +1. Raises: never. Never accept binary chunks from an untrusted source.</remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int lua_load(lua_State* L, lua_Reader reader, void* dt, byte* chunkname, byte* mode)
	{
		return s_table.lua_load(L, reader, dt, chunkname, mode);
	}

	/// <summary>
	///     <c>int lua_dump (lua_State *L, lua_Writer writer, void *data, int strip)</c>. Serializes the Lua function on top
	///     of the stack as a binary chunk through <paramref name="writer" />. Returns the last writer result (0 = no error).
	/// </summary>
	/// <param name="L">The state.</param>
	/// <param name="writer">Receives each piece; a non-zero result stops the dump.</param>
	/// <param name="data">Opaque pointer passed to <paramref name="writer" />.</param>
	/// <param name="strip">Non-zero to omit debug information.</param>
	/// <remarks>Stack: -0 +0. Raises: never. The function stays on the stack.</remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int lua_dump(lua_State* L, lua_Writer writer, void* data, int strip)
	{
		return s_table.lua_dump(L, writer, data, strip);
	}

	/// <summary>
	///     <c>int lua_error (lua_State *L)</c>. Raises the value on top of the stack as a Lua error. It never returns: it
	///     <c>longjmp</c>s to the enclosing protected call.
	/// </summary>
	/// <param name="L">The state.</param>
	/// <remarks>
	///     Stack: -1 +0. Raises: always. <b>Forbidden from managed frames</b>, including <c>[UnmanagedCallersOnly]</c>
	///     callbacks: the jump would skip managed frames, which the runtime does not support. It is declared so that the
	///     prohibition is written down where a caller would look for the function; a managed callback reports failure
	///     through its return values and lets a Lua-side wrapper call <c>error</c>.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int lua_error(lua_State* L)
	{
		return s_table.lua_error(L);
	}

	internal partial struct Table
	{
		internal delegate* unmanaged[Cdecl]<lua_State*, int, int, lua_KContext, lua_KFunction, void> lua_callk;
		internal delegate* unmanaged[Cdecl]<lua_State*, int, int, int, lua_KContext, lua_KFunction, int> lua_pcallk;
		internal delegate* unmanaged[Cdecl]<lua_State*, lua_Reader, void*, byte*, byte*, int> lua_load;
		internal delegate* unmanaged[Cdecl]<lua_State*, lua_Writer, void*, int, int> lua_dump;
		internal delegate* unmanaged[Cdecl]<lua_State*, int> lua_error;

		private void LoadCalls(ref ExportResolver exports)
		{
			lua_callk =
				(delegate* unmanaged[Cdecl]<lua_State*, int, int, lua_KContext, lua_KFunction, void>) exports.Resolve(
					"lua_callk");
			lua_pcallk =
				(delegate* unmanaged[Cdecl]<lua_State*, int, int, int, lua_KContext, lua_KFunction, int>)
				exports.Resolve("lua_pcallk");
			lua_load =
				(delegate* unmanaged[Cdecl]<lua_State*, lua_Reader, void*, byte*, byte*, int>) exports.Resolve(
					"lua_load");
			lua_dump =
				(delegate* unmanaged[Cdecl]<lua_State*, lua_Writer, void*, int, int>) exports.Resolve("lua_dump");
			lua_error = (delegate* unmanaged[Cdecl]<lua_State*, int>) exports.Resolve("lua_error");
		}
	}
}
