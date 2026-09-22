using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Lua.Callbacks;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Interop.Protected;
using CheatEngine.SDK.Lua.Interop.Types;
using CheatEngine.SDK.Lua.Protected;
using CheatEngine.SDK.Lua.Runtime;

using static CheatEngine.SDK.Lua.Interop.Api.LuaApi;

namespace CheatEngine.SDK.Lua.State;

// Pushing managed functions: with the error channel (the normal form) or without it.
public readonly unsafe partial struct
	LuaState // NOSONAR: this partial contains the required Lua C-ABI function pointers.
{
	/// <summary>
	///     Pushes a generated <c>[LuaFunction]</c> closure whose invocation is bound to one host attachment and Lua
	///     state generation. Stack: +1 (the function) on success; +1 (the error value) on failure.
	/// </summary>
	/// <param name="thunk">The managed <c>lua_CFunction</c> that implements the generated export.</param>
	/// <param name="identity">The attachment and state-generation identity captured while registering the export.</param>
	/// <param name="requiresAttachedRuntime">
	///     Whether an attached runtime must still own <paramref name="identity" /> for the closure to enter plugin
	///     code. Native-fixture registrations made while the runtime is detached deliberately pass
	///     <see langword="false" /> to preserve their standalone contract.
	/// </param>
	/// <returns>The status of installing the helpers or running the wrapper.</returns>
	/// <remarks>
	///     This is the implementation primitive behind <see cref="LuaRuntime.TryPushGeneratedFunction" />. It is
	///     public only because generated code is compiled into a consumer assembly; ordinary code should use the
	///     runtime entry point instead. The closure retains the identity in native Lua upvalues, not a mutable managed
	///     field, so a function a script kept through disable, reset or re-enable cannot become current again.
	/// </remarks>
	[EditorBrowsable(EditorBrowsableState.Advanced)]
	public LuaStatus TryPushGeneratedFunction(LuaNativeFunction thunk, LuaStateIdentity identity,
		bool requiresAttachedRuntime)
	{
		if (thunk.IsNull)
		{
			throw new ArgumentException("The thunk is the null function.", nameof(thunk));
		}

		int top = Top;
		LuaStatus status = LuaHelpers.Push(Pointer, LuaHelper.Wrap);
		if (!status.IsOk)
		{
			return status;
		}

		try
		{
			status = PushUncheckedGeneratedFunction(thunk, identity, requiresAttachedRuntime);
		}
		catch (InvalidOperationException)
		{
			// The wrapper occupies the top slot. Replace it with the documented allocation-free nil error value when
			// the guarded closure's immediate stack reservation failed.
			lua_settop(Pointer, -2);
			lua_pushnil(Pointer);
			return LuaStatus.MemoryError;
		}

		// The protected bridge consumes the four upvalues when it reports a Lua failure, but the wrapper remains below
		// its error value. Preserve that one error while dropping every registration intermediate; Try* callers must
		// receive the original Lua status rather than a LuaException from the checked API.
		if (!status.IsOk)
		{
			return KeepProtectedError(top, status);
		}

		return TryCall(1, 1);
	}

	/// <summary>
	///     Pushes a managed function without state, wrapped so that a failure it reports through
	///     <see cref="LuaThunk.Fail(LuaState,System.ReadOnlySpan{byte})" /> becomes a Lua error in the caller.
	///     Stack: +1 (the
	///     function) on success; +1 (the error value) on failure.
	/// </summary>
	/// <param name="thunk">The managed <c>lua_CFunction</c>; see <see cref="LuaNativeFunction" /> for its rules.</param>
	/// <returns>The status of installing the helpers or running the wrapper.</returns>
	/// <exception cref="ArgumentException"><paramref name="thunk" /> is the null function.</exception>
	/// <remarks>
	///     Allocates, inside Lua, the C closure and the wrapper closure: a registration-time cost. Assign the result to a
	///     global with <see cref="TrySetGlobal" />, or keep it with
	///     <see cref="CreateRef" />.
	/// </remarks>
	public LuaStatus TryPushFunction(LuaNativeFunction thunk)
	{
		if (thunk.IsNull)
		{
			throw new ArgumentException("The thunk is the null function.", nameof(thunk));
		}

		LuaStatus status = LuaHelpers.Push(Pointer, LuaHelper.Wrap);
		if (!status.IsOk)
		{
			return status;
		}

		try
		{
			PushUncheckedFunction(thunk);
		}
		catch (InvalidOperationException)
		{
			// The wrapper occupies the top slot. Replace it with the documented allocation-free nil error value when
			// the bare function's immediate stack reservation failed.
			lua_settop(Pointer, -2);
			lua_pushnil(Pointer);
			return LuaStatus.MemoryError;
		}

		return TryCall(1, 1);
	}

	/// <summary>
	///     Pushes a managed function as a bare C function (<c>lua_pushcclosure</c> with no upvalues): no error channel,
	///     so what the thunk returns is exactly what the Lua caller receives. For functions that cannot fail.
	/// </summary>
	/// <param name="thunk">The managed <c>lua_CFunction</c>.</param>
	/// <exception cref="ArgumentException"><paramref name="thunk" /> is the null function.</exception>
	/// <exception cref="InvalidOperationException">
	///     Lua cannot reserve the one stack slot required for the light-C-function fast path. The stack is unchanged.
	/// </exception>
	/// <remarks>
	///     CE's pinned Lua 5.3 implementation stores a zero-upvalue C function as a light C function, so the push does
	///     not allocate after the stack slot is reserved. This method performs that <c>lua_checkstack(L, 1)</c> check
	///     immediately before the push; do not replace it with a generic closure call or move another Lua call between
	///     the check and the push.
	/// </remarks>
	[LuaStackEffect(1)]
	public void PushUncheckedFunction(LuaNativeFunction thunk)
	{
		if (thunk.IsNull)
		{
			throw new ArgumentException("The thunk is the null function.", nameof(thunk));
		}

		if (lua_checkstack(Pointer, 1) == 0)
		{
			throw new InvalidOperationException(
				"Lua could not reserve one stack slot for the bare C function; the stack is unchanged.");
		}

		lua_pushcclosure(Pointer, thunk.Pointer, 0);
	}

	// The generated-export dispatcher needs four upvalues: the original cdecl thunk, attach epoch, state generation
	// and whether this registration was made under a real host attachment. Use the protected bridge for closure
	// creation because lua_pushcclosure may allocate and must never longjmp across a managed frame.
	private LuaStatus PushUncheckedGeneratedFunction(LuaNativeFunction thunk, LuaStateIdentity identity,
		bool requiresAttachedRuntime)
	{
		if (lua_checkstack(Pointer, 4) == 0)
		{
			throw new InvalidOperationException(
				"Lua could not reserve four stack slots for the guarded generated C function; the stack is unchanged.");
		}

		lua_pushlightuserdata(Pointer, (void*) thunk.Address);
		lua_pushinteger(Pointer, identity.AttachEpoch);
		lua_pushinteger(Pointer, identity.StateGeneration);
		lua_pushinteger(Pointer, requiresAttachedRuntime ? 1 : 0);
		return new LuaStatus(LuaProtectedApi.PushClosure(Pointer,
			(nint) (delegate* unmanaged[Cdecl]<lua_State*, int>) &DispatchGeneratedFunction, 4));
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
	private static int DispatchGeneratedFunction(lua_State* pointer)
	{
		LuaState state = new(pointer);
		try
		{
			bool required = lua_tointegerx(pointer, lua_upvalueindex(4), null) != 0;
			if (required)
			{
				int attachEpoch = checked((int) lua_tointegerx(pointer, lua_upvalueindex(2), null));
				int stateGeneration = checked((int) lua_tointegerx(pointer, lua_upvalueindex(3), null));
				if (!LuaRuntime.IsGeneratedFunctionRegistrationCurrent(attachEpoch, stateGeneration))
				{
					return LuaThunk.Fail(state, "Lua function registration has expired"u8);
				}

				if (!LuaRuntime.TryEnterCallbackOperation(out LuaRuntimeOperation operation))
				{
					return LuaThunk.Fail(state, "the Lua runtime is stopping"u8);
				}

				try
				{
					return InvokeGeneratedThunk(pointer, state);
				}
				finally
				{
					operation.Dispose();
				}
			}

			return InvokeGeneratedThunk(pointer, state);
		}
		catch (Exception exception)
		{
			return LuaThunk.Fail(state, exception);
		}
	}

	private static int InvokeGeneratedThunk(lua_State* pointer, LuaState state)
	{
		IntPtr thunkAddress = (nint) lua_touserdata(pointer, lua_upvalueindex(1));
		if (thunkAddress == 0)
		{
			return LuaThunk.Fail(state, "Lua function thunk is unavailable"u8);
		}

		delegate* unmanaged[Cdecl]<IntPtr, int> thunk = (delegate* unmanaged[Cdecl]<nint, int>) thunkAddress;
		return thunk((nint) pointer);
	}
}
