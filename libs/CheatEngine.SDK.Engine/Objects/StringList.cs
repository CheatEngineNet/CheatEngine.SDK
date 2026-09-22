using System;
using System.Diagnostics.CodeAnalysis;

using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Objects;

/// <summary>
///     A borrowed handle to Cheat Engine's <c>Stringlist</c> class. The handle has no destroy operation; a list made by
///     <see cref="StringLists.TryCreate" /> or returned by <c>AOBScan</c> belongs in <see cref="Owned{T}" />.
/// </summary>
/// <remarks>
///     <para>
///         CE 7.7.0.10621's <c>celua.txt</c> documents <c>Stringlist</c> as a <c>Strings</c> descendant. Its items and
///         all public indices are zero-based, just like Cheat Engine's object indexer. A list is a native host object:
///         it is never a managed collection, and every operation uses <see cref="CEObject" />'s protected primitives.
///     </para>
///     <para>
///         A successful <c>Try*</c> method has restored the Lua stack before returning. It returns
///         <see langword="false" />
///         when a Lua member raises, is missing, or gives a value of the wrong kind; a detached runtime still throws
///         <see cref="InvalidOperationException" />. CE's published documentation does not state a thread affinity for
///         this class, so this wrapper deliberately has no <c>MainThreadOnly</c> claim pending the CE 7.7 live probe. It
///         uses the calling thread's host Lua state. Disposing <see cref="Owned{T}" /> retains that type's main-thread
///         ownership contract.
///     </para>
///     <para>
///         <c>Duplicates</c> is CE's numeric <c>TDuplicates</c> enum property. The <c>dup*</c> names document the
///         values, but CE exchanges the enum ordinal through Lua.
///     </para>
/// </remarks>
public readonly struct StringList : IEquatable<StringList>, ICEObject<StringList>, ILuaMarshaller<StringList>
{
	private static ReadOnlySpan<byte> CountPropertyName => "Count"u8;

	private static ReadOnlySpan<byte> SortedPropertyName => "Sorted"u8;

	private static ReadOnlySpan<byte> DuplicatesPropertyName => "Duplicates"u8;

	private static ReadOnlySpan<byte> CaseSensitivePropertyName => "CaseSensitive"u8;

	private static ReadOnlySpan<byte> ClearMethodName => "clear"u8;

	private static ReadOnlySpan<byte> AddMethodName => "add"u8;

	private static ReadOnlySpan<byte> DeleteMethodName => "delete"u8;

	private static ReadOnlySpan<byte> GetTextMethodName => "getText"u8;

	private static ReadOnlySpan<byte> SetTextMethodName => "setText"u8;

	private static ReadOnlySpan<byte> IndexOfMethodName => "indexOf"u8;

	/// <summary>Wraps an untyped Cheat Engine object handle as a StringList handle.</summary>
	/// <param name="handle">The native host-object handle; its runtime class is not checked.</param>
	/// <remarks>
	///     This is a pure value operation. Only a factory or API whose CE contract says it returned a StringList should
	///     call it.
	/// </remarks>
	public StringList(CEObject handle)
	{
		Handle = handle;
	}

	/// <summary>Gets the handle that names no StringList.</summary>
	public static StringList Null => default;

	/// <inheritdoc />
	public CEObject Handle
	{
		get;
	}

	/// <summary>Gets a value indicating whether this handle names no object.</summary>
	public bool IsNull => Handle.IsNull;

	/// <inheritdoc />
	public static StringList FromHandle(CEObject handle)
	{
		return new StringList(handle);
	}

	/// <summary>Compares two StringList handles by native-object identity.</summary>
	/// <param name="left">The first handle.</param>
	/// <param name="right">The second handle.</param>
	public static bool operator ==(StringList left, StringList right)
	{
		return left.Handle == right.Handle;
	}

	/// <summary>Compares two StringList handles by native-object identity.</summary>
	/// <param name="left">The first handle.</param>
	/// <param name="right">The second handle.</param>
	public static bool operator !=(StringList left, StringList right)
	{
		return left.Handle != right.Handle;
	}

	/// <inheritdoc />
	public bool Equals(StringList other)
	{
		return Handle == other.Handle;
	}

	/// <inheritdoc />
	public override bool Equals(object? obj)
	{
		return obj is StringList other && Equals(other);
	}

	/// <inheritdoc />
	public override int GetHashCode()
	{
		return Handle.GetHashCode();
	}

	/// <summary>Formats the native-object identity for diagnostics.</summary>
	/// <returns>The underlying <see cref="CEObject" /> representation.</returns>
	public override string ToString()
	{
		return Handle.ToString();
	}

	/// <inheritdoc />
	[RequiresPluginEnabled]
	public static void Push(LuaState state, StringList value)
	{
		value.Handle.Push(state);
	}

	/// <inheritdoc />
	public static bool TryRead(LuaState state, int index, out StringList value)
	{
		if (CEObject.TryRead(state, index, out CEObject handle))
		{
			value = FromHandle(handle);
			return true;
		}

		value = default;
		return false;
	}

	/// <summary>Reads the number of strings currently in the list.</summary>
	/// <param name="count">The count on success; zero on failure.</param>
	/// <returns><see langword="true" /> when CE returned an integer count.</returns>
	[RequiresPluginEnabled]
	public bool TryGetCount(out int count)
	{
		return Handle.TryGetProperty<Int32Marshaller, int>(CountPropertyName, out count);
	}

	/// <summary>Reads a string at a zero-based StringList index.</summary>
	/// <param name="zeroBasedIndex">The StringList index, starting at zero.</param>
	/// <param name="value">The string on success; <see langword="null" /> on failure.</param>
	/// <returns><see langword="true" /> when the index produced a Lua string.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="zeroBasedIndex" /> is negative.</exception>
	[RequiresPluginEnabled]
	public bool TryGetItem(int zeroBasedIndex, [MaybeNullWhen(false)] out string value)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(zeroBasedIndex);

		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		using LuaFrame frame = new(state);
		if (!Handle.TryGetIndex(state, zeroBasedIndex).IsOk)
		{
			value = default!;
			return false;
		}

		return StringMarshaller.TryRead(state, -1, out value);
	}

	/// <summary>Replaces the string at a zero-based StringList index.</summary>
	/// <param name="zeroBasedIndex">The StringList index, starting at zero.</param>
	/// <param name="value">The replacement text.</param>
	/// <returns><see langword="true" /> when CE accepted the assignment.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="zeroBasedIndex" /> is negative.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="value" /> is <see langword="null" />.</exception>
	[RequiresPluginEnabled]
	public bool TrySetItem(int zeroBasedIndex, string value)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(zeroBasedIndex);
		ArgumentNullException.ThrowIfNull(value);

		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		using LuaFrame frame = new(state);
		StringMarshaller.Push(state, value);
		return Handle.TrySetIndex(state, zeroBasedIndex).IsOk;
	}

	/// <summary>Removes every string from this list.</summary>
	/// <returns><see langword="true" /> when CE completed <c>clear()</c>.</returns>
	[RequiresPluginEnabled]
	public bool TryClear()
	{
		return Handle.TryCallMethod(ClearMethodName);
	}

	/// <summary>Adds a string and returns its zero-based StringList index.</summary>
	/// <param name="value">The string to add.</param>
	/// <param name="zeroBasedIndex">The index returned by CE, or zero on failure.</param>
	/// <returns><see langword="true" /> when CE returned an integer index.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="value" /> is <see langword="null" />.</exception>
	[RequiresPluginEnabled]
	public bool TryAdd(string value, out int zeroBasedIndex)
	{
		ArgumentNullException.ThrowIfNull(value);

		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		using LuaFrame frame = new(state);
		StringMarshaller.Push(state, value);
		if (!Handle.TryCallMethod(state, AddMethodName, 1, 1).IsOk)
		{
			zeroBasedIndex = 0;
			return false;
		}

		return Int32Marshaller.TryRead(state, -1, out zeroBasedIndex);
	}

	/// <summary>Deletes the string at a zero-based StringList index.</summary>
	/// <param name="zeroBasedIndex">The StringList index, starting at zero.</param>
	/// <returns><see langword="true" /> when CE completed <c>delete(index)</c>.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="zeroBasedIndex" /> is negative.</exception>
	[RequiresPluginEnabled]
	public bool TryDelete(int zeroBasedIndex)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(zeroBasedIndex);

		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		using LuaFrame frame = new(state);
		Int32Marshaller.Push(state, zeroBasedIndex);
		return Handle.TryCallMethod(state, DeleteMethodName, 1, 0).IsOk;
	}

	/// <summary>Reads all lines as the one string returned by CE's <c>getText()</c>.</summary>
	/// <param name="text">The text on success; <see langword="null" /> on failure.</param>
	/// <returns><see langword="true" /> when CE returned a Lua string.</returns>
	[RequiresPluginEnabled]
	public bool TryGetText([MaybeNullWhen(false)] out string text)
	{
		return Handle.TryCallMethod<StringMarshaller, string>(GetTextMethodName, out text);
	}

	/// <summary>Sets all lines from the text accepted by CE's <c>setText(string)</c>.</summary>
	/// <param name="text">The text to assign.</param>
	/// <returns><see langword="true" /> when CE completed the assignment.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="text" /> is <see langword="null" />.</exception>
	[RequiresPluginEnabled]
	public bool TrySetText(string text)
	{
		ArgumentNullException.ThrowIfNull(text);

		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		using LuaFrame frame = new(state);
		StringMarshaller.Push(state, text);
		return Handle.TryCallMethod(state, SetTextMethodName, 1, 0).IsOk;
	}

	/// <summary>Finds a string through CE's <c>indexOf(string)</c> method.</summary>
	/// <param name="value">The string to find.</param>
	/// <param name="zeroBasedIndex">CE's index, or -1 when no equal string exists; zero on operation failure.</param>
	/// <returns><see langword="true" /> when CE returned an integer result.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="value" /> is <see langword="null" />.</exception>
	[RequiresPluginEnabled]
	public bool TryIndexOf(string value, out int zeroBasedIndex)
	{
		ArgumentNullException.ThrowIfNull(value);

		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		using LuaFrame frame = new(state);
		StringMarshaller.Push(state, value);
		if (!Handle.TryCallMethod(state, IndexOfMethodName, 1, 1).IsOk)
		{
			zeroBasedIndex = 0;
			return false;
		}

		return Int32Marshaller.TryRead(state, -1, out zeroBasedIndex);
	}

	/// <summary>Reads the CE <c>Sorted</c> property.</summary>
	/// <param name="value">The property value, or <see langword="false" /> on failure.</param>
	/// <returns><see langword="true" /> when CE returned a boolean.</returns>
	[RequiresPluginEnabled]
	public bool TryGetSorted(out bool value)
	{
		return Handle.TryGetProperty<BooleanMarshaller, bool>(SortedPropertyName, out value);
	}

	/// <summary>Sets the CE <c>Sorted</c> property.</summary>
	/// <param name="value">Whether CE should keep the list sorted.</param>
	/// <returns><see langword="true" /> when CE accepted the property assignment.</returns>
	[RequiresPluginEnabled]
	public bool TrySetSorted(bool value)
	{
		return Handle.TrySetProperty<BooleanMarshaller, bool>(SortedPropertyName, value);
	}

	/// <summary>Reads the CE <c>Duplicates</c> property.</summary>
	/// <param name="value">The enum value, or <see cref="DuplicateHandling.Ignore" /> on failure.</param>
	/// <returns><see langword="true" /> when CE returned an integer that fits the enum's underlying type.</returns>
	[RequiresPluginEnabled]
	public bool TryGetDuplicates(out DuplicateHandling value)
	{
		return Handle.TryGetProperty<EnumMarshaller<DuplicateHandling>, DuplicateHandling>(DuplicatesPropertyName,
			out value);
	}

	/// <summary>Sets the CE <c>Duplicates</c> property with its numeric <c>TDuplicates</c> value.</summary>
	/// <param name="value">The duplicate handling value.</param>
	/// <returns><see langword="true" /> when CE accepted the property assignment.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="value" /> is not a defined member.</exception>
	[RequiresPluginEnabled]
	public bool TrySetDuplicates(DuplicateHandling value)
	{
		if (value is not (DuplicateHandling.Ignore or DuplicateHandling.Accept or DuplicateHandling.Error))
		{
			ThrowUndefinedDuplicateHandling(value);
		}

		return Handle.TrySetProperty<EnumMarshaller<DuplicateHandling>, DuplicateHandling>(DuplicatesPropertyName,
			value);
	}

	/// <summary>Reads the CE <c>CaseSensitive</c> property.</summary>
	/// <param name="value">The property value, or <see langword="false" /> on failure.</param>
	/// <returns><see langword="true" /> when CE returned a boolean.</returns>
	[RequiresPluginEnabled]
	public bool TryGetCaseSensitive(out bool value)
	{
		return Handle.TryGetProperty<BooleanMarshaller, bool>(CaseSensitivePropertyName, out value);
	}

	/// <summary>Sets the CE <c>CaseSensitive</c> property.</summary>
	/// <param name="value">Whether CE should compare list strings with case sensitivity.</param>
	/// <returns><see langword="true" /> when CE accepted the property assignment.</returns>
	[RequiresPluginEnabled]
	public bool TrySetCaseSensitive(bool value)
	{
		return Handle.TrySetProperty<BooleanMarshaller, bool>(CaseSensitivePropertyName, value);
	}

	[DoesNotReturn]
	private static void ThrowUndefinedDuplicateHandling(DuplicateHandling value)
	{
		throw new ArgumentOutOfRangeException(nameof(value), value,
			"A StringList duplicate policy must be a defined DuplicateHandling value.");
	}
}
