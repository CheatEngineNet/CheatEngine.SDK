using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;

using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Annotations.Threading;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Objects;

/// <summary>
///     A borrowed handle to a Cheat Engine object: the native object pointer, plus the primitives every wrapper is
///     built from (push, property get and set, indexed access, bound-method call). Pointer-sized, immutable, owns
///     nothing; equality is identity of the native object.
/// </summary>
/// <remarks>
///     <para>
///         <b>Identity.</b> Cheat Engine puts an object on the Lua stack as a full userdata whose first pointer-sized
///         field
///         is the native object pointer, and takes one back through its <c>LuaPushClassInstance</c> export
///         (<see cref="LuaRuntime.PushHostObject" />). That pointer is the whole state of a handle: two handles are equal
///         when they name the same object, and a handle stays valid exactly as long as the object exists, which the handle
///         cannot know. <see cref="TryRead" /> is the single place where the userdata layout is decoded; see its remarks
///         for
///         the assumption it encodes.
///     </para>
///     <para>
///         <b>Ownership.</b> A handle never destroys anything. An object that this plugin created is held in an
///         <see cref="Owned{T}" />, which destroys it on <see cref="Owned{T}.Dispose" />; objects that Cheat Engine owns
///         (the address list, memory records, the GUI scanner and its found list, forms) only ever exist as handles, so
///         there is nothing to dispose by mistake.
///     </para>
///     <para>
///         <b>Access convention.</b> A property is <c>obj.Name</c>: <c>__index</c> and <c>__newindex</c> of the
///         userdata's metatable run inside a protected call. A method is <c>obj.name</c>, which Cheat Engine
///         returns as a function already bound to the instance, called <b>without</b> passing the object as its
///         first argument; the colon form <c>obj:name()</c> is not used. Indexed access is <c>obj[i]</c> with Cheat
///         Engine's own zero-based index, passed through unchanged (see <see cref="IndexBase" />).
///     </para>
///     <para>
///         <b>Two member families.</b> The members that take a <see cref="LuaState" /> work on the stack and return a
///         <see cref="LuaStatus" />, following the protocol of the <c>Try*</c> members of <see cref="LuaState" />: on
///         success
///         the documented values are on the stack; on failure exactly one error value is on top, in place of the results
///         and
///         of the inputs the member consumed, and the caller's frame discards it. They are the building blocks of
///         generated
///         wrappers written by hand. The typed members acquire the state themselves (
///         <see cref="LuaRuntime.AcquireState" />,
///         one call), restore the stack before returning and report failure as <see langword="false" /> with a default
///         result; a Lua error message is not kept, because keeping it would allocate. They are built on the two
///         generator-facing members <see cref="TryGetPropertyLeavingObject" /> and
///         <see cref="TryPushMethodLeavingObject" />,
///         which leave the pushed object on the stack for the body's single restore instead of removing it per access.
///     </para>
///     <para>
///         <b>Threads.</b> A handle is a value and can be copied to any thread; the Lua state passed to a member must be
///         the calling thread's. Whether the <i>object</i> may be touched off the main thread depends on its class
///         (GUI and address-list objects may not), which is why the typed wrappers, not these primitives, carry
///         <see cref="MainThreadOnlyAttribute" />. Every member that pushes the object needs the host binding
///         (<see cref="RequiresPluginEnabledAttribute" />): before the plugin is enabled it throws
///         <see cref="InvalidOperationException" /> from <see cref="LuaRuntime.PushHostObject" />.
///     </para>
///     <para>
///         <b>Allocation.</b> No member allocates on the managed side; the strings pushed for names are the caller's
///         <c>"..."u8</c> literals, copied by Lua.
///     </para>
/// </remarks>
public readonly struct CEObject : IEquatable<CEObject>, ICEObject<CEObject>, ILuaMarshaller<CEObject>
{
	/// <summary>The name of the method every Cheat Engine object has to free itself.</summary>
	private static ReadOnlySpan<byte> DestroyMethodName => "destroy"u8;

	/// <summary>Wraps a native object pointer.</summary>
	/// <param name="value">The pointer as Cheat Engine's Lua API knows it; zero gives <see cref="Null" />.</param>
	/// <remarks>A pure value operation: nothing is checked, nothing is called.</remarks>
	public CEObject(nint value)
	{
		Value = value;
	}

	/// <summary>Gets the handle that names no object.</summary>
	public static CEObject Null => default;

	/// <summary>Gets the native object pointer: the identity of the object, never dereferenced by managed code.</summary>
	public nint Value
	{
		get;
	}

	/// <summary>Gets a value indicating whether the handle names no object.</summary>
	public bool IsNull => Value == 0;

	/// <inheritdoc />
	public CEObject Handle => this;

	/// <inheritdoc />
	public static CEObject FromHandle(CEObject handle)
	{
		return handle;
	}

	/// <summary>Compares two handles for identity of the native object.</summary>
	/// <param name="left">First handle.</param>
	/// <param name="right">Second handle.</param>
	public static bool operator ==(CEObject left, CEObject right)
	{
		return left.Value == right.Value;
	}

	/// <summary>Compares two handles for identity of the native object.</summary>
	/// <param name="left">First handle.</param>
	/// <param name="right">Second handle.</param>
	public static bool operator !=(CEObject left, CEObject right)
	{
		return left.Value != right.Value;
	}

	/// <inheritdoc />
	public bool Equals(CEObject other)
	{
		return Value == other.Value;
	}

	/// <inheritdoc />
	public override bool Equals(object? obj)
	{
		return obj is CEObject other && Equals(other);
	}

	/// <inheritdoc />
	public override int GetHashCode()
	{
		return Value.GetHashCode();
	}

	/// <summary>Formats the pointer, for diagnostics.</summary>
	/// <returns><c>CEObject@0x...</c>, or <c>CEObject(null)</c>.</returns>
	public override string ToString()
	{
		return IsNull ? "CEObject(null)" : "CEObject@0x" + Value.ToString("X", CultureInfo.InvariantCulture);
	}

	/// <summary>
	///     Pushes the object's userdata through the host's pusher (<see cref="LuaRuntime.PushHostObject" />), which is
	///     the only way a Cheat Engine object gets onto the Lua stack. A <see cref="Null" /> handle pushes <c>nil</c>.
	/// </summary>
	/// <param name="state">The calling thread's state.</param>
	/// <exception cref="InvalidOperationException">The plugin is not enabled, or the host binding has no pusher.</exception>
	/// <remarks>
	///     What the host allocates for the userdata (and whether it caches one per object) is the host's business.
	/// </remarks>
	[RequiresPluginEnabled]
	[LuaStackEffect(1)]
	public void Push(LuaState state)
	{
		if (IsNull)
		{
			state.PushNil();
			return;
		}

		LuaRuntime.PushHostObject(state, Value);
	}

	/// <summary>The <see cref="ILuaMarshaller{T}" /> form of <see cref="Push(LuaState)" />.</summary>
	/// <param name="state">The calling thread's state.</param>
	/// <param name="value">The handle to push; <see cref="Null" /> pushes <c>nil</c>.</param>
	[RequiresPluginEnabled]
	[LuaStackEffect(1)]
	public static void Push(LuaState state, CEObject value)
	{
		value.Push(state);
	}

	/// <summary>
	///     Reads the handle of the Cheat Engine object at <paramref name="index" />: the value must be a full userdata
	///     whose block is at least pointer-sized and whose first pointer-sized field is not null. The stack is not
	///     modified and nothing is allocated.
	/// </summary>
	/// <param name="state">The state to read from.</param>
	/// <param name="index">An acceptable index.</param>
	/// <param name="value">The handle, or <see cref="Null" /> when the value is not a host object.</param>
	/// <returns><see langword="true" /> when <paramref name="value" /> holds a handle.</returns>
	/// <remarks>
	///     <para>
	///         This is the one place that decodes the userdata layout, and the layout is an <b>assumption</b>: Cheat
	///         Engine's <c>LuaPushClassInstance</c> creates a full userdata and stores the object pointer in its first
	///         pointer-sized field. Three checks stand in for the knowledge this SDK does not have: the type tag must
	///         be <see cref="LuaType.Userdata" /> (a light userdata is a bare pointer with no block to read; a table or
	///         a number is not an object), the block must be at least a pointer long (<c>lua_rawlen</c>, so the read
	///         cannot overrun a smaller block), and the field must not be zero. A userdata of some other library that
	///         happens to start with a non-null pointer is indistinguishable from an object here; the metatable is not
	///         inspected because Cheat Engine's metatables carry no known marker. The layout can be cross-checked on a
	///         live Cheat Engine against its own <c>userDataToInteger(obj)</c>, which returns the same pointer.
	///     </para>
	///     <para>Three C API calls (type, block, length) and one memory read; never raises.</para>
	/// </remarks>
	[LuaStackEffect(0)]
	public static unsafe bool TryRead(LuaState state, int index, out CEObject value)
	{
		if (state.TypeOf(index) == LuaType.Userdata)
		{
			IntPtr block = state.ToUserdata(index);
			if (block != 0 && state.RawLength(index) >= (nuint) sizeof(nint))
			{
				IntPtr pointer = *(nint*) block;
				if (pointer != 0)
				{
					value = new CEObject(pointer);
					return true;
				}
			}
		}

		value = default;
		return false;
	}

	/// <summary>
	///     Pushes the property <paramref name="name" /> of the object under protection (<c>obj[name]</c>; the metatable's
	///     <c>__index</c> runs inside the call). Stack after success: the value (<c>nil</c> for a member the object does
	///     not have, if the host does not raise for it); after failure: one error value.
	/// </summary>
	/// <param name="state">The calling thread's state.</param>
	/// <param name="name">The property name as Cheat Engine spells it, UTF-8; a <c>"..."u8</c> literal.</param>
	/// <returns>The status.</returns>
	/// <exception cref="InvalidOperationException">The plugin is not enabled.</exception>
	/// <remarks>
	///     One push, one protected field access, one <c>lua_rotate</c> and one <c>lua_settop</c> to remove the object
	///     again.
	/// </remarks>
	[RequiresPluginEnabled]
	public LuaStatus TryGetProperty(LuaState state, ReadOnlySpan<byte> name)
	{
		// [..] -> [.. obj] -> [.. obj v | .. obj err] -> [.. v | .. err]
		Push(state);
		LuaStatus status = state.TryGetField(-1, name);
		state.Remove(-2);
		return status;
	}

	/// <summary>
	///     Pops the value on top and assigns it to the property <paramref name="name" /> of the object under protection
	///     (<c>obj[name] = v</c>; the metatable's <c>__newindex</c> runs inside the call). Stack after success: the value
	///     is gone; after failure: one error value in its place.
	/// </summary>
	/// <param name="state">The calling thread's state, with the value on top.</param>
	/// <param name="name">The property name, UTF-8.</param>
	/// <returns>The status.</returns>
	/// <exception cref="InvalidOperationException">The plugin is not enabled.</exception>
	[RequiresPluginEnabled]
	public LuaStatus TrySetProperty(LuaState state, ReadOnlySpan<byte> name)
	{
		// [.. v] -> [.. v obj] -> [.. obj v] -> [.. obj | .. obj err] -> [.. | .. err]
		Push(state);
		state.Insert(-2);
		LuaStatus status = state.TrySetField(-2, name);
		RemoveObjectAfterSet(state, status);
		return status;
	}

	/// <summary>
	///     Pushes element <paramref name="zeroBasedIndex" /> of the object under protection (<c>obj[i]</c>, Cheat
	///     Engine's own zero-based index: the found list's addresses, a string list's lines). Stack after success: the
	///     value; after failure: one error value.
	/// </summary>
	/// <param name="state">The calling thread's state.</param>
	/// <param name="zeroBasedIndex">The index as Cheat Engine counts it, from 0; passed through unchanged.</param>
	/// <returns>The status.</returns>
	/// <exception cref="InvalidOperationException">The plugin is not enabled.</exception>
	[RequiresPluginEnabled]
	public LuaStatus TryGetIndex(LuaState state, int zeroBasedIndex)
	{
		Push(state);
		LuaStatus status = state.TryGetIndex(-1, zeroBasedIndex);
		state.Remove(-2);
		return status;
	}

	/// <summary>
	///     Pops the value on top and assigns it to element <paramref name="zeroBasedIndex" /> of the object under
	///     protection (<c>obj[i] = v</c>, zero-based). Stack after success: the value is gone; after failure: one error
	///     value in its place.
	/// </summary>
	/// <param name="state">The calling thread's state, with the value on top.</param>
	/// <param name="zeroBasedIndex">The index as Cheat Engine counts it, from 0; passed through unchanged.</param>
	/// <returns>The status.</returns>
	/// <exception cref="InvalidOperationException">The plugin is not enabled.</exception>
	[RequiresPluginEnabled]
	public LuaStatus TrySetIndex(LuaState state, int zeroBasedIndex)
	{
		Push(state);
		state.Insert(-2);
		LuaStatus status = state.TrySetIndex(-2, zeroBasedIndex);
		RemoveObjectAfterSet(state, status);
		return status;
	}

	/// <summary>
	///     Pushes the method <paramref name="name" /> of the object as the instance-bound function Cheat Engine returns
	///     for <c>obj.name</c>, ready to be called with the declared arguments only (no <c>self</c>). Stack after
	///     success: the function; after failure: one error value, also when the member exists but is not a function.
	/// </summary>
	/// <param name="state">The calling thread's state.</param>
	/// <param name="name">The method name as Cheat Engine spells it, UTF-8.</param>
	/// <returns>
	///     The status; <see cref="LuaStatus.RuntimeError" /> with a message naming the member and the type found when it
	///     is not a function.
	/// </returns>
	/// <exception cref="InvalidOperationException">The plugin is not enabled.</exception>
	/// <remarks>
	///     The straight-line shape of a generated method call is this member, the argument pushes,
	///     <see cref="LuaState.TryCall(int, int)" />, the result reads and a stack restore.
	/// </remarks>
	[RequiresPluginEnabled]
	public LuaStatus TryPushMethod(LuaState state, ReadOnlySpan<byte> name)
	{
		LuaStatus status = TryGetProperty(state, name);
		if (status.IsOk && !state.IsFunction(-1))
		{
			return ReplaceWithNotAFunctionError(state, name);
		}

		return status;
	}

	/// <summary>
	///     Calls the method <paramref name="name" /> with the <paramref name="argumentCount" /> values on top of the stack
	///     as its arguments, under protection. Stack after success: the arguments are replaced by the results
	///     (<paramref name="resultCount" /> of them, or all with <see cref="LuaState.MultipleResults" />); after failure:
	///     the arguments are replaced by one error value.
	/// </summary>
	/// <param name="state">The calling thread's state, with the arguments on top.</param>
	/// <param name="name">The method name, UTF-8.</param>
	/// <param name="argumentCount">Number of arguments already pushed.</param>
	/// <param name="resultCount">Number of results to keep, or <see cref="LuaState.MultipleResults" />.</param>
	/// <returns>The status of the lookup or of the call.</returns>
	/// <exception cref="InvalidOperationException">The plugin is not enabled.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="argumentCount" /> is negative.</exception>
	/// <remarks>
	///     Costs one <c>lua_rotate</c> more than <see cref="TryPushMethod" /> followed by the pushes and the call,
	///     because the function has to move below the arguments.
	/// </remarks>
	[RequiresPluginEnabled]
	public LuaStatus TryCallMethod(LuaState state, ReadOnlySpan<byte> name, int argumentCount, int resultCount)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(argumentCount);

		// [.. a1..aN] -> [.. a1..aN f | .. a1..aN err] -> [.. f a1..aN | .. err]
		LuaStatus status = TryPushMethod(state, name);
		if (argumentCount <= 0)
		{
			return status.IsOk ? state.TryCall(argumentCount, resultCount) : status;
		}

		state.Insert(-argumentCount - 1);
		if (!status.IsOk)
		{
			state.Pop(argumentCount);
		}

		return status.IsOk ? state.TryCall(argumentCount, resultCount) : status;
	}

	/// <summary>
	///     Calls <c>destroy()</c> on the object under protection. Stack after success: unchanged; after failure: one
	///     error value.
	/// </summary>
	/// <param name="state">The calling thread's state.</param>
	/// <returns>The status.</returns>
	/// <remarks>
	///     Internal on purpose: destruction is <see cref="Owned{T}" />'s privilege, so that a borrowed handle cannot free
	///     what Cheat Engine still uses.
	/// </remarks>
	internal LuaStatus TryDestroy(LuaState state)
	{
		return TryCallMethod(state, DestroyMethodName, 0, 0);
	}

	/// <summary>
	///     The property read of a straight-line body: pushes the object, then <c>obj[name]</c> under protection, and
	///     <b>leaves the object on the stack</b> below the result, for the body's single <see cref="LuaState.SetTop" /> to
	///     remove together with everything else. Stack after success: the object, then the value; after failure: the
	///     object, then one error value.
	/// </summary>
	/// <param name="state">The calling thread's state.</param>
	/// <param name="name">The property name, UTF-8.</param>
	/// <returns>The status.</returns>
	/// <exception cref="InvalidOperationException">The plugin is not enabled.</exception>
	/// <remarks>
	///     Generator-facing (the shape the typed members and generated wrappers use): the body records the top, calls
	///     this, reads at <c>-1</c> and restores the top, which is two C API calls fewer per access than
	///     <see cref="TryGetProperty(LuaState, ReadOnlySpan{byte})" /> (no <c>lua_rotate</c> + <c>lua_settop</c> to
	///     remove the object, since the final restore removes it anyway). Not for hand-written frame code, whose
	///     protocol expects the object gone. Five C API calls: the push, <c>rawgetp</c>, <c>pushvalue</c>,
	///     <c>pushlstring</c>, <c>pcallk</c>.
	/// </remarks>
	[EditorBrowsable(EditorBrowsableState.Never)]
	[RequiresPluginEnabled]
	public LuaStatus TryGetPropertyLeavingObject(LuaState state, ReadOnlySpan<byte> name)
	{
		// [..] -> [.. obj] -> [.. obj v | .. obj err]
		Push(state);
		return state.TryGetField(-1, name);
	}

	/// <summary>
	///     The method push of a straight-line body: <see cref="TryGetPropertyLeavingObject" /> plus the not-a-function
	///     check of <see cref="TryPushMethod" />. Stack after success: the object, then the bound function, ready for the
	///     argument pushes and <see cref="LuaState.TryCall(int, int)" />; after failure: the object, then one error value.
	///     The object stays below for the body's single <see cref="LuaState.SetTop" />.
	/// </summary>
	/// <param name="state">The calling thread's state.</param>
	/// <param name="name">The method name, UTF-8.</param>
	/// <returns>
	///     The status; <see cref="LuaStatus.RuntimeError" /> with the same message as <see cref="TryPushMethod" /> when
	///     the member is not a function.
	/// </returns>
	/// <exception cref="InvalidOperationException">The plugin is not enabled.</exception>
	/// <remarks>Generator-facing; see <see cref="TryGetPropertyLeavingObject" />. Six C API calls on the success path.</remarks>
	[EditorBrowsable(EditorBrowsableState.Never)]
	[RequiresPluginEnabled]
	public LuaStatus TryPushMethodLeavingObject(LuaState state, ReadOnlySpan<byte> name)
	{
		LuaStatus status = TryGetPropertyLeavingObject(state, name);
		if (status.IsOk && !state.IsFunction(-1))
		{
			return ReplaceWithNotAFunctionError(state, name);
		}

		return status;
	}

	/// <summary>
	///     Reads the property <paramref name="name" /> as a <typeparamref name="TValue" /> through
	///     <typeparamref name="TMarshaller" />: acquires the state, reads under protection, restores the stack. Allocates
	///     nothing beyond what <typeparamref name="TMarshaller" /> allocates (a <see cref="StringMarshaller" /> read
	///     allocates the string; value marshallers allocate nothing).
	/// </summary>
	/// <typeparam name="TMarshaller">
	///     The marshaller of the property's type (<see cref="Int32Marshaller" />,
	///     <see cref="StringMarshaller" />, <see cref="Address" />, <see cref="CEObject" />, ...).
	/// </typeparam>
	/// <typeparam name="TValue">
	///     The property's managed type; never a span into Lua's memory, because the stack is restored
	///     before the method returns.
	/// </typeparam>
	/// <param name="name">The property name, UTF-8.</param>
	/// <param name="value">The value, or <see langword="default" /> on failure.</param>
	/// <returns>
	///     <see langword="false" /> when the access raised or the value is not of the expected kind (a <c>nil</c>
	///     property reads as <see langword="false" />).
	/// </returns>
	/// <exception cref="InvalidOperationException">The plugin is not enabled.</exception>
	/// <remarks>
	///     Nine transitions with a one-call marshaller: the provider, <c>gettop</c>, the five of
	///     <see cref="TryGetPropertyLeavingObject" />, the read, <c>settop</c>.
	/// </remarks>
	[RequiresPluginEnabled]
	public bool TryGetProperty<TMarshaller, TValue>(ReadOnlySpan<byte> name, [MaybeNullWhen(false)] out TValue value)
		where TMarshaller : struct, ILuaMarshaller<TValue>
	{
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		int top = state.Top;
		value = default!;
		try
		{
			// [..] -> [.. obj v]: the object stays under the value; the one SetTop below removes both.
			if (!TryGetPropertyLeavingObject(state, name).IsOk)
			{
				return false;
			}

			return TMarshaller.TryRead(state, -1, out value);
		}
		finally
		{
			// A consumer-supplied marshaller is allowed to throw. It is never allowed to strand the object, result,
			// or any partial value it pushed on this thread's CE Lua stack.
			state.SetTop(top);
		}
	}

	/// <summary>
	///     Writes the property <paramref name="name" /> from a <typeparamref name="TValue" /> through
	///     <typeparamref name="TMarshaller" />: acquires the state, pushes, assigns under protection, restores the stack.
	///     Allocates nothing on the managed side beyond what <typeparamref name="TMarshaller" /> allocates (the shipped
	///     marshallers allocate nothing on push; a <see cref="StringMarshaller" /> push transcodes through a stack buffer).
	/// </summary>
	/// <typeparam name="TMarshaller">The marshaller of the property's type.</typeparam>
	/// <typeparam name="TValue">
	///     The property's managed type; a <see cref="ReadOnlySpan{T}" /> of UTF-8 bytes is allowed (
	///     <see cref="Utf8Marshaller" />).
	/// </typeparam>
	/// <param name="name">The property name, UTF-8.</param>
	/// <param name="value">The value to assign.</param>
	/// <returns><see langword="false" /> when the assignment raised.</returns>
	/// <exception cref="InvalidOperationException">The plugin is not enabled.</exception>
	/// <remarks>
	///     Ten transitions with a one-call marshaller: the provider, <c>gettop</c>, the push of the object, the push of
	///     the value, the five of <see cref="LuaState.TrySetField" />, <c>settop</c>. The object is pushed before the value so
	///     that no <c>lua_rotate</c> is needed to order them.
	/// </remarks>
	[RequiresPluginEnabled]
	public bool TrySetProperty<TMarshaller, TValue>(ReadOnlySpan<byte> name, TValue value)
		where TMarshaller : struct, ILuaMarshaller<TValue>
		where TValue : allows ref struct
	{
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		int top = state.Top;
		try
		{
			// [..] -> [.. obj] -> [.. obj v] -> [.. obj | .. obj err]
			Push(state);
			TMarshaller.Push(state, value);
			return state.TrySetField(-2, name).IsOk;
		}
		finally
		{
			// See TryGetProperty<TMarshaller, TValue>: push implementations are consumer code too.
			state.SetTop(top);
		}
	}

	/// <summary>
	///     Calls the method <paramref name="name" /> with no arguments and discards its results: acquires the state,
	///     calls under protection, restores the stack. Allocates nothing.
	/// </summary>
	/// <param name="name">The method name, UTF-8.</param>
	/// <returns><see langword="false" /> when the member is not a function or the call raised.</returns>
	/// <exception cref="InvalidOperationException">The plugin is not enabled.</exception>
	/// <remarks>
	///     Ten transitions: the provider, <c>gettop</c>, the six of <see cref="TryPushMethodLeavingObject" />,
	///     <c>pcallk</c>, <c>settop</c>.
	/// </remarks>
	[RequiresPluginEnabled]
	public bool TryCallMethod(ReadOnlySpan<byte> name)
	{
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		int top = state.Top;
		try
		{
			// [..] -> [.. obj f] -> [.. obj | .. obj err]
			return TryPushMethodLeavingObject(state, name).IsOk && state.TryCall(0, 0).IsOk;
		}
		finally
		{
			state.SetTop(top);
		}
	}

	/// <summary>
	///     Calls the method <paramref name="name" /> with no arguments and reads its first result as a
	///     <typeparamref name="TResult" /> through <typeparamref name="TMarshaller" />: acquires the state, calls under
	///     protection, restores the stack. Allocates nothing beyond what <typeparamref name="TMarshaller" /> allocates (a
	///     <see cref="StringMarshaller" /> read allocates the string; value marshallers allocate nothing).
	/// </summary>
	/// <typeparam name="TMarshaller">The marshaller of the result's type.</typeparam>
	/// <typeparam name="TResult">The result's managed type; never a span into Lua's memory.</typeparam>
	/// <param name="name">The method name, UTF-8.</param>
	/// <param name="result">The result, or <see langword="default" /> on failure.</param>
	/// <returns>
	///     <see langword="false" /> when the member is not a function, the call raised, or the result is not of the
	///     expected kind (Cheat Engine's <c>nil</c> for "failed").
	/// </returns>
	/// <exception cref="InvalidOperationException">The plugin is not enabled.</exception>
	/// <remarks>
	///     Eleven transitions with a one-call marshaller: the ten of <see cref="TryCallMethod(ReadOnlySpan{byte})" />
	///     plus the read.
	/// </remarks>
	[RequiresPluginEnabled]
	public bool TryCallMethod<TMarshaller, TResult>(ReadOnlySpan<byte> name, [MaybeNullWhen(false)] out TResult result)
		where TMarshaller : struct, ILuaMarshaller<TResult>
	{
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		int top = state.Top;
		result = default!;
		try
		{
			// [..] -> [.. obj f] -> [.. obj r | .. obj err]
			if (!TryPushMethodLeavingObject(state, name).IsOk || !state.TryCall(0, 1).IsOk)
			{
				return false;
			}

			return TMarshaller.TryRead(state, -1, out result);
		}
		finally
		{
			// A result marshaller can run arbitrary managed code. The stack postcondition does not rely on it returning.
			state.SetTop(top);
		}
	}

	// After a protected set the object sits under nothing (success) or under the error value (failure).
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void RemoveObjectAfterSet(LuaState state, LuaStatus status)
	{
		if (status.IsOk)
		{
			state.Pop(1);
		}
		else
		{
			state.Remove(-2);
		}
	}

	// Replaces the non-function member on top with an error message, in a stack buffer: "'name' is a <type>, not a method".
	[MethodImpl(MethodImplOptions.NoInlining)]
	[SkipLocalsInit] // Every byte of the buffer that is pushed is written first.
	private static LuaStatus ReplaceWithNotAFunctionError(LuaState state, ReadOnlySpan<byte> name)
	{
		const int maxNameBytes = 64;
		ReadOnlySpan<byte> typeName = state.TypeName(-1);
		Span<byte> message = stackalloc byte[maxNameBytes + 64];
		int length = 0;
		Append(message, ref length, "'"u8);
		Append(message, ref length, name.Length <= maxNameBytes ? name : name[..maxNameBytes]);
		Append(message, ref length, "' is a "u8);
		Append(message, ref length, typeName);
		Append(message, ref length, ", not a method"u8);

		state.Pop(1);
		state.PushString(message[..length]);
		return LuaStatus.RuntimeError;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void Append(Span<byte> buffer, ref int length, ReadOnlySpan<byte> text)
	{
		text.CopyTo(buffer[length..]);
		length += text.Length;
	}
}
