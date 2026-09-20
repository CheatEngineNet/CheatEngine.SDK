using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.AddressList;

/// <summary>A Cheat Engine memory-record identifier, distinct from its zero-based position in an address list.</summary>
/// <remarks>
///     <para>
///         CE 7.7's <c>MemoryRecord.ID</c> is documented as a unique integer, while <c>MemoryRecord.Index</c> is the
///         record's
///         position. This type preserves that distinction at the API boundary without imposing a range CE does not
///         document.
///     </para>
///     <para>
///         <b>Evidence.</b> Exact installed CE 7.7.0.10621 x64 <c>celua.txt</c>, SHA-256
///         <c>AA1342B4A5D5D5C65B255FB3A8FD7B6BCBBAC1CD138961669D9F37F43E0B9C00</c>, line 2332. The documentation's integer
///         representation is mapped through a checked 32-bit Lua integer marshaller.
///     </para>
/// </remarks>
/// <remarks>Creates an identifier from the integer CE exposes.</remarks>
/// <param name="value">The raw identifier; CE documents no invalid sentinel.</param>
public readonly struct MemoryRecordId(int value) : IEquatable<MemoryRecordId>, IComparable<MemoryRecordId>, IComparable,
    ILuaMarshaller<MemoryRecordId>
{
    /// <summary>Gets the integer carried by Cheat Engine's <c>ID</c> property.</summary>
    public int Value { get; } = value;

    /// <summary>Compares two identifiers by their numeric value.</summary>
    /// <param name="left">The first identifier.</param>
    /// <param name="right">The second identifier.</param>
    /// <returns>A negative value, zero, or a positive value.</returns>
    public static int Compare(MemoryRecordId left, MemoryRecordId right)
    {
        return left.Value.CompareTo(right.Value);
    }

    /// <summary>Tests two identifiers for numeric equality.</summary>
    /// <param name="left">The first identifier.</param>
    /// <param name="right">The second identifier.</param>
    /// <returns><see langword="true" /> when both values are equal.</returns>
    public static bool operator ==(MemoryRecordId left, MemoryRecordId right)
    {
        return left.Value == right.Value;
    }

    /// <summary>Tests two identifiers for numeric inequality.</summary>
    /// <param name="left">The first identifier.</param>
    /// <param name="right">The second identifier.</param>
    /// <returns><see langword="true" /> when the values differ.</returns>
    public static bool operator !=(MemoryRecordId left, MemoryRecordId right)
    {
        return left.Value != right.Value;
    }

    /// <summary>Tests whether the first identifier precedes the second one.</summary>
    /// <param name="left">The first identifier.</param>
    /// <param name="right">The second identifier.</param>
    /// <returns><see langword="true" /> when <paramref name="left" /> is lower than <paramref name="right" />.</returns>
    public static bool operator <(MemoryRecordId left, MemoryRecordId right)
    {
        return Compare(left, right) < 0;
    }

    /// <summary>Tests whether the first identifier does not follow the second one.</summary>
    /// <param name="left">The first identifier.</param>
    /// <param name="right">The second identifier.</param>
    /// <returns><see langword="true" /> when <paramref name="left" /> is lower than or equal to <paramref name="right" />.</returns>
    public static bool operator <=(MemoryRecordId left, MemoryRecordId right)
    {
        return Compare(left, right) <= 0;
    }

    /// <summary>Tests whether the first identifier follows the second one.</summary>
    /// <param name="left">The first identifier.</param>
    /// <param name="right">The second identifier.</param>
    /// <returns><see langword="true" /> when <paramref name="left" /> is greater than <paramref name="right" />.</returns>
    public static bool operator >(MemoryRecordId left, MemoryRecordId right)
    {
        return Compare(left, right) > 0;
    }

    /// <summary>Tests whether the first identifier does not precede the second one.</summary>
    /// <param name="left">The first identifier.</param>
    /// <param name="right">The second identifier.</param>
    /// <returns><see langword="true" /> when <paramref name="left" /> is greater than or equal to <paramref name="right" />.</returns>
    public static bool operator >=(MemoryRecordId left, MemoryRecordId right)
    {
        return Compare(left, right) >= 0;
    }

    /// <inheritdoc />
    public int CompareTo(MemoryRecordId other)
    {
        return Compare(this, other);
    }

    /// <inheritdoc />
    public int CompareTo(object? obj)
    {
        if (obj is null) return 1;
        if (obj is MemoryRecordId other) return CompareTo(other);
        throw new ArgumentException("The value must be a MemoryRecordId.", nameof(obj));
    }

    /// <inheritdoc />
    public bool Equals(MemoryRecordId other)
    {
        return this == other;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is MemoryRecordId other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return Value;
    }

    /// <summary>Formats the identifier with invariant decimal digits.</summary>
    /// <returns>The raw integer in decimal.</returns>
    public override string ToString()
    {
        return Value.ToString(CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    [LuaStackEffect(1)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Push(LuaState state, MemoryRecordId value)
    {
        Int32Marshaller.Push(state, value.Value);
    }

    /// <inheritdoc />
    [LuaStackEffect(0)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryRead(LuaState state, int index, out MemoryRecordId value)
    {
        if (Int32Marshaller.TryRead(state, index, out var raw))
        {
            value = new MemoryRecordId(raw);
            return true;
        }

        value = default;
        return false;
    }
}
