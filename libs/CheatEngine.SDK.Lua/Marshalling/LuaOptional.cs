using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace CheatEngine.SDK.Lua.Marshalling;

/// <summary>
///     Creates <see cref="LuaOptional{T}" /> values. The factories live on this non-generic type so that a call site
///     infers the type argument (<c>LuaOptional.Of(5)</c>) instead of naming it on the generic type.
/// </summary>
public static class LuaOptional
{
	/// <summary>
	///     An omitted optional value: nothing is pushed for an argument, or Lua returned no value at a result position.
	///     Equal to <c>default(LuaOptional&lt;T&gt;)</c>.
	/// </summary>
	/// <typeparam name="T">The value type.</typeparam>
	public static LuaOptional<T> Omitted<T>()
		where T : notnull
	{
		return default;
	}

	/// <summary>An explicit Lua <c>nil</c>: pushed as <c>nil</c> for an argument, or a <c>nil</c> result value.</summary>
	/// <typeparam name="T">The value type.</typeparam>
	public static LuaOptional<T> Nil<T>()
		where T : notnull
	{
		return new LuaOptional<T>(LuaOptional<T>.NilState, default!);
	}

	/// <summary>A present value.</summary>
	/// <typeparam name="T">The value type.</typeparam>
	/// <param name="value">The value; never <see langword="null" />: <c>nil</c> is <see cref="Nil{T}" />.</param>
	/// <exception cref="ArgumentNullException"><paramref name="value" /> is a <see langword="null" /> reference.</exception>
	public static LuaOptional<T> Of<T>(T value)
		where T : notnull
	{
		return LuaOptional<T>.FromValue(value);
	}
}

/// <summary>
///     One optional Lua value in exactly one of three states: <see cref="IsOmitted" /> (no value at all: an argument that
///     is not pushed, or a result position Lua did not return), <see cref="IsNil" /> (an explicit Lua <c>nil</c>) or
///     <see cref="HasValue" />. Cheat Engine functions can behave differently for an omitted argument and a <c>nil</c>
///     one, and a function that returns nothing is not one that returns <c>nil</c>: the three states are never merged.
/// </summary>
/// <typeparam name="T">
///     The value type. <see langword="string" /> is allowed, <see langword="string" />? is not: <c>nil</c> is
///     <see cref="IsNil" />, never a <see langword="null" /> string.
/// </typeparam>
/// <remarks>
///     <para>
///         <c>default(LuaOptional&lt;T&gt;)</c> is <see cref="IsOmitted" />, so a caller writes <see langword="default" />
///         to omit an argument. There is no implicit conversion from <typeparamref name="T" />: the state is always
///         chosen explicitly with <see cref="LuaOptional.Of{T}" />, <see cref="LuaOptional.Nil{T}" /> or
///         <see cref="LuaOptional.Omitted{T}" />.
///     </para>
///     <para>
///         The type is an ordinary (non-<see langword="ref" />) struct, so a span cannot be optional; an optional text value
///         is a <c>LuaOptional&lt;string&gt;</c>. Creating one allocates nothing beyond the value itself.
///     </para>
/// </remarks>
public readonly struct LuaOptional<T> : IEquatable<LuaOptional<T>>
	where T : notnull
{
	internal const byte OmittedState = 0;
	internal const byte NilState = 1;
	internal const byte ValueState = 2;

	// Read before the null test so that a value type never reaches 'value is null', which boxes in unoptimized code.
	private static readonly bool s_canBeNull = !typeof(T).IsValueType;

	private readonly T _value;
	private readonly byte _state;

	internal LuaOptional(byte state, T value)
	{
		_state = state;
		_value = value;
	}

	internal static LuaOptional<T> FromValue(T value)
	{
		if (s_canBeNull && value is null)
		{
			throw new ArgumentNullException(nameof(value),
				"A LuaOptional value cannot be null; use LuaOptional.Nil<T>() to pass Lua nil.");
		}

		return new LuaOptional<T>(ValueState, value);
	}

	/// <summary>Gets whether no value is present at all (the <see langword="default" /> state).</summary>
	public bool IsOmitted => _state == OmittedState;

	/// <summary>Gets whether the value is an explicit Lua <c>nil</c>.</summary>
	public bool IsNil => _state == NilState;

	/// <summary>Gets whether a value is present.</summary>
	public bool HasValue => _state == ValueState;

	/// <summary>Gets the present value.</summary>
	/// <exception cref="InvalidOperationException">The value is omitted or <c>nil</c>.</exception>
	public T Value => _state == ValueState
		? _value
		: throw new InvalidOperationException(_state == NilState
			? "The optional Lua value is nil, not a value."
			: "The optional Lua value is omitted, not a value.");

	/// <summary>Compares the state and, when both hold a value, the values.</summary>
	/// <param name="left">The first value.</param>
	/// <param name="right">The second value.</param>
	public static bool operator ==(LuaOptional<T> left, LuaOptional<T> right)
	{
		return left.Equals(right);
	}

	/// <summary>Compares the state and, when both hold a value, the values.</summary>
	/// <param name="left">The first value.</param>
	/// <param name="right">The second value.</param>
	public static bool operator !=(LuaOptional<T> left, LuaOptional<T> right)
	{
		return !left.Equals(right);
	}

	/// <summary>Gets the value when one is present.</summary>
	/// <param name="value">The value, or <see langword="default" /> when omitted or <c>nil</c>.</param>
	/// <returns><see langword="true" /> exactly when <see cref="HasValue" />.</returns>
	public bool TryGetValue([MaybeNullWhen(false)] out T value)
	{
		value = _value;
		return _state == ValueState;
	}

	/// <inheritdoc />
	public bool Equals(LuaOptional<T> other)
	{
		return _state == other._state
			   && (_state != ValueState || EqualityComparer<T>.Default.Equals(_value, other._value));
	}

	/// <inheritdoc />
	public override bool Equals([NotNullWhen(true)] object? obj)
	{
		return obj is LuaOptional<T> other && Equals(other);
	}

	/// <inheritdoc />
	public override int GetHashCode()
	{
		return _state == ValueState ? HashCode.Combine(_state, _value) : _state;
	}

	/// <summary>Returns <c>&lt;omitted&gt;</c>, <c>nil</c>, or the value formatted with the invariant culture.</summary>
	public override string ToString()
	{
		return _state switch
		{
			ValueState => _value is IFormattable formattable
				? formattable.ToString(null, CultureInfo.InvariantCulture)
				: _value.ToString() ?? string.Empty,
			NilState => "nil",
			_ => "<omitted>"
		};
	}
}
