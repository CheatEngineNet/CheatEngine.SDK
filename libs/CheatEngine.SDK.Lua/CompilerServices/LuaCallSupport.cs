using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;

using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Lua.CompilerServices;

/// <summary>
///     Generator-facing: the cold exits of a generated call body. A generated <c>Try*</c> method keeps its success path
///     straight (record top, push, call, read, restore) and jumps here on every failure, so that the failure handling is
///     one call and the hot method stays small. Not meant to be called by hand.
/// </summary>
/// <remarks>
///     A body has three exits besides success: the bound global could not be resolved, the protected call failed, and
///     the call succeeded but a result is not of the expected kind (Cheat Engine's <c>nil</c> for "failed", or a wrong
///     type). A <c>Try*</c> wrapper uses the <c>Fail</c> helpers for all three;
///     a throwing wrapper uses <see cref="ThrowUnresolvedGlobal" />, <see cref="Throw" /> and
///     <see cref="ThrowUnexpectedResult" />.
///     The result type of the generic <c>Fail</c> helper deliberately does not allow <see langword="ref" />
///     <see langword="struct" />s:
///     a body restores the stack before it returns, and a <c>ReadOnlySpan&lt;byte&gt;</c> read from a popped Lua string
///     would point at memory Lua may already have freed. String results are copied out (
///     <see cref="LuaState.TryCopyUtf8" />)
///     or read as <see cref="string" />.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class LuaCallSupport
{
	/// <summary>
	///     Restores the stack to <paramref name="top" /> (discarding an error value or a partial result) and returns
	///     <see langword="false" />.
	/// </summary>
	/// <param name="state">The state the body ran on.</param>
	/// <param name="top">The top recorded at the start of the body.</param>
	/// <returns><see langword="false" />, always.</returns>
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool Fail(LuaState state, int top)
	{
		state.SetTop(top);
		return false;
	}

	/// <summary>
	///     Restores the stack to <paramref name="top" />, defaults the <see langword="out" /> result and returns
	///     <see langword="false" />.
	/// </summary>
	/// <typeparam name="TResult">
	///     The result type of the generated method: a value, a <see cref="string" />, or the
	///     <c>written</c> count of a copy-out result; never a span into Lua's memory.
	/// </typeparam>
	/// <param name="state">The state the body ran on.</param>
	/// <param name="top">The top recorded at the start of the body.</param>
	/// <param name="result">Set to <see langword="default" />.</param>
	/// <returns><see langword="false" />, always.</returns>
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool Fail<TResult>(LuaState state, int top, out TResult result)
	{
		state.SetTop(top);
		result = default!;
		return false;
	}

	/// <summary>
	///     Restores the stack to <paramref name="top" /> and returns the factual detailed outcome for an opt-in
	///     generated binding.
	/// </summary>
	/// <param name="state">The state the body ran on.</param>
	/// <param name="top">The top recorded at the start of the body.</param>
	/// <param name="status">The already classified outcome; no error text is read from Lua.</param>
	/// <returns><paramref name="status" />.</returns>
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static LuaOperationStatus Fail(LuaState state, int top, LuaOperationStatus status)
	{
		state.SetTop(top);
		return status;
	}

	/// <summary>
	///     Restores the stack to <paramref name="top" />, defaults the <see langword="out" /> result and returns the
	///     factual detailed outcome for an opt-in generated binding.
	/// </summary>
	/// <typeparam name="TResult">The result type of the generated method.</typeparam>
	/// <param name="state">The state the body ran on.</param>
	/// <param name="top">The top recorded at the start of the body.</param>
	/// <param name="status">The already classified outcome; no error text is read from Lua.</param>
	/// <param name="result">Set to <see langword="default" />.</param>
	/// <returns><paramref name="status" />.</returns>
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static LuaOperationStatus Fail<TResult>(LuaState state, int top, LuaOperationStatus status,
		out TResult result)
	{
		state.SetTop(top);
		result = default!;
		return status;
	}

	/// <summary>
	///     Pushes an optional argument that a generated wrapper decided to pass: its value through
	///     <typeparamref name="TMarshaller" />, or <c>nil</c> for <see cref="LuaOptional{T}.IsNil" />. A wrapper never
	///     calls this for an omitted argument: it computes the argument count first and pushes only up to the last
	///     argument that is not omitted.
	/// </summary>
	/// <typeparam name="T">The value type.</typeparam>
	/// <typeparam name="TMarshaller">The marshaller of <typeparamref name="T" />.</typeparam>
	/// <param name="state">The state to push on.</param>
	/// <param name="value">The value or <c>nil</c>.</param>
	/// <exception cref="ArgumentException"><paramref name="value" /> is omitted: an omitted argument is never pushed.</exception>
	[LuaStackEffect(1)]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void PushOptional<T, TMarshaller>(LuaState state, LuaOptional<T> value)
		where T : notnull
		where TMarshaller : ILuaMarshaller<T>
	{
		if (value.TryGetValue(out T? present))
		{
			TMarshaller.Push(state, present);
		}
		else if (value.IsNil)
		{
			state.PushNil();
		}
		else
		{
			ThrowOmittedArgument(nameof(value));
		}
	}

	/// <summary>
	///     Reads an optional value at a positive, absolute stack <paramref name="index" /> without changing the stack: a
	///     position above the top is <see cref="LuaOptional{T}.IsOmitted" /> (the caller or the callee passed fewer
	///     values), <c>nil</c> is <see cref="LuaOptional{T}.IsNil" />, and a value <typeparamref name="TMarshaller" /> reads
	///     is present. Generated thunks read optional arguments and generated wrappers read optional results with it.
	/// </summary>
	/// <typeparam name="T">The value type.</typeparam>
	/// <typeparam name="TMarshaller">The marshaller of <typeparamref name="T" />.</typeparam>
	/// <param name="state">The state to read from.</param>
	/// <param name="index">A positive, absolute stack index; never touched when it is above the top.</param>
	/// <param name="value">The optional value; omitted when the read fails.</param>
	/// <returns>
	///     <see langword="false" /> only when a non-<c>nil</c> value is present that <typeparamref name="TMarshaller" />
	///     cannot read (a value of another kind).
	/// </returns>
	[LuaStackEffect(0)]
	public static bool TryReadOptional<T, TMarshaller>(LuaState state, int index, out LuaOptional<T> value)
		where T : notnull
		where TMarshaller : ILuaMarshaller<T>
	{
		if (index <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(index), index,
				"An optional value is read at a positive, absolute index.");
		}

		if (index > state.Top)
		{
			value = default;
			return true;
		}

		if (state.IsNil(index))
		{
			value = LuaOptional.Nil<T>();
			return true;
		}

		if (TMarshaller.TryRead(state, index, out T? read))
		{
			value = LuaOptional<T>.FromValue(read);
			return true;
		}

		value = default;
		return false;
	}

	/// <summary>
	///     Copies every value from the positive, absolute stack index <paramref name="firstIndex" /> to the top into
	///     <paramref name="destination" /> through <typeparamref name="TMarshaller" />, without changing the stack: the
	///     variadic tail of a generated Outcome wrapper.
	/// </summary>
	/// <typeparam name="T">The unmanaged element type.</typeparam>
	/// <typeparam name="TMarshaller">The marshaller of <typeparamref name="T" />.</typeparam>
	/// <param name="state">The state to read from.</param>
	/// <param name="firstIndex">The absolute index of the first value; above the top means no value.</param>
	/// <param name="destination">Receives the values, in stack order.</param>
	/// <param name="count">
	///     The number of values copied on success; on <see cref="LuaOperationStatusKind.ResultCapacityExceeded" /> the
	///     number of values Lua returned (the capacity needed); otherwise 0.
	/// </param>
	/// <returns>
	///     <see cref="LuaOperationStatus.Success" />; <see cref="LuaOperationStatus.ResultCapacityExceeded" /> when there
	///     are more values than <paramref name="destination" /> holds (nothing copied);
	///     <see cref="LuaOperationStatus.NilResult" /> or <see cref="LuaOperationStatus.InvalidResult" /> for the first
	///     value that cannot be read (<paramref name="destination" /> is cleared up to that position). No error text is
	///     read.
	/// </returns>
	[LuaStackEffect(0)]
	public static LuaOperationStatus ReadResults<T, TMarshaller>(LuaState state, int firstIndex, Span<T> destination,
		out int count)
		where T : unmanaged
		where TMarshaller : ILuaMarshaller<T>
	{
		if (firstIndex <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(firstIndex), firstIndex,
				"Results are read from a positive, absolute index.");
		}

		int available = state.Top - firstIndex + 1;
		if (available <= 0)
		{
			count = 0;
			return LuaOperationStatus.Success;
		}

		if (available > destination.Length)
		{
			count = available;
			return LuaOperationStatus.ResultCapacityExceeded;
		}

		for (int i = 0; i < available; i++)
		{
			int index = firstIndex + i;
			if (!TMarshaller.TryRead(state, index, out destination[i]))
			{
				destination[..(i + 1)].Clear();
				count = 0;
				return state.IsNil(index) ? LuaOperationStatus.NilResult : LuaOperationStatus.InvalidResult;
			}
		}

		count = available;
		return LuaOperationStatus.Success;
	}

	[DoesNotReturn]
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ThrowOmittedArgument(string parameterName)
	{
		throw new ArgumentException("An omitted optional Lua argument is never pushed; the wrapper stops before it.",
			parameterName);
	}

	/// <summary>
	///     Restores the stack to <paramref name="top" /> and throws a <see cref="LuaException" /> describing the error value
	///     that was on top of the stack: the exit of a generated throwing wrapper when the protected call failed.
	/// </summary>
	/// <param name="state">The state the body ran on.</param>
	/// <param name="top">The top recorded at the start of the body.</param>
	/// <param name="status">The failure status.</param>
	/// <exception cref="LuaException">Always.</exception>
	[MethodImpl(MethodImplOptions.NoInlining)]
	[DoesNotReturn]
	public static void Throw(LuaState state, int top, LuaStatus status)
	{
		LuaError error = LuaError.FromStack(state, status);
		state.SetTop(top);
		LuaException.Throw(error);
	}

	/// <summary>
	///     Restores the stack to <paramref name="top" /> and throws because a bound global could not be resolved (undefined
	///     or not a function): the exit of a generated throwing wrapper when <see cref="LuaGlobalFunctions.TryPush" /> fails.
	/// </summary>
	/// <param name="state">The state the body ran on.</param>
	/// <param name="top">The top recorded at the start of the body.</param>
	/// <param name="globalName">The name the body tried to bind.</param>
	/// <exception cref="LuaException">Always.</exception>
	[MethodImpl(MethodImplOptions.NoInlining)]
	[DoesNotReturn]
	public static void ThrowUnresolvedGlobal(LuaState state, int top, string globalName)
	{
		state.SetTop(top);
		throw new LuaException("The Lua global '" + globalName + "' is undefined or is not a function.");
	}

	/// <summary>
	///     Names the Lua type of the value at <paramref name="index" />, restores the stack to <paramref name="top" /> and
	///     throws: the exit of a generated throwing wrapper when the call succeeded but a result could not be read as
	///     the declared type (<c>The Lua global 'readInteger' returned a nil value, not an integer.</c>).
	/// </summary>
	/// <param name="state">The state the body ran on.</param>
	/// <param name="top">The top recorded at the start of the body.</param>
	/// <param name="index">The index of the offending result, still on the stack (<c>-1</c> for a single result).</param>
	/// <param name="globalName">The name of the bound global.</param>
	/// <param name="expected">What the wrapper expected, with its article: <c>"an integer"</c>, <c>"a string"</c>.</param>
	/// <exception cref="LuaException">Always.</exception>
	[MethodImpl(MethodImplOptions.NoInlining)]
	[DoesNotReturn]
	public static void ThrowUnexpectedResult(LuaState state, int top, int index, string globalName, string expected)
	{
		string typeName = Encoding.UTF8.GetString(state.TypeName(index));
		state.SetTop(top);
		throw new LuaException("The Lua global '" + globalName + "' returned a " + typeName + " value, not " +
		                       expected + ".");
	}
}
