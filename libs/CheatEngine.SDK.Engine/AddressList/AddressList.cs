using System;
using System.Diagnostics.CodeAnalysis;
using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.AddressList;

/// <summary>A borrowed handle to Cheat Engine's GUI address list.</summary>
/// <remarks>
///     <para>
///         This is a value handle only; it has no <c>Dispose</c> member. The object returned by <c>getAddressList()</c> is
///         owned
///         by Cheat Engine's main form, and records returned from it are owned by that address list. Do not put either in
///         <see cref="Owned{T}" />.
///     </para>
///     <para>
///         <b>Evidence.</b> Exact installed CE 7.7.0.10621 x64 <c>celua.txt</c>, SHA-256
///         <c>AA1342B4A5D5D5C65B255FB3A8FD7B6BCBBAC1CD138961669D9F37F43E0B9C00</c>: global <c>getAddressList</c> at line
///         774;
///         class spelling <c>Addresslist</c>, properties, and methods at lines 2455-2503. It identifies the class as a
///         <c>Panel</c> descendant, so GUI-thread affinity is an inferred host constraint until the dispatcher live probe
///         is
///         complete. The wrapper deliberately has no <c>MainThreadOnly</c> annotation before that probe turns the
///         inference
///         into an enforceable host contract.
///     </para>
///     <para>
///         Operations run as protected Lua calls and return <see langword="false" /> for a protected error, <c>nil</c>, or
///         a
///         value whose kind does not match the declared result. They require an enabled plugin and an attached host-object
///         pusher; before then, the underlying SDK boundary throws <see cref="InvalidOperationException" />.
///     </para>
/// </remarks>
/// <remarks>Wraps an untyped Cheat Engine object handle without validating its runtime class.</remarks>
/// <param name="handle">The handle; <see cref="CEObject.Null" /> gives <see cref="Null" />.</param>
[SuppressMessage("Meziantou.Analyzer", "MA0049",
    Justification =
        "The namespace groups the address-list API, while this type mirrors Cheat Engine's Addresslist class.")]
