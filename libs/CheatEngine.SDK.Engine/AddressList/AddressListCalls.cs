using System;
using System.Diagnostics.CodeAnalysis;

using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.CompilerServices;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.AddressList;

/// <summary>Protected call shapes used only by the address-list object wrappers.</summary>
/// <remarks>
///     The current EngineApi generator has no object result, object argument, or instance-method specification form.
///     Keeping
///     the three shapes here makes that gap explicit while preserving the generator's stack and failure contract: a failed
///     lookup, protected call, <c>nil</c> result, or result of the wrong kind returns <see langword="false" />, defaults
///     the
///     result, and restores the stack.
/// </remarks>
internal static class AddressListCalls
{
	public static bool TryGetGlobal<TMarshaller, TResult>(LuaRef cache, ReadOnlySpan<byte> global,
		[MaybeNullWhen(false)] out TResult result)
		where TMarshaller : struct, ILuaMarshaller<TResult>
	{
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		int top = state.Top;
		try
		{
			if (!LuaGlobalFunctions.TryPush(state, cache, global) || !state.TryCall(0, 1).IsOk ||
				!TMarshaller.TryRead(state, -1, out result))
			{
				return LuaCallSupport.Fail(state, top, out result);
			}

			return true;
		}
		catch (LuaException)
		{
			result = default;
			return false;
		}
		finally
		{
			state.SetTop(top);
		}
	}

	public static bool TryCall<TArgumentMarshaller, TArgument, TResultMarshaller, TResult>(CEObject receiver,
		ReadOnlySpan<byte> method, TArgument argument, [MaybeNullWhen(false)] out TResult result)
		where TArgumentMarshaller : struct, ILuaMarshaller<TArgument>
		where TResultMarshaller : struct, ILuaMarshaller<TResult>
	{
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		int top = state.Top;
		try
		{
			if (!receiver.TryPushMethodLeavingObject(state, method).IsOk)
			{
				return LuaCallSupport.Fail(state, top, out result);
			}

			TArgumentMarshaller.Push(state, argument);
			if (!state.TryCall(1, 1).IsOk || !TResultMarshaller.TryRead(state, -1, out result))
			{
				return LuaCallSupport.Fail(state, top, out result);
			}

			return true;
		}
		catch (LuaException)
		{
			result = default;
			return false;
		}
		finally
		{
			state.SetTop(top);
		}
	}

	public static bool TryCall<TArgumentMarshaller, TArgument>(CEObject receiver, ReadOnlySpan<byte> method,
		TArgument argument)
		where TArgumentMarshaller : struct, ILuaMarshaller<TArgument>
	{
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		int top = state.Top;
		try
		{
			if (!receiver.TryPushMethodLeavingObject(state, method).IsOk)
			{
				return LuaCallSupport.Fail(state, top);
			}

			TArgumentMarshaller.Push(state, argument);
			return state.TryCall(1, 0).IsOk || LuaCallSupport.Fail(state, top);
		}
		catch (LuaException)
		{
			return false;
		}
		finally
		{
			state.SetTop(top);
		}
	}

	public static bool TryGetIndex<TMarshaller, TResult>(CEObject receiver, int zeroBasedIndex,
		[MaybeNullWhen(false)] out TResult result)
		where TMarshaller : struct, ILuaMarshaller<TResult>
	{
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		int top = state.Top;
		try
		{
			if (!receiver.TryGetIndex(state, zeroBasedIndex).IsOk || !TMarshaller.TryRead(state, -1, out result))
			{
				return LuaCallSupport.Fail(state, top, out result);
			}

			return true;
		}
		catch (LuaException)
		{
			result = default;
			return false;
		}
		finally
		{
			state.SetTop(top);
		}
	}
}
