using System;
using System.Diagnostics.CodeAnalysis;
using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.AddressList;

/// <summary>A borrowed handle to one Cheat Engine address-list entry.</summary>
/// <remarks>
///     <para>
///         A memory record is owned by its address list. It has no disposal member, and every API here returns or accepts
///         it as
///         a borrowed handle. A record can be removed or invalidated by the GUI at any time; this value cannot detect that
///         lifecycle transition.
///     </para>
///     <para>
///         <b>Evidence.</b> Exact installed CE 7.7.0.10621 x64 <c>celua.txt</c>, SHA-256
///         <c>AA1342B4A5D5D5C65B255FB3A8FD7B6BCBBAC1CD138961669D9F37F43E0B9C00</c>, lines 2328-2453. The record belongs to
///         the
///         address-list GUI domain, so main-thread affinity is inferred rather than claimed as a completed live-runtime
///         probe.
///         The members intentionally carry no <c>MainThreadOnly</c> metadata until that probe is complete.
///     </para>
///     <para>
///         Each operation is a protected Lua call. A missing property, <c>nil</c> result, wrong Lua kind, or protected
///         error
///         becomes <see langword="false" />; it does not preserve an allocated Lua error message. Host detachment or a
///         missing
///         host-object pusher is a lifecycle violation surfaced by the underlying SDK as
///         <see cref="InvalidOperationException" />.
///     </para>
/// </remarks>
/// <remarks>Wraps an untyped Cheat Engine object handle without validating its runtime class.</remarks>
/// <param name="handle">The handle; <see cref="CEObject.Null" /> gives <see cref="Null" />.</param>
public readonly struct MemoryRecord(CEObject handle)
    : IEquatable<MemoryRecord>, ICEObject<MemoryRecord>, ILuaMarshaller<MemoryRecord>
{
    /// <summary>Gets the handle that names no memory record.</summary>
    public static MemoryRecord Null => default;

    /// <inheritdoc />
    public CEObject Handle { get; } = handle;

    /// <summary>Gets a value indicating whether this value names no memory record.</summary>
    public bool IsNull => Handle.IsNull;

    /// <inheritdoc />
    public static MemoryRecord FromHandle(CEObject handle)
    {
        return new MemoryRecord(handle);
    }

    /// <summary>Tests two memory-record handles for native-object identity.</summary>
    /// <param name="left">The first handle.</param>
    /// <param name="right">The second handle.</param>
    /// <returns><see langword="true" /> when both handles name the same object.</returns>
    public static bool operator ==(MemoryRecord left, MemoryRecord right)
    {
        return left.Handle == right.Handle;
    }

    /// <summary>Tests two memory-record handles for native-object inequality.</summary>
    /// <param name="left">The first handle.</param>
    /// <param name="right">The second handle.</param>
    /// <returns><see langword="true" /> when the handles name different objects.</returns>
    public static bool operator !=(MemoryRecord left, MemoryRecord right)
    {
        return !(left == right);
    }

    /// <inheritdoc />
    public bool Equals(MemoryRecord other)
    {
        return this == other;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is MemoryRecord other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return Handle.GetHashCode();
    }

    /// <summary>Formats the underlying native-object identity for diagnostics.</summary>
    /// <returns><c>MemoryRecord(CEObject@0x...)</c>, or <c>MemoryRecord(null)</c>.</returns>
    public override string ToString()
    {
        return IsNull ? "MemoryRecord(null)" : "MemoryRecord(" + Handle + ")";
    }

    /// <inheritdoc />
    [LuaStackEffect(1)]
    public static void Push(LuaState state, MemoryRecord value)
    {
        value.Handle.Push(state);
    }

    /// <inheritdoc />
    [LuaStackEffect(0)]
    public static bool TryRead(LuaState state, int index, out MemoryRecord value)
    {
        if (CEObject.TryRead(state, index, out var handle))
        {
            value = new MemoryRecord(handle);
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Gets CE's unique identifier for this record.</summary>
    /// <param name="id">The identifier; default on failure.</param>
    /// <returns><see langword="true" /> when the <c>ID</c> property was a 32-bit integer.</returns>
    [RequiresPluginEnabled]
    public bool TryGetId(out MemoryRecordId id)
    {
        return Handle.TryGetProperty<MemoryRecordId, MemoryRecordId>("ID"u8, out id);
    }

    /// <summary>Gets this record's zero-based position in its current address list.</summary>
    /// <param name="zeroBasedIndex">The position; default on failure.</param>
    /// <returns><see langword="true" /> when the <c>Index</c> property was a 32-bit integer.</returns>
    [RequiresPluginEnabled]
    public bool TryGetIndex(out int zeroBasedIndex)
    {
        return Handle.TryGetProperty<Int32Marshaller, int>("Index"u8, out zeroBasedIndex);
    }

    /// <summary>Gets the record's display description.</summary>
    /// <param name="description">A newly allocated managed string; default on failure.</param>
    /// <returns><see langword="true" /> when CE returned a Lua string.</returns>
    [RequiresPluginEnabled]
    public bool TryGetDescription([MaybeNullWhen(false)] out string description)
    {
        return Handle.TryGetProperty<StringMarshaller, string>("Description"u8, out description);
    }

    /// <summary>Sets the record's display description.</summary>
    /// <param name="description">The non-null display text to pass to CE.</param>
    /// <returns><see langword="true" /> when the assignment completed without a protected Lua error.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="description" /> is <see langword="null" />.</exception>
    [RequiresPluginEnabled]
    public bool TrySetDescription(string description)
    {
        ArgumentNullException.ThrowIfNull(description);
        return Handle.TrySetProperty<StringMarshaller, string>("Description"u8, description);
    }

    /// <summary>Gets CE's interpretable address expression, not the resolved target address.</summary>
    /// <param name="addressExpression">A newly allocated expression string; default on failure.</param>
    /// <returns><see langword="true" /> when CE returned a Lua string.</returns>
    [RequiresPluginEnabled]
    public bool TryGetAddressExpression([MaybeNullWhen(false)] out string addressExpression)
    {
        return Handle.TryGetProperty<StringMarshaller, string>("Address"u8, out addressExpression);
    }

    /// <summary>Sets CE's interpretable address expression.</summary>
    /// <param name="addressExpression">The non-null CE expression, for example a symbol or hexadecimal address.</param>
    /// <returns><see langword="true" /> when the assignment completed without a protected Lua error.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="addressExpression" /> is <see langword="null" />.</exception>
    [RequiresPluginEnabled]
    public bool TrySetAddressExpression(string addressExpression)
    {
        ArgumentNullException.ThrowIfNull(addressExpression);
        return Handle.TrySetProperty<StringMarshaller, string>("Address"u8, addressExpression);
    }

    /// <summary>Gets the record's string-form value.</summary>
    /// <param name="value">A newly allocated managed string; default on failure.</param>
    /// <returns><see langword="true" /> when CE returned a Lua string.</returns>
    [RequiresPluginEnabled]
    public bool TryGetValue([MaybeNullWhen(false)] out string value)
    {
        return Handle.TryGetProperty<StringMarshaller, string>("Value"u8, out value);
    }

    /// <summary>Sets the record's string-form value.</summary>
    /// <param name="value">The non-null value text to pass to CE.</param>
    /// <returns><see langword="true" /> when the assignment completed without a protected Lua error.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value" /> is <see langword="null" />.</exception>
    [RequiresPluginEnabled]
    public bool TrySetValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Handle.TrySetProperty<StringMarshaller, string>("Value"u8, value);
    }

    /// <summary>Gets the numeric variable type from CE's <c>Type</c> property.</summary>
    /// <param name="variableType">The value type; default on failure.</param>
    /// <returns><see langword="true" /> when CE returned an integer that fits <see cref="VariableType" />.</returns>
    [RequiresPluginEnabled]
    public bool TryGetVariableType(out VariableType variableType)
    {
        return Handle.TryGetProperty<EnumMarshaller<VariableType>, VariableType>("Type"u8, out variableType);
    }

    /// <summary>Sets the numeric variable type through CE's <c>Type</c> property.</summary>
    /// <param name="variableType">The CE variable type.</param>
    /// <returns><see langword="true" /> when the assignment completed without a protected Lua error.</returns>
    [RequiresPluginEnabled]
    public bool TrySetVariableType(VariableType variableType)
    {
        return Handle.TrySetProperty<EnumMarshaller<VariableType>, VariableType>("Type"u8, variableType);
    }

    /// <summary>Gets the current resolved target address through CE's <c>getCurrentAddress()</c> method.</summary>
    /// <param name="address">The target-process address; default on failure.</param>
    /// <returns><see langword="true" /> when CE returned a numeric address.</returns>
    [RequiresPluginEnabled]
    public bool TryGetCurrentAddress(out Address address)
    {
        return Handle.TryCallMethod<Address, Address>("getCurrentAddress"u8, out address);
    }

    /// <summary>Gets a direct child by its zero-based child position.</summary>
    /// <param name="zeroBasedIndex">The position in CE's <c>Child[index]</c> accessor; 0 is the first child.</param>
    /// <param name="child">A borrowed, Cheat-Engine-owned child; default when there is no child at the index.</param>
    /// <returns><see langword="true" /> when CE returned a record rather than <c>nil</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="zeroBasedIndex" /> is negative.</exception>
    [RequiresPluginEnabled]
    public bool TryGetChild(int zeroBasedIndex, [CEOwned] out MemoryRecord child)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(zeroBasedIndex);
        return AddressListCalls.TryGetIndex<MemoryRecord, MemoryRecord>(Handle, zeroBasedIndex, out child);
    }

    /// <summary>Gets this record's parent record.</summary>
    /// <param name="parent">A borrowed, Cheat-Engine-owned parent; default for a root record.</param>
    /// <returns><see langword="true" /> when CE returned a parent rather than <c>nil</c>.</returns>
    [RequiresPluginEnabled]
    public bool TryGetParent([CEOwned] out MemoryRecord parent)
    {
        return Handle.TryGetProperty<MemoryRecord, MemoryRecord>("Parent"u8, out parent);
    }
}