public readonly struct AddressList(CEObject handle) : IEquatable<AddressList>, ICEObject<AddressList>,
    ILuaMarshaller<AddressList>
{
    /// <summary>Gets the handle that names no address list.</summary>
    public static AddressList Null => default;

    /// <inheritdoc />
    public CEObject Handle { get; } = handle;

    /// <summary>Gets a value indicating whether this value names no address list.</summary>
    public bool IsNull => Handle.IsNull;

    /// <inheritdoc />
    public static AddressList FromHandle(CEObject handle)
    {
        return new AddressList(handle);
    }

    /// <summary>Tests two address-list handles for native-object identity.</summary>
    /// <param name="left">The first handle.</param>
    /// <param name="right">The second handle.</param>
    /// <returns><see langword="true" /> when both handles name the same object.</returns>
    public static bool operator ==(AddressList left, AddressList right)
    {
        return left.Handle == right.Handle;
    }

    /// <summary>Tests two address-list handles for native-object inequality.</summary>
    /// <param name="left">The first handle.</param>
    /// <param name="right">The second handle.</param>
    /// <returns><see langword="true" /> when the handles name different objects.</returns>
    public static bool operator !=(AddressList left, AddressList right)
    {
        return !(left == right);
    }

    /// <inheritdoc />
    public bool Equals(AddressList other)
    {
        return this == other;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is AddressList other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return Handle.GetHashCode();
    }

    /// <summary>Formats the underlying native-object identity for diagnostics.</summary>
    /// <returns><c>AddressList(CEObject@0x...)</c>, or <c>AddressList(null)</c>.</returns>
    public override string ToString()
    {
        return IsNull ? "AddressList(null)" : "AddressList(" + Handle + ")";
    }

    /// <inheritdoc />
    [LuaStackEffect(1)]
    public static void Push(LuaState state, AddressList value)
    {
        value.Handle.Push(state);
    }

    /// <inheritdoc />
    [LuaStackEffect(0)]
    public static bool TryRead(LuaState state, int index, out AddressList value)
    {
        if (CEObject.TryRead(state, index, out var handle))
        {
            value = new AddressList(handle);
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Gets the number of top-level records through CE's <c>getCount()</c> method.</summary>
    /// <param name="count">The count; 0 on failure.</param>
    /// <returns><see langword="true" /> when CE returned a 32-bit integer.</returns>
    [RequiresPluginEnabled]
    public bool TryGetCount(out int count)
    {
        return Handle.TryCallMethod<Int32Marshaller, int>("getCount"u8, out count);
    }

    /// <summary>Gets a record by its zero-based address-list position.</summary>
    /// <param name="zeroBasedIndex">The index CE's address-list object uses; 0 is the first record.</param>
    /// <param name="record">A borrowed, Cheat-Engine-owned record; default on failure.</param>
    /// <returns><see langword="true" /> when a record was returned.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="zeroBasedIndex" /> is negative.</exception>
    [RequiresPluginEnabled]
    public bool TryGetMemoryRecord(int zeroBasedIndex, [CEOwned] out MemoryRecord record)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(zeroBasedIndex);
        return AddressListCalls.TryCall<Int32Marshaller, int, MemoryRecord, MemoryRecord>(Handle, "getMemoryRecord"u8,
            zeroBasedIndex, out record);
    }

    /// <summary>Gets a record by CE's unique memory-record identifier.</summary>
    /// <param name="id">The identifier from <see cref="MemoryRecord.TryGetId" />.</param>
    /// <param name="record">A borrowed, Cheat-Engine-owned record; default when no matching record exists.</param>
    /// <returns><see langword="true" /> when CE returned a record rather than <c>nil</c>.</returns>
    [RequiresPluginEnabled]
    public bool TryGetMemoryRecordById(MemoryRecordId id, [CEOwned] out MemoryRecord record)
    {
        return AddressListCalls.TryCall<MemoryRecordId, MemoryRecordId, MemoryRecord, MemoryRecord>(Handle,
            "getMemoryRecordByID"u8, id, out record);
    }

    /// <summary>Gets the main selected record.</summary>
    /// <param name="record">A borrowed, Cheat-Engine-owned record; default when no record is selected.</param>
    /// <returns><see langword="true" /> when CE returned a record rather than <c>nil</c>.</returns>
    [RequiresPluginEnabled]
    public bool TryGetSelectedRecord([CEOwned] out MemoryRecord record)
    {
        return Handle.TryCallMethod<MemoryRecord, MemoryRecord>("getSelectedRecord"u8, out record);
    }

    /// <summary>Sets CE's main selected record and clears other selections as CE specifies.</summary>
    /// <param name="record">The borrowed record to select.</param>
    /// <returns><see langword="true" /> when CE completed the call without a protected Lua error.</returns>
    [RequiresPluginEnabled]
    public bool TrySetSelectedRecord([CEOwned] MemoryRecord record)
    {
        return AddressListCalls.TryCall<MemoryRecord, MemoryRecord>(Handle, "setSelectedRecord"u8, record);
    }

    /// <summary>Creates a generic record and adds it to this address list.</summary>
    /// <param name="record">The newly added, borrowed, Cheat-Engine-owned record; default on failure.</param>
    /// <returns><see langword="true" /> when CE returned the added record rather than <c>nil</c>.</returns>
    /// <remarks>
    ///     CE attaches this object to the address list during creation. It is deliberately not returned as
    ///     <see cref="Owned{T}" />: destroying it independently would leave the address list with a dangling object.
    /// </remarks>
    [RequiresPluginEnabled]
    public bool TryCreateMemoryRecord([CEOwned] out MemoryRecord record)
    {
        return Handle.TryCallMethod<MemoryRecord, MemoryRecord>("createMemoryRecord"u8, out record);
    }
}
