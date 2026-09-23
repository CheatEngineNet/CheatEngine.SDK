using System;

using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Inspection;

/// <summary>
///     A borrowed handle to Cheat Engine's <c>SymbolList</c> class: a list of symbols that maps names to addresses and
///     addresses to names, and that can be registered with Cheat Engine's symbol handler.
/// </summary>
/// <remarks>
///     <para>
///         <b>Ownership.</b> The handle owns nothing and has no destroy, register or unregister member. A list created by
///         <see cref="SymbolLists.TryCreate" /> belongs in <see cref="Owned{T}" />; registering it transfers that owner
///         into a <see cref="SymbolListRegistrationLease" />, which unregisters before destroying. The main list returned
///         by <see cref="SymbolLists.TryGetMain" /> belongs to Cheat Engine: it is only ever a borrowed handle, and no SDK
///         API can register, unregister or destroy it. The <c>ccodesymbols</c> list of an Auto Assembler disable-info
///         table is Cheat Engine's too and is never exposed as an owner.
///     </para>
///     <para>
///         <b>Evidence.</b> CE 7.7.0.10621 <c>celua.txt</c> (SHA-256
///         <c>AA1342B4A5D5D5C65B255FB3A8FD7B6BCBBAC1CD138961669D9F37F43E0B9C00</c>), <c>SymbolList</c> class, lines
///         3523-3563; a Lua-only host observation of 2026-09-22 confirmed that <c>createSymbolList()</c> returns an
///         unregistered caller-owned object destroyed once. The optional <c>addSymbol</c> arguments
///         (<c>skipAddressToSymbolLookup</c>, <c>extradata</c>) and the module members are not projected.
///     </para>
///     <para>
///         <b>Results.</b> Every member runs protected calls, restores the Lua stack, and returns a
///         <see cref="LuaOperationStatus" />: <see cref="LuaOperationStatusKind.GlobalUnavailable" /> when the member is
///         absent, <see cref="LuaOperationStatusKind.LuaFailure" /> when an access or call raised (including every call on
///         a list Cheat Engine already destroyed), <see cref="LuaOperationStatusKind.NilResult" /> for a documented
///         <c>nil</c> (a symbol that is not found), and <see cref="LuaOperationStatusKind.InvalidResult" /> for any other
///         shape. No category is derived from Lua error text. Thread affinity is not established: no
///         <c>MainThreadOnly</c> claim is made.
///     </para>
/// </remarks>
public readonly struct SymbolList : IEquatable<SymbolList>, ICEObject<SymbolList>, ILuaMarshaller<SymbolList>
{
	/// <summary>Wraps an untyped Cheat Engine object handle as a SymbolList handle.</summary>
	/// <param name="handle">The native host-object handle; its runtime class is not checked.</param>
	/// <remarks>A pure value operation: it grants no ownership.</remarks>
	public SymbolList(CEObject handle)
	{
		Handle = handle;
	}

	/// <summary>Gets the handle that names no SymbolList.</summary>
	public static SymbolList Null => default;

	/// <inheritdoc />
	public CEObject Handle
	{
		get;
	}

	/// <summary>Gets a value indicating whether this handle names no object.</summary>
	public bool IsNull => Handle.IsNull;

	/// <inheritdoc />
	public static SymbolList FromHandle(CEObject handle)
	{
		return new SymbolList(handle);
	}

	/// <summary>Compares two SymbolList handles by native-object identity.</summary>
	/// <param name="left">The first handle.</param>
	/// <param name="right">The second handle.</param>
	public static bool operator ==(SymbolList left, SymbolList right)
	{
		return left.Handle == right.Handle;
	}

	/// <summary>Compares two SymbolList handles by native-object identity.</summary>
	/// <param name="left">The first handle.</param>
	/// <param name="right">The second handle.</param>
	public static bool operator !=(SymbolList left, SymbolList right)
	{
		return left.Handle != right.Handle;
	}

	/// <inheritdoc />
	public bool Equals(SymbolList other)
	{
		return Handle == other.Handle;
	}

	/// <inheritdoc />
	public override bool Equals(object? obj)
	{
		return obj is SymbolList other && Equals(other);
	}

	/// <inheritdoc />
	public override int GetHashCode()
	{
		return Handle.GetHashCode();
	}

	/// <summary>Formats the native-object identity for diagnostics.</summary>
	/// <returns><c>SymbolList(CEObject@0x...)</c>, or <c>SymbolList(null)</c>.</returns>
	public override string ToString()
	{
		return IsNull ? "SymbolList(null)" : "SymbolList(" + Handle + ")";
	}

	/// <inheritdoc />
	[RequiresPluginEnabled]
	[LuaStackEffect(1)]
	public static void Push(LuaState state, SymbolList value)
	{
		value.Handle.Push(state);
	}

	/// <inheritdoc />
	[LuaStackEffect(0)]
	public static bool TryRead(LuaState state, int index, out SymbolList value)
	{
		if (CEObject.TryRead(state, index, out CEObject handle))
		{
			value = FromHandle(handle);
			return true;
		}

		value = default;
		return false;
	}

	/// <summary>Removes every symbol from this list with CE's <c>clear()</c>.</summary>
	/// <returns>The protected call outcome.</returns>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
	[RequiresPluginEnabled]
	public LuaOperationStatus TryClear()
	{
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		int top = state.Top;
		try
		{
			LuaOperationStatus status = TryPushMember(state, "clear"u8);
			return status.IsSuccess ? Call(state, 0, 0) : status;
		}
		catch (LuaException exception)
		{
			return LuaOperationStatus.LuaFailure(exception.Status);
		}
		finally
		{
			state.SetTop(top);
		}
	}

	/// <summary>
	///     Adds a symbol with CE's <c>addSymbol(modulename, searchkey, address, symbolsize)</c>, passing exactly these four
	///     arguments.
	/// </summary>
	/// <param name="moduleName">The module name recorded for the symbol.</param>
	/// <param name="searchKey">The name the symbol is found by; forwarded as exact UTF-8, embedded NUL included.</param>
	/// <param name="address">The target address of the symbol.</param>
	/// <param name="size">The symbol extent in bytes.</param>
	/// <returns>The protected call outcome.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="moduleName" /> or <paramref name="searchKey" /> is null.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="size" /> is negative.</exception>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
	[RequiresPluginEnabled]
	public LuaOperationStatus TryAddSymbol(string moduleName, string searchKey, Address address, int size)
	{
		ArgumentNullException.ThrowIfNull(moduleName);
		ArgumentNullException.ThrowIfNull(searchKey);
		ArgumentOutOfRangeException.ThrowIfNegative(size);

		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		int top = state.Top;
		try
		{
			LuaOperationStatus status = TryPushMember(state, "addSymbol"u8);
			if (!status.IsSuccess)
			{
				return status;
			}

			StringMarshaller.Push(state, moduleName);
			StringMarshaller.Push(state, searchKey);
			Address.Push(state, address);
			state.PushInteger(size);
			return Call(state, 4, 0);
		}
		catch (LuaException exception)
		{
			return LuaOperationStatus.LuaFailure(exception.Status);
		}
		finally
		{
			state.SetTop(top);
		}
	}

	/// <summary>Deletes the symbol with <paramref name="searchKey" /> with CE's <c>deleteSymbol(searchkey)</c>.</summary>
	/// <param name="searchKey">The search key of the symbol to delete.</param>
	/// <returns>The protected call outcome.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="searchKey" /> is null.</exception>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
	[RequiresPluginEnabled]
	public LuaOperationStatus TryDeleteSymbol(string searchKey)
	{
		ArgumentNullException.ThrowIfNull(searchKey);

		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		int top = state.Top;
		try
		{
			LuaOperationStatus status = TryPushMember(state, "deleteSymbol"u8);
			if (!status.IsSuccess)
			{
				return status;
			}

			StringMarshaller.Push(state, searchKey);
			return Call(state, 1, 0);
		}
		catch (LuaException exception)
		{
			return LuaOperationStatus.LuaFailure(exception.Status);
		}
		finally
		{
			state.SetTop(top);
		}
	}

	/// <summary>Deletes the symbol at <paramref name="address" /> with CE's <c>deleteSymbol(address)</c>.</summary>
	/// <param name="address">The target address of the symbol to delete.</param>
	/// <returns>The protected call outcome.</returns>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
	[RequiresPluginEnabled]
	public LuaOperationStatus TryDeleteSymbol(Address address)
	{
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		int top = state.Top;
		try
		{
			LuaOperationStatus status = TryPushMember(state, "deleteSymbol"u8);
			if (!status.IsSuccess)
			{
				return status;
			}

			Address.Push(state, address);
			return Call(state, 1, 0);
		}
		catch (LuaException exception)
		{
			return LuaOperationStatus.LuaFailure(exception.Status);
		}
		finally
		{
			state.SetTop(top);
		}
	}

	/// <summary>Finds the symbol whose range contains <paramref name="address" /> with CE's <c>getSymbolFromAddress</c>.</summary>
	/// <param name="address">The target address to look up; it does not have to be the symbol's start.</param>
	/// <param name="symbol">The copied symbol on success; default otherwise.</param>
	/// <returns>
	///     <see cref="LuaOperationStatusKind.Success" />, <see cref="LuaOperationStatusKind.NilResult" /> when no symbol
	///     contains the address, or <see cref="LuaOperationStatusKind.InvalidResult" /> for a malformed symbol table.
	/// </returns>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
	[RequiresPluginEnabled]
	public LuaOperationStatus TryGetSymbolFromAddress(Address address, out SymbolInfo symbol)
	{
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		int top = state.Top;
		symbol = default;
		try
		{
			LuaOperationStatus status = TryPushMember(state, "getSymbolFromAddress"u8);
			if (!status.IsSuccess)
			{
				return status;
			}

			Address.Push(state, address);
			return CallForSymbol(state, out symbol);
		}
		catch (LuaException exception)
		{
			return LuaOperationStatus.LuaFailure(exception.Status);
		}
		finally
		{
			state.SetTop(top);
		}
	}

	/// <summary>Finds the symbol with <paramref name="searchKey" /> with CE's <c>getSymbolFromString</c>.</summary>
	/// <param name="searchKey">The search key to look up.</param>
	/// <param name="symbol">The copied symbol on success; default otherwise.</param>
	/// <returns>
	///     <see cref="LuaOperationStatusKind.Success" />, <see cref="LuaOperationStatusKind.NilResult" /> when the key is
	///     not found, or <see cref="LuaOperationStatusKind.InvalidResult" /> for a malformed symbol table.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="searchKey" /> is null.</exception>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
	[RequiresPluginEnabled]
	public LuaOperationStatus TryGetSymbolFromString(string searchKey, out SymbolInfo symbol)
	{
		ArgumentNullException.ThrowIfNull(searchKey);

		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		int top = state.Top;
		symbol = default;
		try
		{
			LuaOperationStatus status = TryPushMember(state, "getSymbolFromString"u8);
			if (!status.IsSuccess)
			{
				return status;
			}

			StringMarshaller.Push(state, searchKey);
			return CallForSymbol(state, out symbol);
		}
		catch (LuaException exception)
		{
			return LuaOperationStatus.LuaFailure(exception.Status);
		}
		finally
		{
			state.SetTop(top);
		}
	}

	/// <summary>Reads CE's <c>Name</c> property: an optional name that identifies the list.</summary>
	/// <param name="name">A copied managed string on success; <see langword="null" /> otherwise.</param>
	/// <returns>
	///     <see cref="LuaOperationStatusKind.Success" /> for a string, <see cref="LuaOperationStatusKind.NilResult" /> for
	///     <c>nil</c>, <see cref="LuaOperationStatusKind.InvalidResult" /> for any other type.
	/// </returns>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
	[RequiresPluginEnabled]
	public LuaOperationStatus TryGetName(out string? name)
	{
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		int top = state.Top;
		name = null;
		try
		{
			LuaStatus status = Handle.TryGetPropertyLeavingObject(state, "Name"u8);
			if (!status.IsOk)
			{
				return LuaOperationStatus.LuaFailure(status);
			}

			if (state.IsNil(-1))
			{
				return LuaOperationStatus.NilResult;
			}

			return state.TryReadString(-1, out name) ? LuaOperationStatus.Success : LuaOperationStatus.InvalidResult;
		}
		catch (LuaException exception)
		{
			return LuaOperationStatus.LuaFailure(exception.Status);
		}
		finally
		{
			state.SetTop(top);
		}
	}

	/// <summary>Sets CE's <c>Name</c> property.</summary>
	/// <param name="name">The name to assign.</param>
	/// <returns>The protected assignment outcome.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="name" /> is null.</exception>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
	[RequiresPluginEnabled]
	public LuaOperationStatus TrySetName(string name)
	{
		ArgumentNullException.ThrowIfNull(name);

		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		int top = state.Top;
		try
		{
			StringMarshaller.Push(state, name);
			LuaStatus status = Handle.TrySetProperty(state, "Name"u8);
			return status.IsOk ? LuaOperationStatus.Success : LuaOperationStatus.LuaFailure(status);
		}
		catch (LuaException exception)
		{
			return LuaOperationStatus.LuaFailure(exception.Status);
		}
		finally
		{
			state.SetTop(top);
		}
	}

	/// <summary>Reads CE's <c>PID</c> property: the process identifier the list refers to.</summary>
	/// <param name="processId">The process identifier on success; 0 otherwise.</param>
	/// <returns>
	///     <see cref="LuaOperationStatusKind.Success" /> for a 32-bit integer, <see cref="LuaOperationStatusKind.NilResult" />
	///     for <c>nil</c>, <see cref="LuaOperationStatusKind.InvalidResult" /> for any other value.
	/// </returns>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
	[RequiresPluginEnabled]
	public LuaOperationStatus TryGetProcessId(out int processId)
	{
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		int top = state.Top;
		processId = 0;
		try
		{
			LuaStatus status = Handle.TryGetPropertyLeavingObject(state, "PID"u8);
			if (!status.IsOk)
			{
				return LuaOperationStatus.LuaFailure(status);
			}

			if (state.IsNil(-1))
			{
				return LuaOperationStatus.NilResult;
			}

			return state.IsInteger(-1) && Int32Marshaller.TryRead(state, -1, out processId)
				? LuaOperationStatus.Success
				: LuaOperationStatus.InvalidResult;
		}
		catch (LuaException exception)
		{
			return LuaOperationStatus.LuaFailure(exception.Status);
		}
		finally
		{
			state.SetTop(top);
		}
	}

	/// <summary>
	///     Calls the argument-less <c>register()</c> or <c>unregister()</c> of this list on <paramref name="state" />. Only
	///     <see cref="SymbolLists" /> and <see cref="SymbolListRegistrationLease" /> call it, for a list they own; the stack
	///     is restored by the caller.
	/// </summary>
	/// <param name="state">The calling thread's admitted state.</param>
	/// <param name="method"><c>register</c> or <c>unregister</c>, UTF-8.</param>
	/// <param name="invoked">Whether the protected call itself began.</param>
	internal LuaOperationStatus TryInvokeRegistration(LuaState state, ReadOnlySpan<byte> method, out bool invoked)
	{
		invoked = false;
		try
		{
			LuaOperationStatus status = TryPushMember(state, method);
			if (!status.IsSuccess)
			{
				return status;
			}

			invoked = true;
			return Call(state, 0, 0);
		}
		catch (LuaException exception)
		{
			return LuaOperationStatus.LuaFailure(exception.Status);
		}
	}

	// Pushes obj.member (the bound function Cheat Engine returns), leaving the object below it for the caller's restore:
	// GlobalUnavailable when the member is absent, InvalidResult when it is not a function.
	private LuaOperationStatus TryPushMember(LuaState state, ReadOnlySpan<byte> member)
	{
		LuaStatus status = Handle.TryGetPropertyLeavingObject(state, member);
		if (!status.IsOk)
		{
			return LuaOperationStatus.LuaFailure(status);
		}

		if (state.IsNil(-1))
		{
			return LuaOperationStatus.GlobalUnavailable;
		}

		return state.IsFunction(-1) ? LuaOperationStatus.Success : LuaOperationStatus.InvalidResult;
	}

	private static LuaOperationStatus Call(LuaState state, int argumentCount, int resultCount)
	{
		LuaStatus status = state.TryCall(argumentCount, resultCount);
		return status.IsOk ? LuaOperationStatus.Success : LuaOperationStatus.LuaFailure(status);
	}

	private static LuaOperationStatus CallForSymbol(LuaState state, out SymbolInfo symbol)
	{
		symbol = default;
		LuaStatus status = state.TryCall(1, 1);
		if (!status.IsOk)
		{
			return LuaOperationStatus.LuaFailure(status);
		}

		if (state.IsNil(-1))
		{
			return LuaOperationStatus.NilResult;
		}

		return SymbolTableReader.TryRead(state, -1, out symbol)
			? LuaOperationStatus.Success
			: LuaOperationStatus.InvalidResult;
	}
}
