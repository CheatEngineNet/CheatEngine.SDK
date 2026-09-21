using System;
using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Inspection;

/// <summary>A non-empty user-defined symbol name registered in Cheat Engine's target symbol handler.</summary>
/// <remarks>
///     This value names a registration; it is not a <see cref="SymbolExpression" />, which may include module offsets
///     and other syntax interpreted during lookup. The name is forwarded to CE as UTF-8 without normalization and owns
///     no native or Lua resource.
/// </remarks>
public readonly struct SymbolName : IEquatable<SymbolName>, ILuaMarshaller<SymbolName>
{
    /// <summary>Creates a user-defined symbol name.</summary>
    /// <param name="value">The non-empty name supplied to CE's <c>registerSymbol</c> and <c>unregisterSymbol</c> globals.</param>
    /// <exception cref="ArgumentException"><paramref name="value" /> is null, empty or white space.</exception>
    public SymbolName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A symbol name must not be empty or white space.", nameof(value));

        Value = value;
    }

    /// <summary>Gets the original symbol name.</summary>
    public string Value { get; }

    /// <inheritdoc />
    public bool Equals(SymbolName other)
    {
        return StringComparer.Ordinal.Equals(Value, other.Value);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is SymbolName other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return Value is null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
    }

    /// <summary>Returns the original symbol name.</summary>
    /// <returns>The name supplied to Cheat Engine.</returns>
    public override string ToString()
    {
        return Value ?? string.Empty;
    }

    /// <summary>Tests two symbol names with ordinal comparison.</summary>
    /// <param name="left">The first symbol name.</param>
    /// <param name="right">The second symbol name.</param>
    /// <returns><see langword="true" /> when the names have the same ordinal text.</returns>
    public static bool operator ==(SymbolName left, SymbolName right)
    {
        return left.Equals(right);
    }

    /// <summary>Tests two symbol names with ordinal comparison.</summary>
    /// <param name="left">The first symbol name.</param>
    /// <param name="right">The second symbol name.</param>
    /// <returns><see langword="true" /> when the names have different ordinal text.</returns>
    public static bool operator !=(SymbolName left, SymbolName right)
    {
        return !left.Equals(right);
    }

    /// <inheritdoc />
    [LuaStackEffect(1)]
    public static void Push(LuaState state, SymbolName value)
    {
        StringMarshaller.Push(state, value.Value);
    }

    /// <inheritdoc />
    [LuaStackEffect(0)]
    public static bool TryRead(LuaState state, int index, out SymbolName value)
    {
        if (StringMarshaller.TryRead(state, index, out var text) && !string.IsNullOrWhiteSpace(text))
        {
            value = new SymbolName(text);
            return true;
        }

        value = default;
        return false;
    }
}
